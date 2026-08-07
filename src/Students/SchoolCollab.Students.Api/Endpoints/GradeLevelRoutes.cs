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
using SchoolCollab.Students.Core.CQRS.Teachers.Queries.ListTeachersForGradeLevel;
using SchoolCollab.Students.Core.CQRS.TopicAssignments.Queries.ListGradeTopicCurriculumByGrade;
using SchoolCollab.Students.Core.CQRS.GradeNotificationPolicies.Commands.UpsertGradeNotificationPolicy;
using SchoolCollab.Students.Core.CQRS.GradeNotificationPolicies.Queries.GetGradeNotificationPolicy;

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

        return group;
    }
}

internal record UpdateGradeLevelRequest(int Level, string Name, int DisplayOrder,
    int? MinAge = null, int? MaxAge = null, Guid? AllowedGenderCodedValueId = null);
internal record GetOrCreateGradeLevelRequest(Guid CodedValueId, int Level, string Name, int DisplayOrder,
    int? MinAge = null, int? MaxAge = null, Guid? AllowedGenderCodedValueId = null);
internal record SetEnrollmentBlockedRequest(bool Blocked);
internal record UpsertGradeNotificationPolicyRequest(
    SchoolCollab.Core.Notifications.NotificationChannel[]? PreferredChannelOrder,
    SchoolCollab.Core.Notifications.NotificationChannel[]? BlockedChannels,
    int? MaxNotifications,
    int? MaxReminders,
    int? ReminderIntervalHours,
    int? LinkValidityDays,
    TimeOnly? SendoutTimeOfDay,
    int? SendoutIntervalMinutes);
