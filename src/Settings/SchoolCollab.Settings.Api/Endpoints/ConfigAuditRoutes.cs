using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.CQRS.FeatureFlags.Queries;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Api.Endpoints;

public static class ConfigAuditRoutes
{
    public static RouteGroupBuilder MapConfigAuditRoutes(this RouteGroupBuilder group)
    {
        group.MapGet("/audit", async (
            [FromQuery] string? key,
            [FromQuery] Guid? tenantId,
            [FromQuery] DateTimeOffset? from,
            [FromQuery] DateTimeOffset? to,
            [FromQuery] int? skip,
            [FromQuery] int? take,
            [FromServices] IQueryHandler<ListAuditEntries, FlagAuditEntryDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListAuditEntries(key, tenantId, from, to, skip ?? 0, take ?? 50), ct)));

        return group;
    }
}
