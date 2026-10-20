namespace LibraryLoans.Api.Domain;

public sealed class Book
{
    public Guid Id { get; set; }
    public required string Title { get; set; }
    public required string Isbn { get; set; }
    public required string Author { get; set; }
    public int TotalCopies { get; set; }
    public int AvailableCopies { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}