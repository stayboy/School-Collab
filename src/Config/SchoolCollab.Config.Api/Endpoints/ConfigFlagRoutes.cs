using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Config.Core.CQRS.FeatureFlags.Commands;
using SchoolCollab.Config.Core.CQRS.FeatureFlags.Queries;
using SchoolCollab.Config.Core.DTOs;
using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Config.Api.Endpoints;

public static class ConfigFlagRoutes
{
    public static RouteGroupBuilder MapConfigFlagRoutes(this RouteGroupBuilder group, bool requireFlagAdmin)
    {
        // ── Create ──
        group.MapPost("/flags", async (
            [FromBody] CreateFlagRequest req,
            [FromServices] ICommandHandler<CreateFeatureFlag, Guid> handler,
            CancellationToken ct) =>
        {
            try
            {
                var id = await handler.HandleAsync(new CreateFeatureFlag(req.Key, req.Name, req.Description, req.IsEnabled, req.Reason), ct);
                return Results.Created($"/api/config/flags/{Uri.EscapeDataString(req.Key)}", new { id });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
        }).ApplyAdminPolicy(requireFlagAdmin);

        // ── List ──
        group.MapGet("/flags", async (
            [FromQuery] string? search,
            [FromQuery] bool? includeArchived,
            [FromServices] IQueryHandler<ListFeatureFlags, FeatureFlagDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListFeatureFlags(search, includeArchived ?? false), ct)));

        // ── Get ──
        group.MapGet("/flags/{key}", async (
            string key,
            [FromServices] IQueryHandler<GetFeatureFlag, FeatureFlagDto?> handler,
            CancellationToken ct) =>
        {
            var flag = await handler.HandleAsync(new GetFeatureFlag(key), ct);
            return flag is null ? Results.NotFound() : Results.Ok(flag);
        });

        // ── Rename ──
        group.MapPut("/flags/{key}", async (
            string key,
            [FromBody] UpdateFlagRequest req,
            [FromServices] ICommandHandler<RenameFeatureFlag> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new RenameFeatureFlag(key, req.Name, req.Description, req.Reason), ct);
                return Results.NoContent();
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (ArgumentException ex) { return Results.BadRequest(new { ex.Message }); }
        }).ApplyAdminPolicy(requireFlagAdmin);

        // ── Set enabled ──
        group.MapPut("/flags/{key}/enabled", async (
            string key,
            [FromBody] SetEnabledRequest req,
            [FromServices] ICommandHandler<SetFeatureFlagEnabled> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new SetFeatureFlagEnabled(key, req.IsEnabled, req.Reason), ct);
                return Results.NoContent();
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        }).ApplyAdminPolicy(requireFlagAdmin);

        // ── Archive / Unarchive ──
        group.MapPost("/flags/{key}/archive", async (string key, [FromBody] ReasonRequest req, [FromServices] ICommandHandler<ArchiveFeatureFlag> handler, CancellationToken ct) =>
        {
            try { await handler.HandleAsync(new ArchiveFeatureFlag(key, req.Reason), ct); return Results.NoContent(); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        }).ApplyAdminPolicy(requireFlagAdmin);

        group.MapPost("/flags/{key}/unarchive", async (string key, [FromBody] ReasonRequest req, [FromServices] ICommandHandler<UnarchiveFeatureFlag> handler, CancellationToken ct) =>
        {
            try { await handler.HandleAsync(new UnarchiveFeatureFlag(key, req.Reason), ct); return Results.NoContent(); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        }).ApplyAdminPolicy(requireFlagAdmin);

        // ── Delete / Recover ──
        group.MapDelete("/flags/{key}", async (
            string key,
            [FromQuery] string reason,
            [FromServices] ICommandHandler<DeleteFeatureFlag> handler,
            CancellationToken ct) =>
        {
            try { await handler.HandleAsync(new DeleteFeatureFlag(key, reason), ct); return Results.NoContent(); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        }).ApplyAdminPolicy(requireFlagAdmin);

        group.MapPost("/flags/{key}/recover", async (string key, [FromBody] ReasonRequest req, [FromServices] ICommandHandler<RecoverFeatureFlag> handler, CancellationToken ct) =>
        {
            try { await handler.HandleAsync(new RecoverFeatureFlag(key, req.Reason), ct); return Results.NoContent(); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        }).ApplyAdminPolicy(requireFlagAdmin);

        return group;
    }

    public sealed record CreateFlagRequest(string Key, string Name, string? Description, bool IsEnabled, string Reason);
    public sealed record UpdateFlagRequest(string Name, string? Description, string Reason);
    public sealed record SetEnabledRequest(bool IsEnabled, string Reason);
    public sealed record ReasonRequest(string Reason);
}

internal static class AdminPolicyExtensions
{
    /// <summary>
    /// Applies the <c>flag_admin</c> role policy to a write endpoint when OIDC auth
    /// is enabled. In dev (TestAuth) the policy is skipped so the admin UI stays
    /// usable; reads stay cookie-gated.
    /// </summary>
    public static RouteHandlerBuilder ApplyAdminPolicy(this RouteHandlerBuilder builder, bool requireFlagAdmin) =>
        requireFlagAdmin ? builder.RequireAuthorization("flag_admin") : builder.RequireAuthorization();
}