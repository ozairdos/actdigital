using LibraryLoans.Api.Api;
using LibraryLoans.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LibraryLoans.Api.Controllers;

[ApiController]
[Route("audit-events")]
public sealed class AuditEventsController(LibraryDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AuditEventResponse[]>> Get(
        [FromQuery] string? entityName,
        [FromQuery] Guid? entityId,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 500)
        {
            throw new ApiProblemException(400, "Invalid limit", "Limit must be between 1 and 500.");
        }

        var query = dbContext.AuditEvents.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(entityName))
        {
            query = query.Where(auditEvent => auditEvent.EntityName == entityName);
        }

        if (entityId.HasValue)
        {
            query = query.Where(auditEvent => auditEvent.EntityId == entityId.Value);
        }

        var events = await query.OrderByDescending(auditEvent => auditEvent.TimestampUtc)
            .Take(limit)
            .Select(auditEvent => new AuditEventResponse(
                auditEvent.Id,
                auditEvent.EntityName,
                auditEvent.EntityId,
                auditEvent.Action,
                auditEvent.Actor,
                auditEvent.TimestampUtc,
                auditEvent.CorrelationId,
                auditEvent.Details))
            .ToArrayAsync(cancellationToken);

        return Ok(events);
    }
}