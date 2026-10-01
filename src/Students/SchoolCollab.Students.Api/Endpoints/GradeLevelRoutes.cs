using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Students.Core.CQRS.GradeLevels.Commands.CreateGradeLevel;
using SchoolCollab.Students.Core.CQRS.GradeLevels.Commands.DeleteGradeLevel;
using SchoolCollab.Students.Core.CQRS.GradeLevels.Commands.GetOrCreateGradeLevel;
using SchoolCollab.Students.Core.CQRS.GradeLevels.Commands.SetGradeLevelEnrollmentBlocked;
using SchoolCollab.Students.Core.CQRS.GradeLevels.Commands.UpdateGradeLevel;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.CQRS.GradeLevels.Queries.GetGradeLevelByCodedValue;
using SchoolCollab.Students.Core.CQRS.GradeLevels.Queries.GetGradeLevelById;
using SchoolCollab.Students.Core.CQRS.GradeLevels.Queries.ListGradeLevels;
using SchoolCollab.Students.Core.CQRS.GradeLevels.Queries.ListGradeLevelsForLanding;
using SchoolCollab.Students.Core.CQRS.GradeStreams.Commands.AssignGradeStream;
using SchoolCollab.Students.Core.CQRS.GradeStreams.Commands.RemoveGradeStream;
using SchoolCollab.Students.Core.CQRS.GradeStreams.Commands.SetGradeStreamOrder;
using SchoolCollab.Students.Core.CQRS.GradeStreams.Queries.ListGradeStreams;
using SchoolCollab.Students.Core.CQRS.Teachers.Queries.ListTeachersForGradeLevel;
using SchoolCollab.Students.Core.CQRS.TopicAssignments.Queries.ListGradeTopicCurriculumByGrade;
using SchoolCollab.Students.Core.CQRS.GradeNotificationPolicies.Commands.UpsertGradeNotificationPolicy;
using SchoolCollab.Students.Core.CQRS.GradeNotificationPolicies.Queries.GetGradeNotificationPolicy;
using SchoolCollab.Students.Core.CQRS.GradeAssignmentPolicies.Commands.UpsertGradeAssignmentPolicy;
using SchoolCollab.Students.Core.CQRS.GradeAssignmentPolicies.Queries.GetGradeAssignmentPolicy;

namespace SchoolCollab.Students.Api.Endpoints;

public static class GradeLevelRoutes
{
    public static RouteGroupBuilder MapGradeLevelRoutes(this RouteGroupBuilder group)
    {
        // ── Grade Levels ──────────────────────────────────────────────────────────

        group.MapGet("/grade-levels", async (
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<ListGradeLevels, SchoolCollab.Students.Core.DTOs.GradeLevelDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListGradeLevels(), ct)));

