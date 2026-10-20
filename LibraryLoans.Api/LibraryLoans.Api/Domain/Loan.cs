namespace LibraryLoans.Api.Domain;

public sealed class Loan
{
    public Guid Id { get; set; }
    public Guid BookId { get; set; }
    public Guid ReaderId { get; set; }
    public DateTime BorrowedAtUtc { get; set; }
    public DateTime DueAtUtc { get; set; }
    public DateTime? ReturnedAtUtc { get; set; }
    public DateTime? CanceledAtUtc { get; set; }
    public LoanStatus Status { get; set; }
    public Book? Book { get; set; }
    public Reader? Reader { get; set; }
}