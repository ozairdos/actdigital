using System.Text.Json;
using LibraryLoans.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;

namespace LibraryLoans.Api.Infrastructure;

public static class DevelopmentDataSeeder
{
    public static readonly Guid DemoBookId = Guid.Parse("d8a65cb2-c871-4a9f-b5e2-6f713d35b001");
    public static readonly Guid DemoReaderId = Guid.Parse("d8a65cb2-c871-4a9f-b5e2-6f713d35c001");
    public static readonly Guid DemoReaderFourId = Guid.Parse("d8a65cb2-c871-4a9f-b5e2-6f713d35c004");
    public static readonly Guid DemoUnavailableBookId = Guid.Parse("d8a65cb2-c871-4a9f-b5e2-6f713d35b004");
    public static readonly Guid DemoNoStockBookId = Guid.Parse("d8a65cb2-c871-4a9f-b5e2-6f713d35b005");
    public static readonly Guid DemoCanceledBookId = Guid.Parse("d8a65cb2-c871-4a9f-b5e2-6f713d35b006");

    private static readonly Guid DemoBookTwoId = Guid.Parse("d8a65cb2-c871-4a9f-b5e2-6f713d35b002");
    private static readonly Guid DemoBookThreeId = Guid.Parse("d8a65cb2-c871-4a9f-b5e2-6f713d35b003");
    private static readonly Guid DemoReaderTwoId = Guid.Parse("d8a65cb2-c871-4a9f-b5e2-6f713d35c002");
    private static readonly Guid DemoReaderThreeId = Guid.Parse("d8a65cb2-c871-4a9f-b5e2-6f713d35c003");
    private static readonly Guid ActiveLoanId = Guid.Parse("d8a65cb2-c871-4a9f-b5e2-6f713d35d001");
    private static readonly Guid ReturnedLoanId = Guid.Parse("d8a65cb2-c871-4a9f-b5e2-6f713d35d002");
    private static readonly Guid UnavailableLoanId = Guid.Parse("d8a65cb2-c871-4a9f-b5e2-6f713d35d003");
    private static readonly Guid CanceledLoanId = Guid.Parse("d8a65cb2-c871-4a9f-b5e2-6f713d35d004");