        // ── Landing page: per-current-period counts (current period derived server-side; §5.3) ──
        group.MapGet("/grade-levels/landing", async (
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<ListGradeLevelsForLanding, SchoolCollab.Students.Core.DTOs.GradeLevelLandingDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListGradeLevelsForLanding(), ct)));

        // ── Read by coded-value id (wizard find-or-create get half; §6.3) ──
        group.MapGet("/grade-levels/by-coded-value/{codedValueId:guid}", async (
            Guid codedValueId,
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<GetGradeLevelByCodedValue, SchoolCollab.Students.Core.DTOs.GradeLevelDto?> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetGradeLevelByCodedValue(codedValueId), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        // ── Find-or-create by CodedValueId (wizard save; §6.3) ──
        group.MapPost("/grade-levels/get-or-create", async (
            [FromBody] GetOrCreateGradeLevelRequest req,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<GetOrCreateGradeLevel, SchoolCollab.Students.Core.DTOs.GradeLevelDto> handler,
            CancellationToken ct) =>
        {
            try
            {
                var dto = await handler.HandleAsync(
                    new GetOrCreateGradeLevel(req.CodedValueId, req.Level, req.Name, req.DisplayOrder,
                        req.MinAge, req.MaxAge, req.AllowedGenderCodedValueId), ct);
                return Results.Ok(dto);
            }
            catch (ConcurrencyException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
        });

        group.MapGet("/grade-levels/{id:guid}", async (
            Guid id,
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<GetGradeLevelById, SchoolCollab.Students.Core.DTOs.GradeLevelDto?> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetGradeLevelById(id), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        // Teachers linked to a grade level, each carrying their coded-value role
        // on that grade and the topics they teach (grade-level-detail-view-plan.md §3.1).
        group.MapGet("/grade-levels/{id:guid}/teachers", async (
            Guid id,
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<ListTeachersForGradeLevel, SchoolCollab.Students.Core.DTOs.TeacherWithRoleDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListTeachersForGradeLevel(id), ct)));

        // Per-topic strand/lesson counts for the grade's assigned topics
        // (grade-detail-rich-grids-plan.md §4).
        group.MapGet("/grade-levels/{id:guid}/curriculum", async (
            Guid id,
            DateOnly? effectiveDate,
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<ListGradeTopicCurriculumByGrade, SchoolCollab.Students.Core.DTOs.GradeTopicCurriculumDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(
                new ListGradeTopicCurriculumByGrade(id, effectiveDate ?? DateOnly.FromDateTime(DateTime.UtcNow)), ct)));

        group.MapPost("/grade-levels", async (
            [FromBody] CreateGradeLevel command,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<CreateGradeLevel, Guid> handler,
            CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(command, ct);
            return Results.Created($"/grade-levels/{id}", new { id });
        });

        group.MapPut("/grade-levels/{id:guid}", async (
            Guid id,
            [FromBody] UpdateGradeLevelRequest req,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<UpdateGradeLevel> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new UpdateGradeLevel(id, req.Level, req.Name, req.DisplayOrder,
                    req.MinAge, req.MaxAge, req.AllowedGenderCodedValueId), ct);
                return Results.NoContent();
            }
            catch (GradeLevelNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ConcurrencyException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
        });

        // ── Block/unblock a grade level from enrollment (landing toggle) ──
        group.MapPatch("/grade-levels/{id:guid}/enrollment-blocked", async (
            Guid id,
            [FromBody] SetEnrollmentBlockedRequest req,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<SetGradeLevelEnrollmentBlocked> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new SetGradeLevelEnrollmentBlocked(id, req.Blocked), ct);
                return Results.NoContent();
            }
            catch (GradeLevelNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ConcurrencyException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
        });

        group.MapDelete("/grade-levels/{id:guid}", async (
            Guid id,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<DeleteGradeLevel> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new DeleteGradeLevel(id), ct);
                return Results.NoContent();
            }
            catch (GradeLevelNotFoundException)
            {
                return Results.NotFound();
            }
            catch (GradeLevelReferencedException ex)
            {
                return Results.Conflict(new { ex.Message, ex.References });
            }
        });

        // ── Grade level ↔ stream bridge (the card's read + add + remove path) ──
        // The bridge row IS the link; the coded value's legacy `gradeLevel`
        // attribute is no longer read or written.
        group.MapGet("/grade-levels/{id:guid}/streams", async (
            Guid id,
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<ListGradeStreams, SchoolCollab.Students.Core.DTOs.GradeStreamDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListGradeStreams(id), ct)));

        group.MapPost("/grade-levels/{id:guid}/streams", async (
            Guid id,
            [FromBody] AssignGradeStreamRequest req,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<AssignGradeStream, Guid> handler,
            CancellationToken ct) =>
        {
            try
            {
                var assignmentId = await handler.HandleAsync(new AssignGradeStream(id, req.StreamCodedValueId), ct);
                return Results.Created($"/grade-levels/{id}/streams/{assignmentId}", new { id = assignmentId });
            }
            catch (GradeLevelNotFoundException) { return Results.NotFound(); }
            catch (DuplicateStreamAssignmentException ex) { return Results.Conflict(new { ex.Message }); }
            catch (StreamGradeMismatchException ex) { return Results.BadRequest(new { ex.Message }); }
        });

        // Unassign: deletes the bridge row only. The coded value STAYS in the
        // GRSTREAMS catalogue and its IsDisabled state is untouched.
        group.MapDelete("/grade-levels/{id:guid}/streams/{assignmentId:guid}", async (
            Guid id,
            Guid assignmentId,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<RemoveGradeStream> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new RemoveGradeStream(id, assignmentId), ct);
                return Results.NoContent();
            }
            catch (InvalidOperationException ex) { return Results.NotFound(new { ex.Message }); }
        });

        // Manual reorder: moves one stream to position `order` in this grade's
        // list (bridge DisplayOrder; swap semantics). 400 when the position is
        // outside the grade's list; 404 for an unknown/other-grade assignment;
        // 409 on a row-version conflict (the ContactRoutes catch shape).
        group.MapPut("/grade-levels/{id:guid}/streams/{assignmentId:guid}/order", async (
            Guid id,
            Guid assignmentId,
            [FromBody] SetGradeStreamOrderRequest req,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<SetGradeStreamOrder> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new SetGradeStreamOrder(id, assignmentId, req.Order), ct);
                return Results.NoContent();
            }
            catch (ArgumentOutOfRangeException ex) { return Results.BadRequest(new { ex.Message }); }
            catch (InvalidOperationException ex) { return Results.NotFound(new { ex.Message }); }
            catch (ConcurrencyException ex) { return Results.Conflict(new { ex.Message }); }
        });

        // ── Per-grade notification policy (override; null fields inherit tenant default) ──
        group.MapGet("/grade-levels/{id:guid}/notification-policy", async (
            Guid id,
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<GetGradeNotificationPolicy, SchoolCollab.Students.Core.DTOs.GradeNotificationPolicyDto?> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetGradeNotificationPolicy(id), ct);
            return result is null ? Results.NoContent() : Results.Ok(result);
        });

        group.MapPut("/grade-levels/{id:guid}/notification-policy", async (
            Guid id,
            [FromBody] UpsertGradeNotificationPolicyRequest req,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<UpsertGradeNotificationPolicy, SchoolCollab.Students.Core.DTOs.GradeNotificationPolicyDto> handler,
            CancellationToken ct) =>
        {
            try
            {
                var result = await handler.HandleAsync(new UpsertGradeNotificationPolicy(
                    id,
                    req.PreferredChannelOrder,
                    req.BlockedChannels,
                    req.MaxNotifications,
                    req.MaxReminders,
                    req.ReminderIntervalHours,
                    req.LinkValidityDays,
                    req.SendoutTimeOfDay,
                    req.SendoutIntervalMinutes), ct);
                return Results.Ok(result);
            }
            catch (GradeLevelNotFoundException) { return Results.NotFound(); }
            catch (ArgumentOutOfRangeException ex) { return Results.BadRequest(new { ex.Message }); }
        });

        // ── Per-grade guardian-signature override (null = inherit tenant default; WS-C1) ──
        group.MapGet("/grade-levels/{id:guid}/assignment-policy", async (
            Guid id,
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<GetGradeAssignmentPolicy, SchoolCollab.Students.Core.DTOs.GradeAssignmentPolicyDto?> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetGradeAssignmentPolicy(id), ct);
            return result is null ? Results.NoContent() : Results.Ok(result);
        });

        // A non-positive contact cap is rejected by the handler (Q7) and mapped to 400
        // here (the notification-policy PUT precedent above).
        group.MapPut("/grade-levels/{id:guid}/assignment-policy", async (
            Guid id,
            [FromBody] UpsertGradeAssignmentPolicyRequest req,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<UpsertGradeAssignmentPolicy, SchoolCollab.Students.Core.DTOs.GradeAssignmentPolicyDto> handler,
            CancellationToken ct) =>
        {
            try
            {
                var result = await handler.HandleAsync(new UpsertGradeAssignmentPolicy(
                    id,
                    req.ResolveSignatureRequirement(),
                    req.RequiresApprovalBeforePublish,
                    req.MaxPrimaryContacts,
                    req.MaxCopyContacts), ct);
                return Results.Ok(result);
            }
            catch (GradeLevelNotFoundException) { return Results.NotFound(); }
            catch (ArgumentOutOfRangeException ex) { return Results.BadRequest(new { ex.Message }); }
        });

        return group;
    }
}

