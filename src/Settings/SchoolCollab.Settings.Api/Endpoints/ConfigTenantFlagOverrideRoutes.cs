using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.CQRS.FeatureFlags.Commands;
using SchoolCollab.Settings.Core.CQRS.FeatureFlags.Queries;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Api.Endpoints;

public static class ConfigTenantFlagOverrideRoutes
{
    public static RouteGroupBuilder MapConfigTenantOverrideRoutes(this RouteGroupBuilder group)
    {
        // ── List tenant overrides for a flag ──
        group.MapGet("/flags/{key}/overrides",
            async Task<Results<Ok<TenantFlagOverrideDto[]>, NotFound>> (
            string key,
            [FromServices] IQueryHandler<ListTenantOverrides, TenantFlagOverrideDto[]> handler,
            CancellationToken ct) =>
        {
            try { return TypedResults.Ok(await handler.HandleAsync(new ListTenantOverrides(key), ct)); }
            catch (KeyNotFoundException) { return TypedResults.NotFound(); }
        });

        // ── Upsert a tenant override ──
        group.MapPut("/flags/{key}/overrides/{tenantId:guid}",
            async Task<Results<Ok<TenantFlagOverrideDto>, BadRequest<MessageResponse>, NotFound>> (
            string key,
            Guid tenantId,
            [FromBody] UpsertOverrideRequest req,
            [FromServices] ICommandHandler<UpsertTenantFlagOverride, TenantFlagOverrideDto> handler,
            CancellationToken ct) =>
        {
            try
            {
                var result = await handler.HandleAsync(
                    new UpsertTenantFlagOverride(key, tenantId, req.IsEnabled, req.Reason, req.EffectiveFrom, req.EffectiveTo), ct);
                return TypedResults.Ok(result);
            }
            catch (KeyNotFoundException) { return TypedResults.NotFound(); }
            catch (ArgumentException ex) { return TypedResults.BadRequest(new MessageResponse(ex.Message)); }
        });

        // ── Delete a tenant override ──
        group.MapDelete("/flags/{key}/overrides/{tenantId:guid}",
            async Task<Results<NoContent, NotFound>> (
            string key,
            Guid tenantId,
            [FromQuery] string reason,
            [FromServices] ICommandHandler<DeleteTenantFlagOverride> handler,
            CancellationToken ct) =>
        {
            try { await handler.HandleAsync(new DeleteTenantFlagOverride(key, tenantId, reason), ct); return TypedResults.NoContent(); }
            catch (KeyNotFoundException) { return TypedResults.NotFound(); }
        });

        return group;
    }

    public sealed record UpsertOverrideRequest(bool? IsEnabled, string Reason, DateTimeOffset? EffectiveFrom, DateTimeOffset? EffectiveTo);
    public sealed record DeleteOverrideRequest(string Reason);
}
