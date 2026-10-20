namespace LibraryLoans.Api.Domain;

public sealed class IdempotencyRecord
{
    public required string Key { get; set; }
    public required string RequestFingerprint { get; set; }
    public Guid LoanId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}