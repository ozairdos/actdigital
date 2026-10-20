using LibraryLoans.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LibraryLoans.Api.Infrastructure;

public sealed class LibraryDbContext(DbContextOptions<LibraryDbContext> options) : DbContext(options)
{
    public DbSet<Book> Books => Set<Book>();
    public DbSet<Reader> Readers => Set<Reader>();
    public DbSet<Loan> Loans => Set<Loan>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Book>(entity =>
        {
            entity.HasKey(book => book.Id);
            entity.HasIndex(book => book.Isbn).IsUnique();
            entity.Property(book => book.Title).HasMaxLength(300).IsRequired();
            entity.Property(book => book.Isbn).HasMaxLength(13).IsRequired();
            entity.Property(book => book.Author).HasMaxLength(200).IsRequired();
            entity.ToTable(table => table.HasCheckConstraint("CK_Books_CopyCounts", "\"TotalCopies\" >= 0 AND \"AvailableCopies\" >= 0 AND \"AvailableCopies\" <= \"TotalCopies\""));
        });

        modelBuilder.Entity<Reader>(entity =>
        {
            entity.HasKey(reader => reader.Id);
            entity.Property(reader => reader.Name).HasMaxLength(200).IsRequired();
        });

        modelBuilder.Entity<Loan>(entity =>
        {
            entity.HasKey(loan => loan.Id);
            entity.HasIndex(loan => new { loan.BookId, loan.BorrowedAtUtc });
            entity.HasIndex(loan => new { loan.ReaderId, loan.BorrowedAtUtc });
            entity.Property(loan => loan.Status).HasConversion<string>().HasMaxLength(20);
            entity.HasOne(loan => loan.Book).WithMany().HasForeignKey(loan => loan.BookId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(loan => loan.Reader).WithMany().HasForeignKey(loan => loan.ReaderId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.HasKey(auditEvent => auditEvent.Id);
            entity.HasIndex(auditEvent => new { auditEvent.EntityName, auditEvent.EntityId, auditEvent.TimestampUtc });
            entity.Property(auditEvent => auditEvent.EntityName).HasMaxLength(100).IsRequired();
            entity.Property(auditEvent => auditEvent.Action).HasMaxLength(100).IsRequired();
            entity.Property(auditEvent => auditEvent.Actor).HasMaxLength(200).IsRequired();
            entity.Property(auditEvent => auditEvent.CorrelationId).HasMaxLength(100).IsRequired();
            entity.Property(auditEvent => auditEvent.Details).HasColumnType("jsonb").IsRequired();
        });

        modelBuilder.Entity<IdempotencyRecord>(entity =>
        {
            entity.HasKey(record => record.Key);
            entity.Property(record => record.Key).HasMaxLength(200);
            entity.Property(record => record.RequestFingerprint).HasMaxLength(64).IsRequired();
            entity.HasIndex(record => record.LoanId);
        });
    }
}