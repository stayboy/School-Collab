using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Commands.DeleteTenantNotificationTemplate;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Commands.UpsertTenantNotificationTemplate;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Queries.GetTemplateAuditEntries;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Queries.GetTenantNotificationTemplate;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Queries.GetTenantNotificationTemplates;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Api.Endpoints;

/// <summary>
/// Notification-template store endpoints (spec §5, workstream 1). One row per tenant
/// per kind × channel; an absent row means the built-in default applies.
/// Every mutation writes a <c>TemplateAuditEntry</c> (spec §3.9).
/// </summary>
public static class NotificationTemplateRoutes
{
    /// <summary>
    /// Maps the tenant's notification templates under
    /// <c>/api/settings/notification-templates</c>.
    /// </summary>
    public static RouteGroupBuilder MapNotificationTemplateRoutes(this RouteGroupBuilder group)
    {
        // ── All rows + constraints (Admin page list, spec §5) ──
        group.MapGet("/notification-templates", async (
            [FromServices] IQueryHandler<GetTenantNotificationTemplates, IReadOnlyList<TenantNotificationTemplateDto>> handler,
            CancellationToken ct) =>
        {
            var templates = await handler.HandleAsync(new GetTenantNotificationTemplates(), ct);
            return Results.Ok(templates);
        });

        // ── One row; 204 = unset (built-in default applies) ──
        group.MapGet("/notification-templates/{kind}/{channel}", async (
            NotificationTemplateKind kind,
            NotificationChannel channel,
            [FromServices] IQueryHandler<GetTenantNotificationTemplate, TenantNotificationTemplateDto?> handler,
            CancellationToken ct) =>
        {
            var template = await handler.HandleAsync(new GetTenantNotificationTemplate(kind, channel), ct);
            return template is null ? Results.NoContent() : Results.Ok(template);
        });

        // ── Upsert (create or replace) + audit row ──
        group.MapPut("/notification-templates/{kind}/{channel}", async (
            NotificationTemplateKind kind,
            NotificationChannel channel,
            [FromBody] UpsertTenantNotificationTemplateRequest req,
            [FromServices] ICommandHandler<UpsertTenantNotificationTemplate, TenantNotificationTemplateDto> handler,
            CancellationToken ct) =>
        {
            try
            {
                var result = await handler.HandleAsync(
                    new UpsertTenantNotificationTemplate(kind, channel, req, req.Reason), ct);
                return Results.Ok(result);
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { ex.Message }); }
        });

        // ── Reset to the built-in default (audited) ──
        // `reason` is REQUIRED and bound explicitly from the query string, matching the
        // audited-delete precedent at ConfigFlagRoutes.MapDelete("/flags/{key}", …).
        // A reset is audited, so the audit row must carry the operator's own words.
        group.MapDelete("/notification-templates/{kind}/{channel}", async (
            NotificationTemplateKind kind,
            NotificationChannel channel,
            [FromQuery] string reason,
            [FromServices] ICommandHandler<DeleteTenantNotificationTemplate> handler,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(reason))
                return Results.BadRequest(new { Message = "A reason is required to reset a notification template." });

            await handler.HandleAsync(new DeleteTenantNotificationTemplate(kind, channel, reason), ct);
            return Results.NoContent();
        });

        // ── Audit trail for this template, most recent first (spec §3.9) ──
        group.MapGet("/notification-templates/{kind}/{channel}/audit", async (
            NotificationTemplateKind kind,
            NotificationChannel channel,
            [FromServices] IQueryHandler<GetTemplateAuditEntries, IReadOnlyList<TemplateAuditEntryDto>> handler,
            CancellationToken ct) =>
        {
            var entries = await handler.HandleAsync(new GetTemplateAuditEntries(kind, channel), ct);
            return Results.Ok(entries);
        });

        return group;
    }
}
