using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Students.Core.CQRS.Enrollments.Commands.EnrollStudent;
using SchoolCollab.Students.Core.CQRS.Enrollments.Commands.TransferStudent;
using SchoolCollab.Students.Core.CQRS.Enrollments.Commands.WithdrawStudent;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.CQRS.Enrollments.Queries.ListEnrollmentsByPeriod;
using SchoolCollab.Students.Core.CQRS.Enrollments.Queries.ListEnrollmentsByStudent;
using SchoolCollab.Students.Core.CQRS.Enrollments.Queries.ListEnrollmentsByStudents;

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

        // Bulk variant — all enrollments for many students in one round-trip.
        // Used by the client-side student-list enrichment (EnrichStudentsAsync)
        // to hydrate CurrentGrade without an N+1 per-student fetch.
        group.MapGet("/enrollments/by-students", async (
            [FromQuery] Guid[] studentIds,
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<ListEnrollmentsByStudents, SchoolCollab.Students.Core.DTOs.StudentEnrollmentDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListEnrollmentsByStudents(studentIds), ct)));

        group.MapPost("/enrollments", async (
            [FromBody] EnrollStudentRequest req,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<EnrollStudent, Guid> handler,
            CancellationToken ct) =>
        {
            try
            {
                var id = await handler.HandleAsync(
                    new EnrollStudent(req.StudentId, req.PeriodId, req.GradeLevelId, req.StreamCodedValueId, req.EnrolledOn), ct);
                return Results.Created($"/enrollments/{id}", new { id });
            }
            catch (StudentNotFoundException)
            {
                return Results.NotFound();
            }
            catch (PeriodNotOpenException ex)
            {
                return Results.BadRequest(new { ex.Message });
            }
            catch (StreamGradeMismatchException ex)
            {
                return Results.BadRequest(new { ex.Message });
            }
            catch (GradeLevelNotFoundException ex)
            {
                return Results.NotFound(new { ex.Message });
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
                await handler.HandleAsync(new TransferStudent(id, req.NewGradeLevelId, req.NewStreamCodedValueId, req.TransferDate, req.Reason), ct);
                return Results.NoContent();
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(new { ex.Message });
            }
            catch (StreamGradeMismatchException ex)
            {
                return Results.BadRequest(new { ex.Message });
            }
            catch (GradeLevelNotFoundException ex)
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

internal record EnrollStudentRequest(Guid StudentId, Guid PeriodId, Guid GradeLevelId, Guid? StreamCodedValueId, DateOnly? EnrolledOn);
internal record TransferStudentRequest(Guid NewGradeLevelId, Guid? NewStreamCodedValueId, DateOnly? TransferDate, string Reason);
internal record WithdrawStudentRequest(DateOnly? ExitDate);
