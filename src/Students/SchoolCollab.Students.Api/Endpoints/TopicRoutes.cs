using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Students.Core.CQRS.Topics.Commands.AssignLessonStrand;
using SchoolCollab.Students.Core.CQRS.Topics.Commands.CreateTopic;
using SchoolCollab.Students.Core.CQRS.Topics.Commands.CreateTopicForGrade;
using SchoolCollab.Students.Core.CQRS.Topics.Commands.CreateTopicLesson;
using SchoolCollab.Students.Core.CQRS.Topics.Commands.CreateTopicStrand;
using SchoolCollab.Students.Core.CQRS.Topics.Commands.DeleteTopic;
using SchoolCollab.Students.Core.CQRS.Topics.Commands.RemoveTopicLesson;
using SchoolCollab.Students.Core.CQRS.Topics.Commands.RemoveTopicStrand;
using SchoolCollab.Students.Core.CQRS.Topics.Commands.UpdateTopic;
using SchoolCollab.Students.Core.CQRS.Topics.Commands.UpdateTopicLesson;
using SchoolCollab.Students.Core.CQRS.Topics.Commands.UpdateTopicStrand;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.CQRS.Topics.Queries.GetTopicByCode;
using SchoolCollab.Students.Core.CQRS.Topics.Queries.GetTopicById;
using SchoolCollab.Students.Core.CQRS.Topics.Queries.ListTopicLessons;
using SchoolCollab.Students.Core.CQRS.Topics.Queries.ListTopicStrands;
using SchoolCollab.Students.Core.CQRS.Topics.Queries.ListTopics;
using SchoolCollab.Students.Core.CQRS.Topics.Queries.ListTopicsByGrade;
using SchoolCollab.Students.Core.CQRS.Topics.Queries.ListTopicsByGroup;

namespace SchoolCollab.Students.Api.Endpoints;

public static class TopicRoutes
{
    public static RouteGroupBuilder MapTopicRoutes(this RouteGroupBuilder group)
    {
        // NFR-6 / AC-16 (subject-to-topic-polymorphism.md): `/topics` is the
        // canonical route prefix. The legacy `/subjects` prefix is registered
        // as a backward-compatible alias for a deprecation window so existing
        // clients keep working and receive the same TopicDto data.
        MapTopicSubgroup(group, "/topics");
        MapTopicSubgroup(group, "/subjects"); // deprecated alias (NFR-6)

        return group;
    }

    /// <summary>
    /// Registers the full topic (subject) route surface under the given prefix.
    /// <paramref name="prefix"/> is the canonical `/topics` or the deprecated
    /// `/subjects` alias; every handler is shared.
    /// </summary>
    private static void MapTopicSubgroup(RouteGroupBuilder group, string prefix)
    {
        // ── Topics ────────────────────────────────────────────────────────────

        group.MapGet(prefix, async (
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<ListTopics, SchoolCollab.Students.Core.DTOs.TopicDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListTopics(), ct)));

