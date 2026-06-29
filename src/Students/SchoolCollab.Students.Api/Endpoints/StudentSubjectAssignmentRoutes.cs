using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Students.Core.CQRS.StudentSubjectAssignments.Commands.AssignStudentSubject;
using SchoolCollab.Students.Core.CQRS.StudentSubjectAssignments.Commands.RemoveStudentSubject;
using SchoolCollab.Students.Core.CQRS.StudentSubjectAssignments.Queries.ListStudentSubjectAssignmentsByPeriod;
using SchoolCollab.Students.Core.CQRS.StudentSubjectAssignments.Queries.ListStudentSubjectAssignmentsByStudent;

namespace SchoolCollab.Students.Api.Endpoints;

public static class StudentSubjectAssignmentRoutes
{
    public static RouteGroupBuilder MapStudentSubjectAssignmentRoutes(this RouteGroupBuilder group)
    {
        // ── Student Subject Assignments ───────────────────────────────────────────

        group.MapGet("/student-subjects/by-student/{studentId:guid}/period/{periodId:guid}", async (
            Guid studentId,
            Guid periodId,
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<ListStudentSubjectAssignmentsByStudent, SchoolCollab.Students.Core.DTOs.StudentSubjectAssignmentDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListStudentSubjectAssignmentsByStudent(studentId, periodId), ct)));

        group.MapGet("/student-subjects/by-period/{periodId:guid}", async (
            Guid periodId,
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<ListStudentSubjectAssignmentsByPeriod, SchoolCollab.Students.Core.DTOs.StudentSubjectAssignmentDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListStudentSubjectAssignmentsByPeriod(periodId), ct)));

        group.MapPost("/student-subjects", async (
            [FromBody] AssignStudentSubject command,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<AssignStudentSubject, Guid> handler,
            CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(command, ct);
            return Results.Created($"/student-subjects/{id}", new { id });
        });

        group.MapDelete("/student-subjects/{id:guid}", async (
            Guid id,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<RemoveStudentSubject> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new RemoveStudentSubject(id), ct);
                return Results.NoContent();
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(new { ex.Message });
            }
        });

        return group;
    }
}
