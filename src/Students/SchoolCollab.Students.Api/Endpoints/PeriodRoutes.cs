using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.ActivatePeriod;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.CompletePeriod;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.CreatePeriod;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.UpdatePeriod;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.CQRS.Periods.Queries.GetPeriodById;
using SchoolCollab.Students.Core.CQRS.Periods.Queries.ListPeriods;

namespace SchoolCollab.Students.Api.Endpoints;

public static class PeriodRoutes
{
    public static RouteGroupBuilder MapPeriodRoutes(this RouteGroupBuilder group)
    {
        // ── Periods ───────────────────────────────────────────────────────────────

        group.MapGet("/periods", async (
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<ListPeriods, SchoolCollab.Students.Core.DTOs.PeriodDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListPeriods(), ct)));

        group.MapGet("/periods/{id:guid}", async (
            Guid id,
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<GetPeriodById, SchoolCollab.Students.Core.DTOs.PeriodDto?> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetPeriodById(id), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapPost("/periods", async (
            [FromBody] CreatePeriod command,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<CreatePeriod, Guid> handler,
            CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(command, ct);
            return Results.Created($"/periods/{id}", new { id });
        });

        group.MapPut("/periods/{id:guid}", async (
            Guid id,
            [FromBody] UpdatePeriodRequest req,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<UpdatePeriod> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new UpdatePeriod(id, req.Name, req.StartDate,
                    req.EndDate, req.AllowSubjectOverrides), ct);
                return Results.NoContent();
            }
            catch (PeriodNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ConcurrencyException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
        });

        group.MapPost("/periods/{id:guid}/activate", async (
            Guid id,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<ActivatePeriod> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new ActivatePeriod(id), ct);
                return Results.NoContent();
            }
            catch (PeriodNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ConcurrencyException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
        });

        group.MapPost("/periods/{id:guid}/complete", async (
            Guid id,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<CompletePeriod> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new CompletePeriod(id), ct);
                return Results.NoContent();
            }
            catch (PeriodNotFoundException)
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

internal record UpdatePeriodRequest(string Name, DateOnly StartDate, DateOnly EndDate, bool AllowSubjectOverrides);
