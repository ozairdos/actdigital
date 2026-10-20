using System.Text.Json;
using LibraryLoans.Api.Domain;

namespace LibraryLoans.Api.Infrastructure;

public static class AuditWriter
{
    public static AuditEvent Create(
        HttpContext context,
        string entityName,
        Guid entityId,
        string action,
        object details) => new()
        {
            Id = Guid.NewGuid(),
            EntityName = entityName,
            EntityId = entityId,
            Action = action,
            Actor = "api",
            TimestampUtc = DateTime.UtcNow,
            CorrelationId = context.TraceIdentifier,
            Details = JsonSerializer.Serialize(details)
        };
}