    public static async Task SeedAsync(IServiceProvider services, ILogger logger, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var changed = false;

        var books = new[]
        {
            new Book
            {
                Id = DemoBookId,
                Title = "Livro de demonstração: empréstimo ativo",
                Isbn = "9780000000009",
                Author = "Autor de demonstração",
                TotalCopies = 5,
                AvailableCopies = 5,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            },
            new Book
            {
                Id = DemoBookTwoId,
                Title = "Livro de demonstração: histórico devolvido",
                Isbn = "9780000000018",
                Author = "Autora de demonstração",
                TotalCopies = 2,
                AvailableCopies = 2,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            },
            new Book
            {
                Id = DemoBookThreeId,
                Title = "Livro de demonstração: último exemplar",
                Isbn = "9780000000027",
                Author = "Autor de demonstração",
                TotalCopies = 1,
                AvailableCopies = 1,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            },
            new Book
            {
                Id = DemoUnavailableBookId,
                Title = "Livro de demonstração: indisponível por empréstimo ativo",
                Isbn = "9780000000034",
                Author = "Autora de demonstração",
                TotalCopies = 1,
                AvailableCopies = 1,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            },
            new Book
            {
                Id = DemoNoStockBookId,
                Title = "Livro de demonstração: sem exemplares cadastrados",
                Isbn = "9780000000041",
                Author = "Autor de demonstração",
                TotalCopies = 0,
                AvailableCopies = 0,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            },
            new Book
            {
                Id = DemoCanceledBookId,
                Title = "Livro de demonstração: empréstimo cancelado",
                Isbn = "9780000000058",
                Author = "Autora de demonstração",
                TotalCopies = 2,
                AvailableCopies = 2,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            }
        };

        foreach (var book in books)
        {
            if (await dbContext.Books.AnyAsync(existing => existing.Id == book.Id || existing.Isbn == book.Isbn, cancellationToken))
            {
                continue;
            }

            dbContext.Books.Add(book);
            AddAudit(dbContext, book.Id, "BookCreated", new { book.Title, book.Isbn, book.TotalCopies });
            changed = true;
        }

        var readers = new[]
        {
            new Reader { Id = DemoReaderId, Name = "Leitor de demonstração 1", CreatedAtUtc = now },
            new Reader { Id = DemoReaderTwoId, Name = "Leitor de demonstração 2", CreatedAtUtc = now },
            new Reader { Id = DemoReaderThreeId, Name = "Leitor de demonstração 3", CreatedAtUtc = now },
            new Reader { Id = DemoReaderFourId, Name = "Leitor de demonstração 4", CreatedAtUtc = now }
        };

        foreach (var reader in readers)
        {
            if (await dbContext.Readers.AnyAsync(existing => existing.Id == reader.Id, cancellationToken))
            {
                continue;
            }

            dbContext.Readers.Add(reader);
            changed = true;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var activeBook = await dbContext.Books.SingleOrDefaultAsync(book => book.Id == DemoBookId, cancellationToken);
        var activeReader = await dbContext.Readers.SingleOrDefaultAsync(reader => reader.Id == DemoReaderId, cancellationToken);
        if (activeBook is not null && activeReader is not null
            && !await dbContext.Loans.AnyAsync(loan => loan.Id == ActiveLoanId, cancellationToken)
            && activeBook.AvailableCopies > 0)
        {
            activeBook.AvailableCopies--;
            activeBook.UpdatedAtUtc = now;
            var loan = new Loan
            {
                Id = ActiveLoanId,
                BookId = activeBook.Id,
                ReaderId = activeReader.Id,
                BorrowedAtUtc = now.AddDays(-2),
                DueAtUtc = now.AddDays(12),
                Status = LoanStatus.Active
            };
            dbContext.Loans.Add(loan);
            AddAudit(dbContext, loan.Id, "LoanCreated", new { loan.BookId, loan.ReaderId, loan.BorrowedAtUtc, loan.DueAtUtc });
            changed = true;
        }

        var returnedBook = await dbContext.Books.SingleOrDefaultAsync(book => book.Id == DemoBookTwoId, cancellationToken);
        var returnedReader = await dbContext.Readers.SingleOrDefaultAsync(reader => reader.Id == DemoReaderTwoId, cancellationToken);
        if (returnedBook is not null && returnedReader is not null
            && !await dbContext.Loans.AnyAsync(loan => loan.Id == ReturnedLoanId, cancellationToken))
        {
            var loan = new Loan
            {
                Id = ReturnedLoanId,
                BookId = returnedBook.Id,
                ReaderId = returnedReader.Id,
                BorrowedAtUtc = now.AddDays(-20),
                DueAtUtc = now.AddDays(-6),
                ReturnedAtUtc = now.AddDays(-7),
                Status = LoanStatus.Returned
            };
            dbContext.Loans.Add(loan);
            AddAudit(dbContext, loan.Id, "LoanCreated", new { loan.BookId, loan.ReaderId, loan.BorrowedAtUtc, loan.DueAtUtc });
            AddAudit(dbContext, loan.Id, "LoanReturned", new { loan.BookId, loan.ReaderId, TimestampUtc = loan.ReturnedAtUtc });
            changed = true;
        }

        var unavailableBook = await dbContext.Books.SingleOrDefaultAsync(book => book.Id == DemoUnavailableBookId, cancellationToken);
        var unavailableReader = await dbContext.Readers.SingleOrDefaultAsync(reader => reader.Id == DemoReaderThreeId, cancellationToken);
        if (unavailableBook is not null && unavailableReader is not null
            && !await dbContext.Loans.AnyAsync(loan => loan.Id == UnavailableLoanId, cancellationToken)
            && unavailableBook.AvailableCopies > 0)
        {
            unavailableBook.AvailableCopies--;
            unavailableBook.UpdatedAtUtc = now;
            var loan = new Loan
            {
                Id = UnavailableLoanId,
                BookId = unavailableBook.Id,
                ReaderId = unavailableReader.Id,
                BorrowedAtUtc = now.AddDays(-1),
                DueAtUtc = now.AddDays(13),
                Status = LoanStatus.Active
            };
            dbContext.Loans.Add(loan);
            AddAudit(dbContext, loan.Id, "LoanCreated", new { loan.BookId, loan.ReaderId, loan.BorrowedAtUtc, loan.DueAtUtc });
            changed = true;
        }

        var canceledBook = await dbContext.Books.SingleOrDefaultAsync(book => book.Id == DemoCanceledBookId, cancellationToken);
        var canceledReader = await dbContext.Readers.SingleOrDefaultAsync(reader => reader.Id == DemoReaderFourId, cancellationToken);
        if (canceledBook is not null && canceledReader is not null
            && !await dbContext.Loans.AnyAsync(loan => loan.Id == CanceledLoanId, cancellationToken))
        {
            var loan = new Loan
            {
                Id = CanceledLoanId,
                BookId = canceledBook.Id,
                ReaderId = canceledReader.Id,
                BorrowedAtUtc = now.AddDays(-10),
                DueAtUtc = now.AddDays(4),
                CanceledAtUtc = now.AddDays(-8),
                Status = LoanStatus.Canceled
            };
            dbContext.Loans.Add(loan);
            AddAudit(dbContext, loan.Id, "LoanCreated", new { loan.BookId, loan.ReaderId, loan.BorrowedAtUtc, loan.DueAtUtc });
            AddAudit(dbContext, loan.Id, "LoanCanceled", new { loan.BookId, loan.ReaderId, TimestampUtc = loan.CanceledAtUtc });
            changed = true;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        if (changed)
        {
            await InvalidateCatalogCacheAsync(scope.ServiceProvider, logger, cancellationToken);
        }
    }

    private static void AddAudit(LibraryDbContext dbContext, Guid entityId, string action, object details)
    {
        dbContext.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            EntityName = action.StartsWith("Book", StringComparison.Ordinal) ? "Book" : "Loan",
            EntityId = entityId,
            Action = action,
            Actor = "development-seed",
            TimestampUtc = DateTime.UtcNow,
            CorrelationId = "development-seed",
            Details = JsonSerializer.Serialize(details)
        });
    }

    private static async Task InvalidateCatalogCacheAsync(
        IServiceProvider services,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var cache = services.GetRequiredService<IDistributedCache>();
            await cache.RemoveAsync("books:catalog", cancellationToken);
            await cache.RemoveAsync($"books:{DemoBookId}:availability", cancellationToken);
            await cache.RemoveAsync($"books:{DemoBookTwoId}:availability", cancellationToken);
            await cache.RemoveAsync($"books:{DemoBookThreeId}:availability", cancellationToken);
            await cache.RemoveAsync($"books:{DemoUnavailableBookId}:availability", cancellationToken);
            await cache.RemoveAsync($"books:{DemoNoStockBookId}:availability", cancellationToken);
            await cache.RemoveAsync($"books:{DemoCanceledBookId}:availability", cancellationToken);
        }
        catch (RedisException exception)
        {
            logger.LogWarning(exception, "Could not invalidate Redis cache after seeding development data");
        }
    }
}