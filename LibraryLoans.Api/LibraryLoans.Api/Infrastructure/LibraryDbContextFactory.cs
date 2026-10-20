using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LibraryLoans.Api.Infrastructure;

public sealed class LibraryDbContextFactory : IDesignTimeDbContextFactory<LibraryDbContext>
{
    public LibraryDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? throw new InvalidOperationException("Set ConnectionStrings__Postgres before running EF Core tools.");

        var options = new DbContextOptionsBuilder<LibraryDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new LibraryDbContext(options);
    }
}