using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Students.Core.CQRS.Students.Commands.CreateStudent;
using SchoolCollab.Students.Core.CQRS.Students.Commands.DeleteStudent;
using SchoolCollab.Students.Core.CQRS.Students.Commands.RecoverStudent;
using SchoolCollab.Students.Core.CQRS.Students.Commands.UpdateStudent;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.CQRS.Students.Queries.GetStudentById;
using SchoolCollab.Students.Core.CQRS.Students.Queries.GetStudentByStudentNumber;
using SchoolCollab.Students.Core.CQRS.Students.Queries.ListDeletedStudents;
using SchoolCollab.Students.Core.CQRS.Students.Queries.ListStudents;
using SchoolCollab.Students.Core.CQRS.Students.Queries.ListStudentsByGrade;

namespace SchoolCollab.Students.Api.Endpoints;

public static class StudentRoutes
{
    public static RouteGroupBuilder MapStudentRoutes(this RouteGroupBuilder group)
    {
        // ── Students ──────────────────────────────────────────────────────────────

        group.MapGet("/", async (
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<ListStudents, SchoolCollab.Students.Core.DTOs.StudentDto[]> handler,
            CancellationToken ct,
            [FromQuery] string? search = null) =>
            Results.Ok(await handler.HandleAsync(new ListStudents(search), ct)));

        group.MapGet("/{id:guid}", async (
            Guid id,
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<GetStudentById, SchoolCollab.Students.Core.DTOs.StudentDto?> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetStudentById(id), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapGet("/by-number/{studentNumber}", async (
            string studentNumber,
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<GetStudentByStudentNumber, SchoolCollab.Students.Core.DTOs.StudentDto?> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetStudentByStudentNumber(studentNumber), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapGet("/deleted", async (
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<ListDeletedStudents, SchoolCollab.Students.Core.DTOs.StudentDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListDeletedStudents(), ct)));

        group.MapGet("/by-grade/{gradeLevelId:guid}", async (
            Guid gradeLevelId,
            Guid? periodId,
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<ListStudentsByGrade, SchoolCollab.Students.Core.DTOs.StudentDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListStudentsByGrade(gradeLevelId, periodId), ct)));

        group.MapPost("/", async (
            [FromBody] CreateStudent command,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<CreateStudent, Guid> handler,
            CancellationToken ct) =>
        {
            try
            {
                var id = await handler.HandleAsync(command, ct);
                return Results.Created($"/students/{id}", new { id });
            }
            catch (DuplicateStudentNumberException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
        });

        group.MapPut("/{id:guid}", async (
            Guid id,
            [FromBody] UpdateStudentRequest req,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<UpdateStudent> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new UpdateStudent(id, req.FirstName, req.LastName,
                    req.DateOfBirth, req.GenderCodedValueId), ct);
                return Results.NoContent();
            }
            catch (StudentNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ConcurrencyException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
        });

        group.MapDelete("/{id:guid}", async (
            Guid id,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<DeleteStudent> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new DeleteStudent(id), ct);
                return Results.NoContent();
            }
            catch (StudentNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ConcurrencyException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
        });

        group.MapPost("/{id:guid}/recover", async (
            Guid id,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<RecoverStudent> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new RecoverStudent(id), ct);
                return Results.NoContent();
            }
            catch (StudentNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ConcurrencyException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
        });

        return group;
    }
}

internal record UpdateStudentRequest(string FirstName, string LastName, DateOnly? DateOfBirth, Guid? GenderCodedValueId);
