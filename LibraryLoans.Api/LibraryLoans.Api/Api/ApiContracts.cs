using System.ComponentModel.DataAnnotations;

namespace LibraryLoans.Api.Api;

public sealed record CreateBookRequest(
    [property: Required, StringLength(300, MinimumLength = 1)] string Title,
    [property: Required, StringLength(20, MinimumLength = 10)] string Isbn,
    [property: Required, StringLength(200, MinimumLength = 1)] string Author,
    [property: Range(0, int.MaxValue)] int TotalCopies);

public sealed record UpdateBookRequest(
    [property: StringLength(300, MinimumLength = 1)] string? Title,
    [property: StringLength(20, MinimumLength = 10)] string? Isbn,
    [property: StringLength(200, MinimumLength = 1)] string? Author,
    [property: Range(0, int.MaxValue)] int? TotalCopies);

public sealed record BookResponse(
    Guid Id,
    string Title,
    string Isbn,
    string Author,
    int TotalCopies,
    int AvailableCopies,
    bool IsActive);

public sealed record CreateReaderRequest(
    [property: Required, StringLength(200, MinimumLength = 1)] string Name);

public sealed record ReaderResponse(Guid Id, string Name, DateTime CreatedAtUtc);

public sealed record CreateLoanRequest(Guid BookId, Guid ReaderId, DateTimeOffset DueAtUtc);

public sealed record LoanResponse(
    Guid Id,
    Guid BookId,
    Guid ReaderId,
    DateTime BorrowedAtUtc,
    DateTime DueAtUtc,
    DateTime? ReturnedAtUtc,
    DateTime? CanceledAtUtc,
    string Status);

public sealed record AuditEventResponse(
    Guid Id,
    string EntityName,
    Guid EntityId,
    string Action,
    string Actor,
    DateTime TimestampUtc,
    string CorrelationId,
    string Details);