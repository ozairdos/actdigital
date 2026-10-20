using LibraryLoans.Api.Api;
using LibraryLoans.Api.Domain;
using LibraryLoans.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LibraryLoans.Api.Controllers;

[ApiController]
[Route("users")]
public sealed class ReadersController(LibraryDbContext dbContext) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<ReaderResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ReaderResponse>> Create(
        CreateReaderRequest request,
        CancellationToken cancellationToken)
    {
        var reader = new Reader
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };

        dbContext.Readers.Add(reader);
        await dbContext.SaveChangesAsync(cancellationToken);
        var response = new ReaderResponse(reader.Id, reader.Name, reader.CreatedAtUtc);
        return Created($"/users/{reader.Id}", response);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ReaderResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var reader = await dbContext.Readers.AsNoTracking()
            .Where(reader => reader.Id == id)
            .Select(reader => new ReaderResponse(reader.Id, reader.Name, reader.CreatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ApiProblemException(404, "Reader not found", "No reader exists with the supplied id.");

        return Ok(reader);
    }

    [HttpGet("{id:guid}/loans")]
    public async Task<ActionResult<LoanResponse[]>> GetLoans(Guid id, CancellationToken cancellationToken)
    {
        if (!await dbContext.Readers.AnyAsync(reader => reader.Id == id, cancellationToken))
        {
            throw new ApiProblemException(404, "Reader not found", "No reader exists with the supplied id.");
        }

        var loans = await dbContext.Loans.AsNoTracking()
            .Where(loan => loan.ReaderId == id)
            .OrderByDescending(loan => loan.BorrowedAtUtc)
            .Select(loan => new LoanResponse(
                loan.Id,
                loan.BookId,
                loan.ReaderId,
                loan.BorrowedAtUtc,
                loan.DueAtUtc,
                loan.ReturnedAtUtc,
                loan.CanceledAtUtc,
                loan.Status.ToString()))
            .ToArrayAsync(cancellationToken);

        return Ok(loans);
    }
}