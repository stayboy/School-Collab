using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Config.Core.CQRS.FeatureFlags.Queries;
using SchoolCollab.Config.Core.DTOs;
using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Config.Api.Endpoints;

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