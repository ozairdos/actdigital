namespace LibraryLoans.Api.Domain;

public sealed class AuditEvent
{
    public Guid Id { get; set; }
    public required string EntityName { get; set; }
    public Guid EntityId { get; set; }
    public required string Action { get; set; }
    public required string Actor { get; set; }
    public DateTime TimestampUtc { get; set; }
    public required string CorrelationId { get; set; }
    public required string Details { get; set; }
}