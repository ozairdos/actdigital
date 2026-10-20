using LibraryLoans.Api.Api;
using LibraryLoans.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace LibraryLoans.Api.Controllers;

[ApiController]
[Route("loans")]
public sealed class LoansController(LoanService loanService) : ControllerBase
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

    [HttpPost("{id:guid}/return")]
    public async Task<ActionResult<LoanResponse>> Return(Guid id, CancellationToken cancellationToken) =>
        Ok(await loanService.ReturnAsync(id, HttpContext, cancellationToken));

    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<LoanResponse>> Cancel(Guid id, CancellationToken cancellationToken) =>
        Ok(await loanService.CancelAsync(id, HttpContext, cancellationToken));
}