        group.MapGet($"{prefix}/{{id:guid}}", async (
            Guid id,
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<GetTopicById, SchoolCollab.Students.Core.DTOs.TopicDto?> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetTopicById(id), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapGet($"{prefix}/by-code/{{code}}", async (
            string code,
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<GetTopicByCode, SchoolCollab.Students.Core.DTOs.TopicDto?> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetTopicByCode(code), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapGet($"{prefix}/by-group/{{activityGroupId:guid}}", async (
            Guid activityGroupId,
            DateOnly? effectiveDate,
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<ListTopicsByGroup, SchoolCollab.Students.Core.DTOs.TopicDto[]> handler,
            CancellationToken ct) =>
        {
            try
            {
                var topics = await handler.HandleAsync(
                    new ListTopicsByGroup(activityGroupId, effectiveDate), ct);
                return Results.Ok(topics);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
            }
            catch (Exception ex)
            {
                return Results.Json(
                    new { Message = "Topics by group: unexpected error", Detail = ex.Message },
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        });

        group.MapGet($"{prefix}/by-grade/{{gradeLevelId:guid}}", async (
            Guid gradeLevelId,
            DateOnly? effectiveDate,
            Guid? periodId,
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<ListTopicsByGrade, SchoolCollab.Students.Core.DTOs.TopicDto[]> handler,
            CancellationToken ct) =>
        {
            try
            {
                var topics = await handler.HandleAsync(
                    new ListTopicsByGrade(gradeLevelId, effectiveDate, periodId), ct);
                return Results.Ok(topics);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
            {
                return Results.Json(
                    new { Message = "Topics by grade: database error", Detail = ex.Message },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (Exception ex)
            {
                return Results.Json(
                    new { Message = "Topics by grade: unexpected error", Detail = ex.Message },
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        });

        group.MapPost(prefix, async (
            [FromBody] CreateTopic command,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<CreateTopic, Guid> handler,
            CancellationToken ct) =>
        {
            try
            {
                var id = await handler.HandleAsync(command, ct);
                return Results.Created($"{prefix}/{id}", new { id });
            }
            catch (DuplicateTopicCodeException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
        });

        group.MapPost($"{prefix}/get-or-create", async (
            [FromBody] GetOrCreateTopicRequest req,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<SchoolCollab.Students.Core.CQRS.Topics.Commands.GetOrCreateTopic.GetOrCreateTopic, SchoolCollab.Students.Core.DTOs.TopicDto> handler,
            CancellationToken ct) =>
        {
            var dto = await handler.HandleAsync(
                new SchoolCollab.Students.Core.CQRS.Topics.Commands.GetOrCreateTopic.GetOrCreateTopic(
                    req.GradeLevelId, req.CodedValueId, req.Code, req.Name, req.DisplayOrder), ct);
            return Results.Ok(dto);
        });

        group.MapPost($"{prefix}/for-grade", async (
            [FromBody] CreateTopicForGradeRequest req,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<CreateTopicForGrade, SchoolCollab.Students.Core.DTOs.TopicDto> handler,
            CancellationToken ct) =>
        {
            try
            {
                var dto = await handler.HandleAsync(
                    new CreateTopicForGrade(req.GradeLevelId, req.CodedValueId, req.Code, req.Name, req.DisplayOrder, req.PeriodId), ct);
                return Results.Ok(dto);
            }
            catch (GradeLevelNotFoundException)
            {
                return Results.NotFound(new { Message = $"Grade level '{req.GradeLevelId}' not found." });
            }
            catch (NoCurrentPeriodException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
            catch (DuplicateTopicCodeException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
        });

        group.MapPut($"{prefix}/{{id:guid}}", async (
            Guid id,
            [FromBody] UpdateTopicRequest req,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<UpdateTopic> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new UpdateTopic(id, req.Name, req.DisplayOrder, req.CodedValueId, req.Code), ct);
                return Results.NoContent();
            }
            catch (TopicNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ConcurrencyException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
        });

        group.MapDelete($"{prefix}/{{id:guid}}", async (
            Guid id,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<DeleteTopic> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new DeleteTopic(id), ct);
                return Results.NoContent();
            }
            catch (TopicNotFoundException)
            {
                return Results.NotFound();
            }
            catch (TopicReferencedException ex)
            {
                return Results.Conflict(new { ex.Message, ex.References });
            }
        });

        group.MapGet($"{prefix}/{{topicId:guid}}/strands", async (
            Guid topicId,
            Guid? parentStrandId,
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<ListTopicStrands, SchoolCollab.Students.Core.DTOs.TopicStrandDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListTopicStrands(topicId, parentStrandId), ct)));

        group.MapPost($"{prefix}/strands", async (
            [FromBody] CreateTopicStrand command,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<CreateTopicStrand, SchoolCollab.Students.Core.DTOs.TopicStrandDto> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(command, ct);
            return Results.Created($"{prefix}/strands/{result.Id}", result);
        });

        group.MapPut($"{prefix}/strands/{{id:guid}}", async (
            Guid id,
            [FromBody] UpdateTopicStrandRequest req,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<UpdateTopicStrand, SchoolCollab.Students.Core.DTOs.TopicStrandDto> handler,
            CancellationToken ct) =>
        {
            try
            {
                var result = await handler.HandleAsync(new UpdateTopicStrand(id, req.Name, req.Description, req.DisplayOrder, req.ParentStrandId, req.StartDate, req.EndDate), ct);
                return Results.Ok(result);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { ex.Message });
            }
        });

        group.MapDelete($"{prefix}/strands/{{id:guid}}", async (
            Guid id,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<RemoveTopicStrand> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new RemoveTopicStrand(id), ct);
                return Results.NoContent();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });

        group.MapGet($"{prefix}/{{topicId:guid}}/lessons", async (
            Guid topicId,
            Guid? strandId,
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<ListTopicLessons, SchoolCollab.Students.Core.DTOs.TopicLessonDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListTopicLessons(topicId, strandId), ct)));

        group.MapPost($"{prefix}/lessons", async (
            [FromBody] CreateTopicLesson command,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<CreateTopicLesson, SchoolCollab.Students.Core.DTOs.TopicLessonDto> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(command, ct);
            return Results.Created($"{prefix}/lessons/{result.Id}", result);
        });

        group.MapPut($"{prefix}/lessons/{{id:guid}}", async (
            Guid id,
            [FromBody] UpdateTopicLessonRequest req,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<UpdateTopicLesson, SchoolCollab.Students.Core.DTOs.TopicLessonDto> handler,
            CancellationToken ct) =>
        {
            try
            {
                var result = await handler.HandleAsync(new UpdateTopicLesson(id, req.Name, req.Description, req.StartDate, req.EndDate, req.DisplayOrder), ct);
                return Results.Ok(result);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });

        group.MapPost($"{prefix}/lessons/{{id:guid}}/strand", async (
            Guid id,
            [FromBody] AssignLessonStrandRequest req,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<AssignLessonStrand, SchoolCollab.Students.Core.DTOs.TopicLessonDto> handler,
            CancellationToken ct) =>
        {
            try
            {
                var result = await handler.HandleAsync(new AssignLessonStrand(id, req.StrandId), ct);
                return Results.Ok(result);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });

        group.MapDelete($"{prefix}/lessons/{{id:guid}}", async (
            Guid id,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<RemoveTopicLesson> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new RemoveTopicLesson(id), ct);
                return Results.NoContent();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });
    }
}

internal record UpdateTopicRequest(
    string Name,
    int DisplayOrder,
    Guid? CodedValueId = null,
    string? Code = null);
internal record UpdateTopicStrandRequest(
    string Name,
    string? Description,
    int DisplayOrder,
    Guid? ParentStrandId = null,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null);
internal record UpdateTopicLessonRequest(string Name, string? Description, DateOnly? StartDate, DateOnly? EndDate, int DisplayOrder);
internal record AssignLessonStrandRequest(Guid? StrandId);

internal record CreateTopicForGradeRequest(
    Guid GradeLevelId,
    Guid? CodedValueId,
    string? Code,
    string Name,
    int DisplayOrder,
    Guid? PeriodId = null);

internal record GetOrCreateTopicRequest(
    Guid GradeLevelId,
    Guid CodedValueId,
    string? Code,
    string Name,
    int DisplayOrder);
