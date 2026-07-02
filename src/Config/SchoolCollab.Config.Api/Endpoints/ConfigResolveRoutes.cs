using Microsoft.AspNetCore.Mvc;
using SchoolCollab.Config.Core.CQRS.FeatureFlags.Queries;
using SchoolCollab.Config.Core.DTOs;
using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Config.Api.Endpoints;

/// <summary>
/// Consumer-facing read endpoints. <c>/api/features/global</c> is anonymous so a
/// consumer host can resolve flags at startup without a user session; the
/// tenant-scoped read is authorization-gated when OIDC is enabled. Both return
/// only <see cref="ResolvedFlagDto"/> (Key + IsEnabled) — no value payloads.
/// </summary>
public static class ConfigResolveRoutes
{
    public static WebApplication MapConfigResolveRoutes(this WebApplication app, bool oidcEnabled)
    {
        app.MapGet("/api/features/global", async (
            [FromServices] IQueryHandler<ResolveFlagsForTenant, ResolvedFlagDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ResolveFlagsForTenant(TenantId: null), ct)))
            .AllowAnonymous();

        var tenantRead = app.MapGet("/api/features/{tenantId:guid}", async (
            Guid tenantId,
            [FromServices] IQueryHandler<ResolveFlagsForTenant, ResolvedFlagDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ResolveFlagsForTenant(tenantId), ct)));

        if (oidcEnabled)
        {
            tenantRead.RequireAuthorization();
        }

        return app;
    }
}