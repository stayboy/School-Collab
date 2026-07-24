using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Students.Core.CQRS.Enrollments.Commands.EnrollStudent;
using SchoolCollab.Students.Core.CQRS.Enrollments.Commands.TransferStudent;
using SchoolCollab.Students.Core.CQRS.Enrollments.Commands.WithdrawStudent;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.CQRS.Enrollments.Queries.ListEnrollmentsByPeriod;
using SchoolCollab.Students.Core.CQRS.Enrollments.Queries.ListEnrollmentsByStudent;

namespace SchoolCollab.Students.Api.Endpoints;

public static class EnrollmentRoutes
{
    public static RouteGroupBuilder MapEnrollmentRoutes(this RouteGroupBuilder group)
    {
        // ── Enrollments ───────────────────────────────────────────────────────────

        group.MapGet("/enrollments/by-student/{studentId:guid}", async (
            Guid studentId,
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<ListEnrollmentsByStudent, SchoolCollab.Students.Core.DTOs.StudentEnrollmentDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListEnrollmentsByStudent(studentId), ct)));

        group.MapGet("/enrollments/by-period/{periodId:guid}", async (
            Guid periodId,
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<ListEnrollmentsByPeriod, SchoolCollab.Students.Core.DTOs.StudentEnrollmentDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListEnrollmentsByPeriod(periodId), ct)));

        group.MapPost("/enrollments", async (
            [FromBody] EnrollStudent command,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<EnrollStudent, Guid> handler,
            CancellationToken ct) =>
        {
            try
            {
                var id = await handler.HandleAsync(command, ct);
                return Results.Created($"/enrollments/{id}", new { id });
            }
            catch (StudentNotFoundException)
            {
                return Results.NotFound();
            }
            catch (PeriodNotOpenException ex)
            {
                // The enroll handler throws PeriodNotOpenException when
                // the tenant has no active period or when the request's
                // PeriodId does not match the active one (FR-A3 in
                // active-period-per-tenancy.md). Surface the full
                // exception message in a 400 response body so the
                // client's <EnrollStudentAsync> can include it in
                // the tracing detail the user sees in the dialog's
                // per-field error MessageBar. Without this catch the
                // exception bubbles up as a 500 with no body, and the
                // client only sees the generic "Response status code
                // does not indicate success: 500" text — useless for
                // tracing WHAT went wrong.
                return Results.BadRequest(new { ex.Message });
            }
            catch (ConcurrencyException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
        });

        group.MapPost("/enrollments/{id:guid}/transfer", async (
            Guid id,
            [FromBody] TransferStudentRequest req,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<TransferStudent> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new TransferStudent(id, req.NewGradeLevelId, req.TransferDate, req.Reason), ct);
                return Results.NoContent();
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(new { ex.Message });
            }
            catch (ConcurrencyException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
        });

        group.MapPost("/enrollments/{id:guid}/withdraw", async (
            Guid id,
            [FromBody] WithdrawStudentRequest req,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<WithdrawStudent> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new WithdrawStudent(id, req.ExitDate), ct);
                return Results.NoContent();
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(new { ex.Message });
            }
            catch (ConcurrencyException ex)
            {
                return Results.Conflict(new { ex.Message });
            }
        });

        return group;
    }
}

internal record TransferStudentRequest(Guid NewGradeLevelId, DateOnly? TransferDate, string Reason);
internal record WithdrawStudentRequest(DateOnly? ExitDate);
