using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Commands.AdoptTenantNotificationTemplate;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Commands.CreateTenantNotificationTemplate;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Commands.DeleteTenantNotificationTemplate;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Commands.PromoteTenantNotificationTemplate;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Commands.UpdateTenantNotificationTemplate;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Queries.GetApplicableNotificationTemplates;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Queries.GetTemplateAuditEntries;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Queries.GetTenantNotificationTemplate;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Queries.GetTenantNotificationTemplateById;
using SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Queries.GetTenantNotificationTemplates;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.Domain.Exceptions;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Api.Endpoints;

/// <summary>
/// Notification-template store endpoints (spec §5, workstream 1; round notification-templates-b2c
/// re-shaped the writer). A tenant holds several live templates per kind × channel: the one with an
/// empty constraint set is the tenant default, each constrained one is a candidate for the
/// applicability matcher. Mutations are therefore addressed by <b>id</b> — the (kind, channel)-keyed
/// upsert could not tell two candidates apart. Every mutation writes a <c>TemplateAuditEntry</c>
/// (spec §3.9).
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
        // `includeTenantOwned` is the promote picker's source (redesign spec §3.6): honoured only in
        // system scope, where it reads every tenant's rows so one can be published portal-wide. A
        // tenant session passing the flag still sees its own scope only.
        group.MapGet("/notification-templates",
            async Task<Ok<IReadOnlyList<TenantNotificationTemplateDto>>> (
            [FromQuery] bool? includeTenantOwned,
            [FromServices] IQueryHandler<GetTenantNotificationTemplates, IReadOnlyList<TenantNotificationTemplateDto>> handler,
            CancellationToken ct) =>
        {
            var templates = await handler.HandleAsync(
                new GetTenantNotificationTemplates(includeTenantOwned is true), ct);
            return TypedResults.Ok(templates);
        });

        // ── The templates that CAN apply to one grade (grade-detail card, spec §3.11).
        // Mapped ahead of "/notification-templates/{kind}/{channel}" because that pattern matches this
        // URL too ("applicable" and the guid would bind to {kind}/{channel}): pattern precedence — a
        // literal segment beats a parameter — is what actually selects this route, so the ordering is
        // belt-and-braces. The paired resolution test pins both halves of that split.
        group.MapGet("/notification-templates/applicable/{gradeLevelId:guid}",
            async Task<Ok<ApplicableNotificationTemplatesDto>> (
            Guid gradeLevelId,
            [FromServices] IQueryHandler<GetApplicableNotificationTemplates, ApplicableNotificationTemplatesDto> handler,
            CancellationToken ct) =>
        {
            var templates = await handler.HandleAsync(new GetApplicableNotificationTemplates(gradeLevelId), ct);
            return TypedResults.Ok(templates);
        });

        // ── One row by id (redesign spec §5 — the per-template detail page's read). A row outside the
        // caller's scope is a 404, never a 403: a 403 would confirm that a foreign row exists.
        group.MapGet("/notification-templates/{id:guid}",
            async Task<Results<Ok<TenantNotificationTemplateDto>, NotFound<MessageResponse>>> (
            Guid id,
            [FromServices] IQueryHandler<GetTenantNotificationTemplateById, TenantNotificationTemplateDto?> handler,
            CancellationToken ct) =>
        {
            var template = await handler.HandleAsync(new GetTenantNotificationTemplateById(id), ct);
            return template is null
                ? TypedResults.NotFound(new MessageResponse($"Notification template '{id}' was not found."))
                : TypedResults.Ok(template);
        });

        // ── The tenant DEFAULT row for one kind × channel; 204 = unset (built-in default applies).
        // Round b2c: with candidates in the store, a context-less read can only mean the default row —
        // the handler filters on an empty constraint set instead of assuming one row per slot.
        group.MapGet("/notification-templates/{kind}/{channel}",
            async Task<Results<Ok<TenantNotificationTemplateDto>, NoContent>> (
            NotificationTemplateKind kind,
            NotificationChannel channel,
            [FromServices] IQueryHandler<GetTenantNotificationTemplate, TenantNotificationTemplateDto?> handler,
            CancellationToken ct) =>
        {
            var template = await handler.HandleAsync(new GetTenantNotificationTemplate(kind, channel), ct);
            return template is null ? TypedResults.NoContent() : TypedResults.Ok(template);
        });

        // ── Create one candidate row (the default included, when the slot has none) ──
        // 200, not 201: the store exposes no single-row-by-id GET to point a Location header at, and
        // the response body already carries the new id.
        group.MapPost("/notification-templates",
            async Task<Results<Ok<TenantNotificationTemplateDto>, BadRequest<MessageResponse>, Conflict<MessageResponse>>> (
            [FromBody] CreateTenantNotificationTemplateRequest req,
            [FromServices] ICommandHandler<CreateTenantNotificationTemplate, TenantNotificationTemplateDto> handler,
            CancellationToken ct) =>
        {
            try
            {
                var result = await handler.HandleAsync(
                    new CreateTenantNotificationTemplate(req, req.Reason), ct);
                return TypedResults.Ok(result);
            }
            catch (TenantNotificationTemplateConflictException ex)
            {
                return TypedResults.Conflict(new MessageResponse(ex.Message));
            }
            catch (ArgumentException ex)
            {
                return TypedResults.BadRequest(new MessageResponse(ex.Message));
            }
        });

        // ── Replace one candidate row, by id ──
        group.MapPut("/notification-templates/{id:guid}",
            async Task<Results<Ok<TenantNotificationTemplateDto>, NotFound<MessageResponse>, BadRequest<MessageResponse>, Conflict<MessageResponse>, ProblemHttpResult>> (
            Guid id,
            [FromBody] UpdateTenantNotificationTemplateRequest req,
            [FromServices] ICommandHandler<UpdateTenantNotificationTemplate, TenantNotificationTemplateDto> handler,
            CancellationToken ct) =>
        {
            try
            {
                var result = await handler.HandleAsync(
                    new UpdateTenantNotificationTemplate(id, req, req.Reason), ct);
                return TypedResults.Ok(result);
            }
            catch (TenantNotificationTemplateNotFoundException ex)
            {
                return TypedResults.NotFound(new MessageResponse(ex.Message));
            }
            catch (TenantNotificationTemplateScopeException ex)
            {
                // A row the caller can see but must not write (spec §6): 403, not a silent stamp.
                return TypedResults.Problem(detail: ex.Message, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (TenantNotificationTemplateConflictException ex)
            {
                return TypedResults.Conflict(new MessageResponse(ex.Message));
            }
            catch (ArgumentException ex)
            {
                return TypedResults.BadRequest(new MessageResponse(ex.Message));
            }
        });

        // ── Tombstone one candidate row, by id (audited) ──
        // `reason` is REQUIRED and bound explicitly from the query string, matching the
        // audited-delete precedent at ConfigFlagRoutes.MapDelete("/flags/{key}", …).
        // A reset is audited, so the audit row must carry the operator's own words.
        group.MapDelete("/notification-templates/{id:guid}",
            async Task<Results<NoContent, BadRequest<MessageResponse>, NotFound<MessageResponse>, ProblemHttpResult>> (
            Guid id,
            [FromQuery] string reason,
            [FromServices] ICommandHandler<DeleteTenantNotificationTemplate> handler,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(reason))
                return TypedResults.BadRequest(
                    new MessageResponse("A reason is required to reset a notification template."));

            try
            {
                await handler.HandleAsync(new DeleteTenantNotificationTemplate(id, reason), ct);
                return TypedResults.NoContent();
            }
            catch (TenantNotificationTemplateNotFoundException ex)
            {
                return TypedResults.NotFound(new MessageResponse(ex.Message));
            }
            catch (TenantNotificationTemplateScopeException ex)
            {
                return TypedResults.Problem(detail: ex.Message, statusCode: StatusCodes.Status403Forbidden);
            }
        });

        // ── Adopt a system-blueprint row into the caller's tenant (redesign spec §3.8) ──
        // A one-way copy: the source is never modified and re-adopting an identical set is a 409.
        // 403 when the caller has no real tenant (system scope) or names a row that is already
        // tenant-owned.
        group.MapPost("/notification-templates/{id:guid}/adopt",
            async Task<Results<Ok<TenantNotificationTemplateDto>, NotFound<MessageResponse>, Conflict<MessageResponse>, ProblemHttpResult>> (
            Guid id,
            [FromQuery] string? reason,
            [FromServices] ICommandHandler<AdoptTenantNotificationTemplate, TenantNotificationTemplateDto> handler,
            CancellationToken ct) =>
        {
            try
            {
                var result = await handler.HandleAsync(new AdoptTenantNotificationTemplate(id, reason), ct);
                return TypedResults.Ok(result);
            }
            catch (TenantNotificationTemplateNotFoundException ex)
            {
                return TypedResults.NotFound(new MessageResponse(ex.Message));
            }
            catch (TenantNotificationTemplateScopeException ex)
            {
                return TypedResults.Problem(detail: ex.Message, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (TenantNotificationTemplateConflictException ex)
            {
                return TypedResults.Conflict(new MessageResponse(ex.Message));
            }
        });

        // ── Publish a tenant row portal-wide (redesign spec §3.8) ──
        // System scope only: the one sanctioned cross-tenant read. The copy lands in the blueprint
        // (TenantId NULL); the source row is untouched.
        group.MapPost("/notification-templates/{id:guid}/promote",
            async Task<Results<Ok<TenantNotificationTemplateDto>, NotFound<MessageResponse>, Conflict<MessageResponse>, ProblemHttpResult>> (
            Guid id,
            [FromQuery] string? reason,
            [FromServices] ICommandHandler<PromoteTenantNotificationTemplate, TenantNotificationTemplateDto> handler,
            CancellationToken ct) =>
        {
            try
            {
                var result = await handler.HandleAsync(new PromoteTenantNotificationTemplate(id, reason), ct);
                return TypedResults.Ok(result);
            }
            catch (TenantNotificationTemplateNotFoundException ex)
            {
                return TypedResults.NotFound(new MessageResponse(ex.Message));
            }
            catch (TenantNotificationTemplateScopeException ex)
            {
                return TypedResults.Problem(detail: ex.Message, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (TenantNotificationTemplateConflictException ex)
            {
                return TypedResults.Conflict(new MessageResponse(ex.Message));
            }
        });

        // ── Audit trail for one kind × channel, most recent first (spec §3.9) ──
        // The detail page narrows the stream to ONE row with ?templateId= (redesign spec §3.12); the
        // merged per-slot stream stays the default, so a reset's trail is still reachable after the
        // template row is tombstoned.
        group.MapGet("/notification-templates/{kind}/{channel}/audit",
            async Task<Ok<IReadOnlyList<TemplateAuditEntryDto>>> (
            NotificationTemplateKind kind,
            NotificationChannel channel,
            [FromQuery] Guid? templateId,
            [FromServices] IQueryHandler<GetTemplateAuditEntries, IReadOnlyList<TemplateAuditEntryDto>> handler,
            CancellationToken ct) =>
        {
            var entries = await handler.HandleAsync(new GetTemplateAuditEntries(kind, channel, templateId), ct);
            return TypedResults.Ok(entries);
        });

        return group;
    }
}
