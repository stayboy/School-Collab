using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Students.Core.CQRS.GradeLevels.Commands.CreateGradeLevel;
using SchoolCollab.Students.Core.CQRS.GradeLevels.Commands.DeleteGradeLevel;
using SchoolCollab.Students.Core.CQRS.GradeLevels.Commands.GetOrCreateGradeLevel;
using SchoolCollab.Students.Core.CQRS.GradeLevels.Commands.UpdateGradeLevel;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.CQRS.GradeLevels.Queries.GetGradeLevelByCodedValue;
using SchoolCollab.Students.Core.CQRS.GradeLevels.Queries.GetGradeLevelById;
using SchoolCollab.Students.Core.CQRS.GradeLevels.Queries.ListGradeLevels;
using SchoolCollab.Students.Core.CQRS.GradeLevels.Queries.ListGradeLevelsForLanding;

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
                    new GetOrCreateGradeLevel(req.CodedValueId, req.Level, req.Name, req.DisplayOrder), ct);
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

        return group;
    }
}

internal record UpdateGradeLevelRequest(int Level, string Name, int DisplayOrder);
internal record GetOrCreateGradeLevelRequest(Guid CodedValueId, int Level, string Name, int DisplayOrder);
