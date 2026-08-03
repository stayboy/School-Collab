using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Students.Core.CQRS.TopicAssignments.Commands.AssignGradeTopic;
using SchoolCollab.Students.Core.CQRS.TopicAssignments.Commands.AssignActivityGroupTopic;
using SchoolCollab.Students.Core.CQRS.TopicAssignments.Commands.RemoveTopicAssignment;
using SchoolCollab.Students.Core.CQRS.TopicAssignments.Commands.UpdateTopicAssignmentTags;
using SchoolCollab.Students.Core.CQRS.TopicAssignments.Queries.ListGradeTopicAssignments;
using SchoolCollab.Students.Core.CQRS.TopicAssignments.Queries.ListActivityGroupTopicAssignments;

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
            var id = await handler.HandleAsync(command, ct);
            return Results.Created($"/topic-assignments/{id}", new { id });
        });

        group.MapPost("/topic-assignments/activity-group", async (
            [FromBody] AssignActivityGroupTopic command,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<AssignActivityGroupTopic, Guid> handler,
            CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(command, ct);
            return Results.Created($"/topic-assignments/{id}", new { id });
        });

        group.MapPut("/topic-assignments/{id:guid}/tags", async (
            Guid id,
            [FromBody] UpdateTopicAssignmentTagsRequest req,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<UpdateTopicAssignmentTags, SchoolCollab.Students.Core.DTOs.TopicAssignmentDto> handler,
            CancellationToken ct) =>
        {
            try
            {
                var result = await handler.HandleAsync(new UpdateTopicAssignmentTags(id, req.TopicStrandId, req.TopicLessonId), ct);
                return Results.Ok(result);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
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

        return group;
    }
}

internal record UpdateTopicAssignmentTagsRequest(Guid? TopicStrandId, Guid? TopicLessonId);
