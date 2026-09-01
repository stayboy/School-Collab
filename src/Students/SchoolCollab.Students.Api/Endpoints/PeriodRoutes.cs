using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.ActivatePeriod;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.ArchivePeriod;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.CompletePeriod;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.CreatePeriod;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.DeactivatePeriod;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.DeletePeriod;
using SchoolCollab.Students.Core.CQRS.Periods.Commands.UpdatePeriod;
using SchoolCollab.Students.Core.CQRS.Periods.Queries.GetActiveAcademicYear;
using SchoolCollab.Students.Core.CQRS.Periods.Queries.GetActiveSubPeriod;
using SchoolCollab.Students.Core.CQRS.Periods.Queries.GetPeriodById;
using SchoolCollab.Students.Core.CQRS.Periods.Queries.ListPeriods;
using SchoolCollab.Students.Core.CQRS.Periods.Queries.ListTopLevelPeriods;
using SchoolCollab.Students.Core.CQRS.Periods.Queries.ListSubPeriods;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.DTOs;

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

        // Top-level periods only (landing grid): parents with server-computed
        // sub-period counts — no sub-period rows are returned for display.
        group.MapGet("/periods/top-level", async (
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<ListTopLevelPeriods, PeriodLandingDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListTopLevelPeriods(), ct)));

        group.MapGet("/periods/{id:guid}", async (
            Guid id,
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<GetPeriodById, SchoolCollab.Students.Core.DTOs.PeriodDto?> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetPeriodById(id), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        // ── H4.3 (FR-H12): hierarchy reads ────────────────────────────────────────

        group.MapGet("/periods/active-academic-year", async (
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<GetActiveAcademicYear, PeriodDto?> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetActiveAcademicYear(), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapGet("/periods/active-sub-period", async (
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<GetActiveSubPeriod, PeriodDto?> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetActiveSubPeriod(), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapGet("/periods/{academicYearId:guid}/sub-periods", async (
            Guid academicYearId,
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<ListSubPeriods, PeriodDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListSubPeriods(academicYearId), ct)));

        group.MapPost("/periods", async (
            [FromBody] CreatePeriod command,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<CreatePeriod, CreatePeriodResult> handler,
            CancellationToken ct) =>
        {
            try
            {
                var result = await handler.HandleAsync(command, ct);
                return Results.Created($"/periods/{result.YearId}", new { id = result.YearId, subPeriodIds = result.SubPeriodIds });
            }
            catch (PeriodFrameworkMismatchException ex)
            {
                return Results.Json(new { ex.Message }, statusCode: 422);
            }
            catch (PeriodContainmentException ex)
            {
                return Results.Json(new { ex.Message }, statusCode: 422);
            }
            catch (PeriodOverlapException ex)
            {
                return Results.Json(new { ex.Message }, statusCode: 422);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { ex.Message });
            }
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
                    req.EndDate, req.ParentPeriodId), ct);
                return Results.NoContent();
            }
            catch (PeriodNotFoundException)
            {
                return Results.NotFound();
            }
            catch (PeriodFrameworkMismatchException ex)
            {
                return Results.Json(new { ex.Message }, statusCode: 422);
            }
            catch (PeriodContainmentException ex)
            {
                return Results.Json(new { ex.Message }, statusCode: 422);
            }
            catch (PeriodOverlapException ex)
            {
                return Results.Json(new { ex.Message }, statusCode: 422);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { ex.Message });
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
            catch (PeriodGuardException ex)
            {
                return Results.Json(new { ex.Message }, statusCode: 422);
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

        group.MapPost("/periods/{id:guid}/deactivate", async (
            Guid id,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<DeactivatePeriod> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new DeactivatePeriod(id), ct);
                return Results.NoContent();
            }
            catch (PeriodNotFoundException)
            {
                return Results.NotFound();
            }
            catch (PeriodNotDeactivatableException ex)
            {
                return Results.Json(new { ex.Message }, statusCode: 422);
            }
            catch (ConcurrencyException)
            {
                // NFR-E2: an already-gone / concurrently-deactivated period resolves to a
                // 404 (idempotent), never a 409.
                return Results.NotFound();
            }
        });

        group.MapDelete("/periods/{id:guid}", async (
            Guid id,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<DeletePeriod> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new DeletePeriod(id), ct);
                return Results.NoContent();
            }
            catch (PeriodNotFoundException)
            {
                return Results.NotFound();
            }
            catch (PeriodNotDeletableException ex)
            {
                return Results.Json(new { ex.Message }, statusCode: 422);
            }
            catch (ConcurrencyException)
            {
                // EC-3 / NFR-D2: delete-after-delete or concurrent-edit delete is an
                // idempotent 404 — delete routes never return 409.
                return Results.NotFound();
            }
        });

        group.MapPost("/periods/{id:guid}/archive", async (
            Guid id,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<ArchivePeriod> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new ArchivePeriod(id), ct);
                return Results.NoContent();
            }
            catch (PeriodNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ConcurrencyException)
            {
                return Results.NotFound();
            }
        });

        return group;
    }
}

internal record UpdatePeriodRequest(
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    Guid? ParentPeriodId = null);
