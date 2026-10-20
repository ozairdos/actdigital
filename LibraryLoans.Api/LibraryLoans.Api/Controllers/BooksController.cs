using System.Text.Json;
using LibraryLoans.Api.Api;
using LibraryLoans.Api.Domain;
using LibraryLoans.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StackExchange.Redis;

namespace LibraryLoans.Api.Controllers;

[ApiController]
[Route("books")]
public sealed class BooksController(
    LibraryDbContext dbContext,
    IDistributedCache cache,
    ILogger<BooksController> logger) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<BookResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<BookResponse>> Create(CreateBookRequest request, CancellationToken cancellationToken)
    {
        var isbn = NormalizeIsbn(request.Isbn);
        ValidateIsbn(isbn);
        if (request.TotalCopies < 0)
        {
            throw new ApiProblemException(400, "Invalid book", "TotalCopies cannot be negative.");
        }

        var book = new Book
        {
            Id = Guid.NewGuid(),
            Title = request.Title.Trim(),
            Isbn = isbn,
            Author = request.Author.Trim(),
            TotalCopies = request.TotalCopies,
            AvailableCopies = request.TotalCopies,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        dbContext.Books.Add(book);
        dbContext.AuditEvents.Add(AuditWriter.Create(HttpContext, "Book", book.Id, "BookCreated", new
        {
            book.Title,
            book.Isbn,
            book.Author,
            book.TotalCopies
        }));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            throw new ApiProblemException(409, "ISBN already exists", "A book with this ISBN is already registered.");
        }

        await InvalidateCatalogAsync();
        return CreatedAtAction(nameof(GetById), new { id = book.Id }, ToResponse(book));
    }

    [HttpGet]
    [ProducesResponseType<BookResponse[]>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BookResponse[]>> GetAll(CancellationToken cancellationToken)
    {
        var cached = await ReadCacheAsync<BookResponse[]>("books:catalog", cancellationToken);
        if (cached is not null)
        {
            return Ok(cached);
        }

        var books = await dbContext.Books.AsNoTracking()
            .Where(book => book.IsActive)
            .OrderBy(book => book.Title)
            .Select(book => ToResponse(book))
            .ToArrayAsync(cancellationToken);

        await WriteCacheAsync("books:catalog", books, cancellationToken);
        return Ok(books);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<BookResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BookResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var book = await dbContext.Books.AsNoTracking().SingleOrDefaultAsync(book => book.Id == id, cancellationToken)
            ?? throw new ApiProblemException(404, "Book not found", "No book exists with the supplied id.");

        return Ok(ToResponse(book));
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<BookResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BookResponse>> Update(
        Guid id,
        UpdateBookRequest request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var book = await dbContext.Books
            .FromSqlInterpolated($"SELECT * FROM \"Books\" WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ApiProblemException(404, "Book not found", "No book exists with the supplied id.");

        var oldValues = new { book.Title, book.Isbn, book.Author, book.TotalCopies };
        var newIsbn = request.Isbn is null ? book.Isbn : NormalizeIsbn(request.Isbn);
        ValidateIsbn(newIsbn);
        var newTotalCopies = request.TotalCopies ?? book.TotalCopies;
        var borrowedCopies = book.TotalCopies - book.AvailableCopies;
        if (newTotalCopies < borrowedCopies)
        {
            throw new ApiProblemException(409, "Invalid copy count", "TotalCopies cannot be lower than the number of borrowed copies.");
        }

        book.Title = request.Title?.Trim() ?? book.Title;
        book.Isbn = newIsbn;
        book.Author = request.Author?.Trim() ?? book.Author;
        book.AvailableCopies += newTotalCopies - book.TotalCopies;
        book.TotalCopies = newTotalCopies;
        book.UpdatedAtUtc = DateTime.UtcNow;

        dbContext.AuditEvents.Add(AuditWriter.Create(HttpContext, "Book", book.Id, "BookUpdated", new
        {
            Before = oldValues,
            After = new { book.Title, book.Isbn, book.Author, book.TotalCopies }
        }));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            throw new ApiProblemException(409, "ISBN already exists", "A book with this ISBN is already registered.");
        }

        await InvalidateBookCacheAsync(id);
        return Ok(ToResponse(book));
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var book = await dbContext.Books
            .FromSqlInterpolated($"SELECT * FROM \"Books\" WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ApiProblemException(404, "Book not found", "No book exists with the supplied id.");

        if (book.IsActive)
        {
            book.IsActive = false;
            book.UpdatedAtUtc = DateTime.UtcNow;
            dbContext.AuditEvents.Add(AuditWriter.Create(HttpContext, "Book", id, "BookDeactivated", new { book.Isbn }));
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        await InvalidateBookCacheAsync(id);
        return NoContent();
    }

    [HttpGet("{id:guid}/availability")]
    [ProducesResponseType<int>(StatusCodes.Status200OK)]
    public async Task<ActionResult<object>> GetAvailability(Guid id, CancellationToken cancellationToken)
    {
        var cacheKey = $"books:{id}:availability";
        var cached = await ReadCacheAsync<int?>(cacheKey, cancellationToken);
        if (cached.HasValue)
        {
            return Ok(new { bookId = id, availableCopies = cached.Value });
        }

        var availability = await dbContext.Books.AsNoTracking()
            .Where(book => book.Id == id && book.IsActive)
            .Select(book => (int?)book.AvailableCopies)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ApiProblemException(404, "Book not found", "No active book exists with the supplied id.");

        await WriteCacheAsync(cacheKey, availability, cancellationToken);
        return Ok(new { bookId = id, availableCopies = availability });
    }

    [HttpGet("{id:guid}/history")]
    public async Task<ActionResult<LoanResponse[]>> GetHistory(Guid id, CancellationToken cancellationToken)
    {
        if (!await dbContext.Books.AnyAsync(book => book.Id == id, cancellationToken))
        {
            throw new ApiProblemException(404, "Book not found", "No book exists with the supplied id.");
        }

        var loans = await dbContext.Loans.AsNoTracking()
            .Where(loan => loan.BookId == id)
            .OrderByDescending(loan => loan.BorrowedAtUtc)
            .Select(loan => new LoanResponse(
                loan.Id,
                loan.BookId,
                loan.ReaderId,
                loan.BorrowedAtUtc,
                loan.DueAtUtc,
                loan.ReturnedAtUtc,
                loan.CanceledAtUtc,
                loan.Status.ToString()))
            .ToArrayAsync(cancellationToken);

        return Ok(loans);
    }

    private async Task<T?> ReadCacheAsync<T>(string key, CancellationToken cancellationToken)
    {
        try
        {
            var json = await cache.GetStringAsync(key, cancellationToken);
            return json is null ? default : JsonSerializer.Deserialize<T>(json);
        }
        catch (RedisException exception)
        {
            logger.LogWarning(exception, "Redis read failed for cache key {CacheKey}; using PostgreSQL", key);
            return default;
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Invalid cached value for key {CacheKey}; using PostgreSQL", key);
            return default;
        }
    }

    private async Task WriteCacheAsync<T>(string key, T value, CancellationToken cancellationToken)
    {
        try
        {
            await cache.SetStringAsync(key, JsonSerializer.Serialize(value), new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2)
            }, cancellationToken);
        }
        catch (RedisException exception)
        {
            logger.LogWarning(exception, "Redis write failed for cache key {CacheKey}", key);
        }
    }

    private async Task InvalidateCatalogAsync()
    {
        try
        {
            await cache.RemoveAsync("books:catalog");
        }
        catch (RedisException exception)
        {
            logger.LogWarning(exception, "Redis catalog invalidation failed");
        }
    }

    private async Task InvalidateBookCacheAsync(Guid id)
    {
        try
        {
            await cache.RemoveAsync("books:catalog");
            await cache.RemoveAsync($"books:{id}:availability");
        }
        catch (RedisException exception)
        {
            logger.LogWarning(exception, "Redis invalidation failed for book {BookId}", id);
        }
    }

    private static BookResponse ToResponse(Book book) => new(
        book.Id, book.Title, book.Isbn, book.Author, book.TotalCopies, book.AvailableCopies, book.IsActive);

    private static string NormalizeIsbn(string isbn) => isbn.Trim().Replace("-", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();

    private static void ValidateIsbn(string isbn)
    {
        var valid = isbn.Length == 10 && isbn[..9].All(char.IsDigit) && (char.IsDigit(isbn[9]) || isbn[9] == 'X')
            || isbn.Length == 13 && isbn.All(char.IsDigit);
        if (!valid)
        {
            throw new ApiProblemException(400, "Invalid ISBN", "ISBN must contain 10 or 13 valid digits (ISBN-10 may end in X).");
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}