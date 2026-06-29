using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Students.Core.CQRS.GradeLevels.Commands.CreateGradeLevel;
using SchoolCollab.Students.Core.CQRS.GradeLevels.Commands.UpdateGradeLevel;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.CQRS.GradeLevels.Queries.GetGradeLevelById;
using SchoolCollab.Students.Core.CQRS.GradeLevels.Queries.ListGradeLevels;

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

        group.MapGet("/grade-levels/{id:guid}", async (
            Guid id,
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<GetGradeLevelById, SchoolCollab.Students.Core.DTOs.GradeLevelDto?> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetGradeLevelById(id), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

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
                await handler.HandleAsync(new UpdateGradeLevel(id, req.Level, req.Name, req.DisplayOrder), ct);
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

        return group;
    }
}

internal record UpdateGradeLevelRequest(int Level, string Name, int DisplayOrder);