internal record UpdateGradeLevelRequest(int Level, string Name, int DisplayOrder,
    int? MinAge = null, int? MaxAge = null, Guid? AllowedGenderCodedValueId = null);
internal record GetOrCreateGradeLevelRequest(Guid CodedValueId, int Level, string Name, int DisplayOrder,
    int? MinAge = null, int? MaxAge = null, Guid? AllowedGenderCodedValueId = null);
internal record SetEnrollmentBlockedRequest(bool Blocked);
internal record AssignGradeStreamRequest(Guid StreamCodedValueId);

/// <summary>Request body for moving one stream to a position in its grade's list.</summary>
internal record SetGradeStreamOrderRequest(int Order);

/// <summary>
/// PUT body for the per-grade assignment-policy override. Every field is nullable: null means
/// "inherit the tenant default" (<c>documents/solution/assignment-policy-fields.md</c> §4).
///
/// <para><b>Legacy-input compatibility (Round A only).</b> The pre-widening wire shape was the
/// boolean <c>requiresSignatureDefault</c>, and the shipped Admin editor
/// (<c>GradeSignaturePolicyEditor</c>) still sends it. It is accepted <i>alongside</i> the new
/// field set and mapped when <c>SignatureRequirement</c> is absent (<c>true → Optional</c>,
/// <c>false → Disabled</c>, null → inherit). Round B deletes this member together with the
/// retired editor.</para>
/// </summary>
internal record UpsertGradeAssignmentPolicyRequest(
    SchoolCollab.Core.AssignmentPolicies.SignatureRequirementMode? SignatureRequirement,
    bool? RequiresApprovalBeforePublish,
    int? MaxPrimaryContacts,
    int? MaxCopyContacts,
    bool? RequiresSignatureDefault = null)
{
    /// <summary>
    /// The signature requirement to persist: the new field when supplied, else the legacy
    /// boolean mapped per D8's <c>false → Disabled, true → Optional</c> back-compat rule.
    /// </summary>
    public SchoolCollab.Core.AssignmentPolicies.SignatureRequirementMode? ResolveSignatureRequirement() =>
        SignatureRequirement ?? (RequiresSignatureDefault switch
        {
            true => SchoolCollab.Core.AssignmentPolicies.SignatureRequirementMode.Optional,
            false => SchoolCollab.Core.AssignmentPolicies.SignatureRequirementMode.Disabled,
            null => (SchoolCollab.Core.AssignmentPolicies.SignatureRequirementMode?)null,
        });
}
internal record UpsertGradeNotificationPolicyRequest(
    SchoolCollab.Core.Notifications.NotificationChannel[]? PreferredChannelOrder,
    SchoolCollab.Core.Notifications.NotificationChannel[]? BlockedChannels,
    int? MaxNotifications,
    int? MaxReminders,
    int? ReminderIntervalHours,
    int? LinkValidityDays,
    TimeOnly? SendoutTimeOfDay,
    int? SendoutIntervalMinutes);
