using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Students.Core.CQRS.Subjects.Commands.AssignLessonStrand;
using SchoolCollab.Students.Core.CQRS.Subjects.Commands.CreateSubject;
using SchoolCollab.Students.Core.CQRS.Subjects.Commands.CreateSubjectForGrade;
using SchoolCollab.Students.Core.CQRS.Subjects.Commands.CreateSubjectLesson;
using SchoolCollab.Students.Core.CQRS.Subjects.Commands.CreateSubjectStrand;
using SchoolCollab.Students.Core.CQRS.Subjects.Commands.DeleteSubject;
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
using SchoolCollab.Students.Core.CQRS.Subjects.Queries.ListSubjectsByGrade;

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

        group.MapGet("/subjects/by-grade/{gradeLevelId:guid}", async (
            Guid gradeLevelId,
            Guid? periodId,
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<ListSubjectsByGrade, SchoolCollab.Students.Core.DTOs.SubjectDto[]> handler,
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
                    new ListSubjectsByGrade(gradeLevelId, periodId), ct);
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

        // ── Find-or-create Subject by CodedValueId (wizard's "Add to grade" flow) ─
        group.MapPost("/subjects/get-or-create", async (
            [FromBody] CreateSubject command,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<SchoolCollab.Students.Core.CQRS.Subjects.Commands.GetOrCreateSubject.GetOrCreateSubject, SchoolCollab.Students.Core.DTOs.SubjectDto> handler,
            CancellationToken ct) =>
        {
            var dto = await handler.HandleAsync(
                new SchoolCollab.Students.Core.CQRS.Subjects.Commands.GetOrCreateSubject.GetOrCreateSubject(
                    command.CodedValueId, command.Code, command.Name, command.DisplayOrder), ct);
            return Results.Ok(dto);
        });

        // ── Create Subject + GradeSubjectAssignment for the current period (§8.1) ─
        group.MapPost("/subjects/for-grade", async (
            [FromBody] CreateSubjectForGradeRequest req,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<CreateSubjectForGrade, SchoolCollab.Students.Core.DTOs.SubjectDto> handler,
            CancellationToken ct) =>
        {
            try
            {
                var dto = await handler.HandleAsync(
                    new CreateSubjectForGrade(req.GradeLevelId, req.CodedValueId, req.Code, req.Name, req.DisplayOrder), ct);
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

        group.MapDelete("/subjects/{id:guid}", async (
            Guid id,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<DeleteSubject> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new DeleteSubject(id), ct);
                return Results.NoContent();
            }
            catch (SubjectNotFoundException)
            {
                return Results.NotFound();
            }
            catch (SubjectReferencedException ex)
            {
                return Results.Conflict(new { ex.Message, ex.References });
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

internal record CreateSubjectForGradeRequest(
    Guid GradeLevelId,
    Guid? CodedValueId,
    string Code,
    string Name,
    int DisplayOrder);
