using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.CQRS.FeatureFlags.Commands;
using SchoolCollab.Settings.Core.CQRS.FeatureFlags.Queries;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Api.Endpoints;

public static class ConfigTenantFlagOverrideRoutes
{
    public static RouteGroupBuilder MapConfigTenantOverrideRoutes(this RouteGroupBuilder group, bool requireFlagAdmin)
    {
        // ── List tenant overrides for a flag ──
        group.MapGet("/flags/{key}/overrides", async (
            string key,
            [FromServices] IQueryHandler<ListTenantOverrides, TenantFlagOverrideDto[]> handler,
            CancellationToken ct) =>
        {
            try { return Results.Ok(await handler.HandleAsync(new ListTenantOverrides(key), ct)); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        });

        // ── Upsert a tenant override ──
        group.MapPut("/flags/{key}/overrides/{tenantId:guid}", async (
            string key,
            Guid tenantId,
            [FromBody] UpsertOverrideRequest req,
            [FromServices] ICommandHandler<UpsertTenantFlagOverride, TenantFlagOverrideDto> handler,
            CancellationToken ct) =>
        {
            try
            {
                var result = await handler.HandleAsync(
                    new UpsertTenantFlagOverride(key, tenantId, req.IsEnabled, req.Value, req.Reason, req.EffectiveFrom, req.EffectiveTo), ct);
                return Results.Ok(result);
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (ArgumentException ex) { return Results.BadRequest(new { ex.Message }); }
        }).ApplyAdminPolicy(requireFlagAdmin);

        // ── Delete a tenant override ──
        group.MapDelete("/flags/{key}/overrides/{tenantId:guid}", async (
            string key,
            Guid tenantId,
            [FromQuery] string reason,
            [FromServices] ICommandHandler<DeleteTenantFlagOverride> handler,
            CancellationToken ct) =>
        {
            try { await handler.HandleAsync(new DeleteTenantFlagOverride(key, tenantId, reason), ct); return Results.NoContent(); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        }).ApplyAdminPolicy(requireFlagAdmin);

        return group;
    }

    public sealed record UpsertOverrideRequest(bool? IsEnabled, string? Value, string Reason, DateTimeOffset? EffectiveFrom, DateTimeOffset? EffectiveTo);
    public sealed record DeleteOverrideRequest(string Reason);
}
