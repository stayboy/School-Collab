using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Students.Core.CQRS.Subjects.Commands.AssignLessonStrand;
using SchoolCollab.Students.Core.CQRS.Subjects.Commands.CreateSubject;
using SchoolCollab.Students.Core.CQRS.Subjects.Commands.CreateSubjectLesson;
using SchoolCollab.Students.Core.CQRS.Subjects.Commands.CreateSubjectStrand;
using SchoolCollab.Students.Core.CQRS.Subjects.Commands.RemoveSubjectLesson;
using SchoolCollab.Students.Core.CQRS.Subjects.Commands.RemoveSubjectStrand;
using SchoolCollab.Students.Core.CQRS.Subjects.Commands.UpdateSubject;
using SchoolCollab.Students.Core.CQRS.Subjects.Commands.UpdateSubjectLesson;
using SchoolCollab.Students.Core.CQRS.Subjects.Commands.UpdateSubjectStrand;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.CQRS.Subjects.Queries.GetSubjectByCode;
using SchoolCollab.Students.Core.CQRS.Subjects.Queries.GetSubjectById;
using SchoolCollab.Students.Core.CQRS.Subjects.Queries.ListSubjectLessons;
using SchoolCollab.Students.Core.CQRS.Subjects.Queries.ListSubjectStrands;
using SchoolCollab.Students.Core.CQRS.Subjects.Queries.ListSubjects;

namespace SchoolCollab.Students.Api.Endpoints;

public static class SubjectRoutes
{
    public static RouteGroupBuilder MapSubjectRoutes(this RouteGroupBuilder group)
    {
        // ── Subjects ──────────────────────────────────────────────────────────────

        group.MapGet("/subjects", async (
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<ListSubjects, SchoolCollab.Students.Core.DTOs.SubjectDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListSubjects(), ct)));

        group.MapGet("/subjects/{id:guid}", async (
            Guid id,
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<GetSubjectById, SchoolCollab.Students.Core.DTOs.SubjectDto?> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetSubjectById(id), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapGet("/subjects/by-code/{code}", async (
            string code,
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<GetSubjectByCode, SchoolCollab.Students.Core.DTOs.SubjectDto?> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetSubjectByCode(code), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapPost("/subjects", async (
            [FromBody] CreateSubject command,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<CreateSubject, Guid> handler,
            CancellationToken ct) =>
        {
            try
            {
                var id = await handler.HandleAsync(command, ct);
                return Results.Created($"/subjects/{id}", new { id });
            }
            catch (DuplicateSubjectCodeException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
        });

        group.MapPut("/subjects/{id:guid}", async (
            Guid id,
            [FromBody] UpdateSubjectRequest req,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<UpdateSubject> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new UpdateSubject(id, req.Name, req.DisplayOrder), ct);
                return Results.NoContent();
            }
            catch (SubjectNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ConcurrencyException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
        });

        // ── Subject Strands ───────────────────────────────────────────────────────

        group.MapGet("/subjects/{subjectId:guid}/strands", async (
            Guid subjectId,
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<ListSubjectStrands, SchoolCollab.Students.Core.DTOs.SubjectStrandDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListSubjectStrands(subjectId), ct)));

        group.MapPost("/subjects/strands", async (
            [FromBody] CreateSubjectStrand command,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<CreateSubjectStrand, SchoolCollab.Students.Core.DTOs.SubjectStrandDto> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(command, ct);
            return Results.Created($"/subjects/strands/{result.Id}", result);
        });

        group.MapPut("/subjects/strands/{id:guid}", async (
            Guid id,
            [FromBody] UpdateSubjectStrandRequest req,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<UpdateSubjectStrand, SchoolCollab.Students.Core.DTOs.SubjectStrandDto> handler,
            CancellationToken ct) =>
        {
            try
            {
                var result = await handler.HandleAsync(new UpdateSubjectStrand(id, req.Name, req.Description, req.DisplayOrder), ct);
                return Results.Ok(result);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });

        group.MapDelete("/subjects/strands/{id:guid}", async (
            Guid id,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<RemoveSubjectStrand> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new RemoveSubjectStrand(id), ct);
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
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<ListSubjectLessons, SchoolCollab.Students.Core.DTOs.SubjectLessonDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListSubjectLessons(subjectId, strandId), ct)));

        group.MapPost("/subjects/lessons", async (
            [FromBody] CreateSubjectLesson command,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<CreateSubjectLesson, SchoolCollab.Students.Core.DTOs.SubjectLessonDto> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(command, ct);
            return Results.Created($"/subjects/lessons/{result.Id}", result);
        });

        group.MapPut("/subjects/lessons/{id:guid}", async (
            Guid id,
            [FromBody] UpdateSubjectLessonRequest req,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<UpdateSubjectLesson, SchoolCollab.Students.Core.DTOs.SubjectLessonDto> handler,
            CancellationToken ct) =>
        {
            try
            {
                var result = await handler.HandleAsync(new UpdateSubjectLesson(id, req.Name, req.Description, req.StartDate, req.EndDate, req.DisplayOrder), ct);
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
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<AssignLessonStrand, SchoolCollab.Students.Core.DTOs.SubjectLessonDto> handler,
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
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<RemoveSubjectLesson> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new RemoveSubjectLesson(id), ct);
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

internal record UpdateSubjectRequest(string Name, int DisplayOrder);
internal record UpdateSubjectStrandRequest(string Name, string? Description, int DisplayOrder);
internal record UpdateSubjectLessonRequest(string Name, string? Description, DateOnly? StartDate, DateOnly? EndDate, int DisplayOrder);
internal record AssignLessonStrandRequest(Guid? StrandId);
