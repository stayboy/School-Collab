using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.CQRS.Guardians.Commands.CreateGuardian;
using SchoolCollab.Students.Core.CQRS.Guardians.Commands.DeleteGuardian;
using SchoolCollab.Students.Core.CQRS.Guardians.Commands.UpdateGuardian;
using SchoolCollab.Students.Core.CQRS.Guardians.Queries.GetGuardianById;
using SchoolCollab.Students.Core.CQRS.Guardians.Queries.GetGuardianNameHistory;
using SchoolCollab.Students.Core.CQRS.Guardians.Queries.ListGuardians;
using SchoolCollab.Students.Core.CQRS.Guardians.Queries.ListGuardiansByStudent;
using SchoolCollab.Students.Core.CQRS.Guardians.Queries.ListStudentCountsByGuardians;
using SchoolCollab.Students.Core.CQRS.Guardians.Queries.ListStudentsForGuardian;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Api.Endpoints;

public static class GuardianRoutes
{
    public static RouteGroupBuilder MapGuardianRoutes(this RouteGroupBuilder group)
    {
        group.MapPost("/", async (
            [FromBody] CreateGuardian command,
            [FromServices] ICommandHandler<CreateGuardian, Guid> handler,
            CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(command, ct);
            return Results.Created($"/guardians/{id}", new { id });
        });

        group.MapGet("/", async (
            [FromServices] IQueryHandler<ListGuardians, GuardianDto[]> handler,
            CancellationToken ct,
            [FromQuery] string? search = null,
            [FromQuery] Guid? excludeStudentId = null,
            [FromQuery] Guid? studentId = null) =>
            Results.Ok(await handler.HandleAsync(new ListGuardians(search, excludeStudentId, studentId), ct)));

        group.MapGet("/{id:guid}", async (
            Guid id,
            [FromServices] IQueryHandler<GetGuardianById, GuardianDto?> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetGuardianById(id), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapGet("/{id:guid}/name-history", async (
            Guid id,
            [FromServices] IQueryHandler<GetGuardianNameHistory, GuardianNameHistoryDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new GetGuardianNameHistory(id), ct)));

        group.MapGet("/{id:guid}/students", async (
            Guid id,
            [FromServices] IQueryHandler<ListStudentsForGuardian, StudentDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListStudentsForGuardian(id), ct)));

        // Bulk student-count endpoint: GET /guardians/student-counts?guardianIds=…
        // Returns the number of linked (non-deleted) students per guardian so
        // the guardians landing page can render an "N students" cell without an
        // N+1 per-guardian fetch. Mirrors /students/guardian-counts.
        group.MapGet("/student-counts", async (
            [FromQuery] Guid[] guardianIds,
            [FromServices] IQueryHandler<ListStudentCountsByGuardians, StudentCountDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListStudentCountsByGuardians(guardianIds), ct)));

        group.MapPut("/{id:guid}", async (
            Guid id,
            [FromBody] UpdateGuardianRequest req,
            [FromServices] ICommandHandler<UpdateGuardian> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new UpdateGuardian(id, req.TitleCodedValueId, req.FirstName,
                    req.LastName, req.DisplayName, req.Address, req.CommunityId, req.DateOfBirth, req.GenderCodedValueId), ct);
                return Results.NoContent();
            }
            catch (GuardianNotFoundException)
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
            [FromServices] ICommandHandler<DeleteGuardian> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new DeleteGuardian(id), ct);
                return Results.NoContent();
            }
            catch (GuardianNotFoundException)
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

internal record UpdateGuardianRequest(
    Guid? TitleCodedValueId, string FirstName, string LastName, string? DisplayName, string? Address, Guid? CommunityId, DateOnly? DateOfBirth, Guid? GenderCodedValueId);
