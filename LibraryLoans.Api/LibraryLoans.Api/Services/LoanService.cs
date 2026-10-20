using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LibraryLoans.Api.Api;
using LibraryLoans.Api.Domain;
using LibraryLoans.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Npgsql;
using StackExchange.Redis;

namespace LibraryLoans.Api.Services;

public sealed record LoanCreationResult(LoanResponse Loan, bool Replayed);

public sealed class LoanService(
    LibraryDbContext dbContext,
    IDistributedCache cache,
    ILogger<LoanService> logger)
{
    public async Task<LoanCreationResult> CreateAsync(
        CreateLoanRequest request,
        string? idempotencyKey,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200)
            {
                throw new ApiProblemException(400, "Invalid Idempotency-Key", "Provide a non-empty Idempotency-Key of at most 200 characters.");
            }

            var dueAtUtc = request.DueAtUtc.UtcDateTime;
            if (dueAtUtc <= DateTime.UtcNow)
            {
                throw new ApiProblemException(400, "Invalid due date", "DueAtUtc must be in the future.");
            }

            var fingerprint = CreateFingerprint(request, dueAtUtc);
            var existing = await dbContext.IdempotencyRecords.AsNoTracking()
                .SingleOrDefaultAsync(record => record.Key == idempotencyKey, cancellationToken);
            if (existing is not null)
            {
                return await ReplayAsync(existing, fingerprint, cancellationToken);
            }

            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var readerExists = await dbContext.Readers.AnyAsync(reader => reader.Id == request.ReaderId, cancellationToken);
            if (!readerExists)
            {
                throw new ApiProblemException(404, "Reader not found", "No reader exists with the supplied id.");
            }

            var decremented = await dbContext.Books
                .Where(book => book.Id == request.BookId && book.IsActive && book.AvailableCopies > 0)
                .ExecuteUpdateAsync(update => update.SetProperty(book => book.AvailableCopies, book => book.AvailableCopies - 1)
                    .SetProperty(book => book.UpdatedAtUtc, DateTime.UtcNow), cancellationToken);

            if (decremented == 0)
            {
                var bookExists = await dbContext.Books.AnyAsync(book => book.Id == request.BookId && book.IsActive, cancellationToken);
                if (!bookExists)
                {
                    throw new ApiProblemException(404, "Book not found", "No active book exists with the supplied id.");
                }

                LoanMetrics.RecordUnavailable();
                throw new ApiProblemException(409, "Book unavailable", "No copies of this book are currently available.");
            }

            var loan = new Loan
            {
                Id = Guid.NewGuid(),
                BookId = request.BookId,
                ReaderId = request.ReaderId,
                BorrowedAtUtc = DateTime.UtcNow,
                DueAtUtc = dueAtUtc,
                Status = LoanStatus.Active
            };

            dbContext.Loans.Add(loan);
            dbContext.AuditEvents.Add(AuditWriter.Create(httpContext, "Loan", loan.Id, "LoanCreated", new
            {
                loan.BookId,
                loan.ReaderId,
                loan.BorrowedAtUtc,
                loan.DueAtUtc
            }));
            dbContext.IdempotencyRecords.Add(new IdempotencyRecord
            {
                Key = idempotencyKey,
                RequestFingerprint = fingerprint,
                LoanId = loan.Id,
                CreatedAtUtc = DateTime.UtcNow
            });

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException)
            {
                await transaction.RollbackAsync(cancellationToken);
                var winner = await dbContext.IdempotencyRecords.AsNoTracking()
                    .SingleOrDefaultAsync(record => record.Key == idempotencyKey, cancellationToken);
                if (winner is null)
                {
                    throw;
                }

                return await ReplayAsync(winner, fingerprint, cancellationToken);
            }

            await InvalidateAvailabilityAsync(request.BookId);
            LoanMetrics.RecordCreated();
            logger.LogInformation(
                "Loan created {LoanId} for book {BookId} and reader {ReaderId}; correlation {CorrelationId}",
                loan.Id,
                loan.BookId,
                loan.ReaderId,
                httpContext.TraceIdentifier);
            return new LoanCreationResult(ToResponse(loan), false);
        }
        finally
        {
            LoanMetrics.RecordDuration(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        }
    }

    public async Task<LoanResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var loan = await dbContext.Loans.AsNoTracking().SingleOrDefaultAsync(loan => loan.Id == id, cancellationToken)
            ?? throw new ApiProblemException(404, "Loan not found", "No loan exists with the supplied id.");
        return ToResponse(loan);
    }

    public Task<LoanResponse> ReturnAsync(Guid id, HttpContext context, CancellationToken cancellationToken) =>
        ChangeStatusAsync(id, LoanStatus.Returned, context, cancellationToken);

    public Task<LoanResponse> CancelAsync(Guid id, HttpContext context, CancellationToken cancellationToken) =>
        ChangeStatusAsync(id, LoanStatus.Canceled, context, cancellationToken);

    private async Task<LoanResponse> ChangeStatusAsync(
        Guid id,
        LoanStatus newStatus,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var loan = await dbContext.Loans
            .FromSqlInterpolated($"SELECT * FROM \"Loans\" WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ApiProblemException(404, "Loan not found", "No loan exists with the supplied id.");

        if (loan.Status != LoanStatus.Active)
        {
            throw new ApiProblemException(409, "Loan is not active", "Only active loans can be returned or canceled.");
        }

        var now = DateTime.UtcNow;
        loan.Status = newStatus;
        loan.ReturnedAtUtc = newStatus == LoanStatus.Returned ? now : null;
        loan.CanceledAtUtc = newStatus == LoanStatus.Canceled ? now : null;

        var incremented = await dbContext.Books
            .Where(book => book.Id == loan.BookId && book.AvailableCopies < book.TotalCopies)
            .ExecuteUpdateAsync(update => update.SetProperty(book => book.AvailableCopies, book => book.AvailableCopies + 1)
                .SetProperty(book => book.UpdatedAtUtc, now), cancellationToken);
        if (incremented != 1)
        {
            throw new InvalidOperationException("Loan and book copy counts are inconsistent.");
        }

        var action = newStatus == LoanStatus.Returned ? "LoanReturned" : "LoanCanceled";
        dbContext.AuditEvents.Add(AuditWriter.Create(context, "Loan", loan.Id, action, new
        {
            loan.BookId,
            loan.ReaderId,
            TimestampUtc = now
        }));

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await InvalidateAvailabilityAsync(loan.BookId);
        return ToResponse(loan);
    }

    private async Task<LoanCreationResult> ReplayAsync(
        IdempotencyRecord record,
        string requestFingerprint,
        CancellationToken cancellationToken)
    {
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(record.RequestFingerprint), Convert.FromHexString(requestFingerprint)))
        {
            throw new ApiProblemException(409, "Idempotency-Key conflict", "This key was already used with a different request.");
        }

        var loan = await dbContext.Loans.AsNoTracking().SingleOrDefaultAsync(loan => loan.Id == record.LoanId, cancellationToken)
            ?? throw new InvalidOperationException("The idempotency record references a missing loan.");
        LoanMetrics.RecordReplay();
        return new LoanCreationResult(ToResponse(loan), true);
    }

    private async Task InvalidateAvailabilityAsync(Guid bookId)
    {
        try
        {
            await cache.RemoveAsync("books:catalog");
            await cache.RemoveAsync($"books:{bookId}:availability");
        }
        catch (RedisException exception)
        {
            logger.LogWarning(exception, "Redis invalidation failed after a loan change for book {BookId}", bookId);
        }
    }

    private static string CreateFingerprint(CreateLoanRequest request, DateTime dueAtUtc)
    {
        var payload = JsonSerializer.Serialize(new { request.BookId, request.ReaderId, DueAtUtc = dueAtUtc });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static LoanResponse ToResponse(Loan loan) => new(
        loan.Id,
        loan.BookId,
        loan.ReaderId,
        loan.BorrowedAtUtc,
        loan.DueAtUtc,
        loan.ReturnedAtUtc,
        loan.CanceledAtUtc,
        loan.Status.ToString());
}