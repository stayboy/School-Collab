using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Students.Core.CQRS.TopicAssignments.Commands.AssignGradeTopic;
using SchoolCollab.Students.Core.CQRS.TopicAssignments.Commands.AssignActivityGroupTopic;
using SchoolCollab.Students.Core.CQRS.TopicAssignments.Commands.RemoveTopicAssignment;
using SchoolCollab.Students.Core.CQRS.TopicAssignments.Commands.SetGradeTopicOrder;
using SchoolCollab.Students.Core.CQRS.TopicAssignments.Commands.UpdateTopicAssignmentPeriod;
using SchoolCollab.Students.Core.CQRS.TopicAssignments.Commands.UpdateTopicAssignmentTags;
using SchoolCollab.Students.Core.CQRS.TopicAssignments.Queries.ListGradeTopicAssignments;
using SchoolCollab.Students.Core.CQRS.TopicAssignments.Queries.ListActivityGroupTopicAssignments;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Api.Endpoints;

public static class TopicAssignmentRoutes
{
    public static RouteGroupBuilder MapTopicAssignmentRoutes(this RouteGroupBuilder group)
    {
        // ── Topic Assignments ─────────────────────────────────────────────────
        // Grade↔topic and activity-group↔topic assignments are date-based (not
        // period-bound) and span multiple years. An optional effectiveDate filters
        // to assignments in effect on that date; omitted = today.

        group.MapGet("/topic-assignments/by-grade/{gradeLevelId:guid}", async (
            Guid gradeLevelId,
            DateOnly? effectiveDate,
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<ListGradeTopicAssignments, SchoolCollab.Students.Core.DTOs.TopicAssignmentDto[]> handler,
            CancellationToken ct) =>
        {
            var effective = effectiveDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
            return Results.Ok(await handler.HandleAsync(new ListGradeTopicAssignments(gradeLevelId, effective), ct));
        });

        group.MapGet("/topic-assignments/by-activity-group/{activityGroupId:guid}", async (
            Guid activityGroupId,
            DateOnly? effectiveDate,
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<ListActivityGroupTopicAssignments, SchoolCollab.Students.Core.DTOs.TopicAssignmentDto[]> handler,
            CancellationToken ct) =>
        {
            var effective = effectiveDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
            return Results.Ok(await handler.HandleAsync(new ListActivityGroupTopicAssignments(activityGroupId, effective), ct));
        });

        group.MapPost("/topic-assignments/grade", async (
            [FromBody] AssignGradeTopic command,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<AssignGradeTopic, Guid> handler,
            CancellationToken ct) =>
        {
            try
            {
                var id = await handler.HandleAsync(command, ct);
                return Results.Created($"/topic-assignments/{id}", new { id });
            }
            catch (TopicAssignmentPeriodException ex) { return Results.Json(new { ex.Message }, statusCode: 422); }
        });

        group.MapPost("/topic-assignments/activity-group", async (
            [FromBody] AssignActivityGroupTopic command,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<AssignActivityGroupTopic, Guid> handler,
            CancellationToken ct) =>
        {
            try
            {
                var id = await handler.HandleAsync(command, ct);
                return Results.Created($"/topic-assignments/{id}", new { id });
            }
            catch (TopicAssignmentPeriodException ex) { return Results.Json(new { ex.Message }, statusCode: 422); }
            catch (ActivityGroupNotFoundException) { return Results.NotFound(); }
            catch (DuplicateTopicAssignmentException ex) { return Results.Conflict(new { ex.Message }); }
        });

        group.MapPut("/topic-assignments/{id:guid}/tags", async (
            Guid id,
            [FromBody] UpdateTopicAssignmentTagsRequest req,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<UpdateTopicAssignmentTags, SchoolCollab.Students.Core.DTOs.TopicAssignmentDto> handler,
            CancellationToken ct) =>
        {
            try
            {
                var result = await handler.HandleAsync(new UpdateTopicAssignmentTags(id, req.TopicStrandId), ct);
                return Results.Ok(result);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });

        // ── DEPRECATED (2026-09-26) ───────────────────────────────────────────
        // subject-period-exception-model.md: the bridge row carries no period any
        // more, so this route is a NO-OP with respect to availability. It is kept
        // (not removed) for wire compatibility — no UI calls it after this round.
        // "Not offered in period P" is POST /students/enrollment-exceptions instead.
        group.MapPut("/topic-assignments/{id:guid}/period", async (
            Guid id,
            [FromBody] UpdateTopicAssignmentPeriodRequest req,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<UpdateTopicAssignmentPeriod, SchoolCollab.Students.Core.DTOs.TopicAssignmentDto> handler,
            CancellationToken ct) =>
        {
            try
            {
                var result = await handler.HandleAsync(new UpdateTopicAssignmentPeriod(id, req.PeriodId), ct);
                return Results.Ok(result);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (TopicAssignmentPeriodException ex)
            {
                return Results.Json(new { ex.Message }, statusCode: 422);
            }
        });

        group.MapDelete("/topic-assignments/{id:guid}", async (
            Guid id,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<RemoveTopicAssignment> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new RemoveTopicAssignment(id), ct);
                return Results.NoContent();
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(new { ex.Message });
            }
        });

        // Manual reorder of a grade's subject list: moves the ASSIGNMENT to
        // position `order` (bridge DisplayOrder; swap semantics). The route is
        // id-only, so the grade (and tenant) scope comes from the loaded row.
        // 400 when the position is outside the grade's effective list; 404 for an
        // unknown, other-tenant, activity-group, or non-effective assignment;
        // 409 on a row-version conflict (the ContactRoutes catch shape).
        group.MapPut("/topic-assignments/{id:guid}/order", async (
            Guid id,
            [FromBody] SetGradeTopicOrderRequest req,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<SetGradeTopicOrder> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new SetGradeTopicOrder(id, req.Order), ct);
                return Results.NoContent();
            }
            catch (ArgumentOutOfRangeException ex) { return Results.BadRequest(new { ex.Message }); }
            catch (InvalidOperationException ex) { return Results.NotFound(new { ex.Message }); }
            catch (ConcurrencyException ex) { return Results.Conflict(new { ex.Message }); }
        });

        return group;
    }
}

internal record UpdateTopicAssignmentTagsRequest(Guid? TopicStrandId);
internal record UpdateTopicAssignmentPeriodRequest(Guid? PeriodId);

/// <summary>Request body for moving one subject assignment to a position in its grade's list.</summary>
internal record SetGradeTopicOrderRequest(int Order);
