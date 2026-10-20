namespace LibraryLoans.Api.Domain;

public sealed class Reader
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}