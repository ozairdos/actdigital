using LibraryLoans.Api.Api;
using LibraryLoans.Api.Infrastructure;
using LibraryLoans.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LibraryLoans.Api.Controllers;

[ApiController]
[Route("loans")]
public sealed class LoansController(LoanService loanService, LibraryDbContext dbContext) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<LoanResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<LoanResponse>> Create(
        CreateLoanRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var result = await loanService.CreateAsync(request, idempotencyKey, HttpContext, cancellationToken);
        if (result.Replayed)
        {
            return Ok(result.Loan);
        }

        return CreatedAtAction(nameof(GetById), new { id = result.Loan.Id }, result.Loan);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<LoanResponse>> GetById(Guid id, CancellationToken cancellationToken) =>
        Ok(await loanService.GetByIdAsync(id, cancellationToken));

    [HttpGet]
    public async Task<ActionResult<LoanResponse[]>> GetAll(
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 500)
        {
            throw new ApiProblemException(400, "Invalid limit", "Limit must be between 1 and 500.");
        }

        var loans = await dbContext.Loans.AsNoTracking()
            .OrderByDescending(loan => loan.BorrowedAtUtc)
            .Take(limit)
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

    [HttpPost("{id:guid}/return")]
    public async Task<ActionResult<LoanResponse>> Return(Guid id, CancellationToken cancellationToken) =>
        Ok(await loanService.ReturnAsync(id, HttpContext, cancellationToken));

    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<LoanResponse>> Cancel(Guid id, CancellationToken cancellationToken) =>
        Ok(await loanService.CancelAsync(id, HttpContext, cancellationToken));
}