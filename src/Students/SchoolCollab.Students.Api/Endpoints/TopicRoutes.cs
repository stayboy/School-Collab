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

namespace SchoolCollab.Students.Api.Endpoints;

public static class TopicRoutes
{
    public static RouteGroupBuilder MapTopicRoutes(this RouteGroupBuilder group)
    {
        // ── Subjects ──────────────────────────────────────────────────────────────

        group.MapGet("/subjects", async (
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<ListTopics, SchoolCollab.Students.Core.DTOs.TopicDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListTopics(), ct)));

        group.MapGet("/subjects/{id:guid}", async (
            Guid id,
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<GetTopicById, SchoolCollab.Students.Core.DTOs.TopicDto?> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetTopicById(id), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapGet("/subjects/by-code/{code}", async (
            string code,
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<GetTopicByCode, SchoolCollab.Students.Core.DTOs.TopicDto?> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetTopicByCode(code), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapGet("/subjects/by-grade/{gradeLevelId:guid}", async (
            Guid gradeLevelId,
            DateOnly? effectiveDate,
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<ListTopicsByGrade, SchoolCollab.Students.Core.DTOs.TopicDto[]> handler,
            CancellationToken ct) =>
        {
            // Wrapped in try/catch so a database transient (e.g. dropped
            // connection mid-fetch, EF's DbUpdateException, an in-flight
            // cancellation from a closed client) surfaces as a typed
            // response instead of an empty ASP.NET 500. Mirrors the
            // catch blocks in ContactRoutes / EnrollmentRoutes — command
            // endpoints do this for validation; this GET does it for the
            // I/O failure modes listed below.
            //
            // Pin: a pre-cancelled token (client disconnect) must NOT be
            // reported as a 500. OperationCanceledException is caught and
            // returned as 499 Client Closed Request so the upstream proxy
            // / load balancer can distinguish it from a real failure.
            try
            {
                var subjects = await handler.HandleAsync(
                    new ListTopicsByGrade(gradeLevelId, effectiveDate), ct);
                return Results.Ok(subjects);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Client closed the connection before the query finished.
                return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
            {
                // EF Core surfaces update/transaction errors here. For a
                // read-only query this typically means the connection was
                // dropped mid-fetch. Return 503 so the client knows the
                // upstream data store is the problem and can retry.
                return Results.Json(
                    new { Message = "Subjects by grade: database error", Detail = ex.Message },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (Exception ex)
            {
                // Last-resort: log + typed 500 with the message body so the
                // caller can surface a meaningful error. The handler does
                // no validation, so any uncaught exception here is a real
                // bug — not user input.
                return Results.Json(
                    new { Message = "Subjects by grade: unexpected error", Detail = ex.Message },
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        });

        group.MapPost("/subjects", async (
            [FromBody] CreateTopic command,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<CreateTopic, Guid> handler,
            CancellationToken ct) =>
        {
            try
            {
                var id = await handler.HandleAsync(command, ct);
                return Results.Created($"/subjects/{id}", new { id });
            }
            catch (DuplicateTopicCodeException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
        });

        // ── Find-or-create Subject by CodedValueId (wizard's "Add to grade" flow) ─
        group.MapPost("/subjects/get-or-create", async (
            [FromBody] GetOrCreateTopicRequest req,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<SchoolCollab.Students.Core.CQRS.Topics.Commands.GetOrCreateTopic.GetOrCreateTopic, SchoolCollab.Students.Core.DTOs.TopicDto> handler,
            CancellationToken ct) =>
        {
            var dto = await handler.HandleAsync(
                new SchoolCollab.Students.Core.CQRS.Topics.Commands.GetOrCreateTopic.GetOrCreateTopic(
                    req.GradeLevelId, req.CodedValueId, req.Code, req.Name, req.DisplayOrder), ct);
            return Results.Ok(dto);
        });

        // ── Create Subject + GradeSubjectAssignment for the current period (§8.1) ─
        group.MapPost("/subjects/for-grade", async (
            [FromBody] CreateTopicForGradeRequest req,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<CreateTopicForGrade, SchoolCollab.Students.Core.DTOs.TopicDto> handler,
            CancellationToken ct) =>
        {
            try
            {
                var dto = await handler.HandleAsync(
                    new CreateTopicForGrade(req.GradeLevelId, req.CodedValueId, req.Code, req.Name, req.DisplayOrder), ct);
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

        group.MapPut("/subjects/{id:guid}", async (
            Guid id,
            [FromBody] UpdateTopicRequest req,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<UpdateTopic> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new UpdateTopic(id, req.Name, req.DisplayOrder), ct);
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

        group.MapDelete("/subjects/{id:guid}", async (
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

        // ── Subject Strands ───────────────────────────────────────────────────────

        group.MapGet("/subjects/{subjectId:guid}/strands", async (
            Guid subjectId,
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<ListTopicStrands, SchoolCollab.Students.Core.DTOs.TopicStrandDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListTopicStrands(subjectId), ct)));

        group.MapPost("/subjects/strands", async (
            [FromBody] CreateTopicStrand command,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<CreateTopicStrand, SchoolCollab.Students.Core.DTOs.TopicStrandDto> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(command, ct);
            return Results.Created($"/subjects/strands/{result.Id}", result);
        });

        group.MapPut("/subjects/strands/{id:guid}", async (
            Guid id,
            [FromBody] UpdateTopicStrandRequest req,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<UpdateTopicStrand, SchoolCollab.Students.Core.DTOs.TopicStrandDto> handler,
            CancellationToken ct) =>
        {
            try
            {
                var result = await handler.HandleAsync(new UpdateTopicStrand(id, req.Name, req.Description, req.DisplayOrder), ct);
                return Results.Ok(result);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });

        group.MapDelete("/subjects/strands/{id:guid}", async (
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

        // ── Subject Lessons ───────────────────────────────────────────────────────

        group.MapGet("/subjects/{subjectId:guid}/lessons", async (
            Guid subjectId,
            Guid? strandId,
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<ListTopicLessons, SchoolCollab.Students.Core.DTOs.TopicLessonDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListTopicLessons(subjectId, strandId), ct)));

        group.MapPost("/subjects/lessons", async (
            [FromBody] CreateTopicLesson command,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<CreateTopicLesson, SchoolCollab.Students.Core.DTOs.TopicLessonDto> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(command, ct);
            return Results.Created($"/subjects/lessons/{result.Id}", result);
        });

        group.MapPut("/subjects/lessons/{id:guid}", async (
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

        group.MapPost("/subjects/lessons/{id:guid}/strand", async (
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

        group.MapDelete("/subjects/lessons/{id:guid}", async (
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

        return group;
    }
}

internal record UpdateTopicRequest(string Name, int DisplayOrder);
internal record UpdateTopicStrandRequest(string Name, string? Description, int DisplayOrder);
internal record UpdateTopicLessonRequest(string Name, string? Description, DateOnly? StartDate, DateOnly? EndDate, int DisplayOrder);
internal record AssignLessonStrandRequest(Guid? StrandId);

internal record CreateTopicForGradeRequest(
    Guid GradeLevelId,
    Guid? CodedValueId,
    string Code,
    string Name,
    int DisplayOrder);

internal record GetOrCreateTopicRequest(
    Guid GradeLevelId,
    Guid CodedValueId,
    string Code,
    string Name,
    int DisplayOrder);
