using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.CQRS.StudentTopicAssignments.Commands.AssignStudentTopic;
using SchoolCollab.Students.Core.CQRS.StudentTopicAssignments.Commands.RemoveStudentTopic;
using SchoolCollab.Students.Core.CQRS.StudentTopicAssignments.Queries.ListStudentSubjects;
using SchoolCollab.Students.Core.CQRS.StudentTopicAssignments.Queries.ListStudentTopicAssignmentsByPeriod;
using SchoolCollab.Students.Core.CQRS.StudentTopicAssignments.Queries.ListStudentTopicAssignmentsByStudent;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Api.Endpoints;

public static class StudentTopicAssignmentRoutes
{
    public static RouteGroupBuilder MapStudentTopicAssignmentRoutes(this RouteGroupBuilder group)
    {
        // ── A student's subjects (subject-enrollment-requirement-flag.md §6 / FR-ER-16) ──
        // The union: every false subject on the student's grade plus every true subject the
        // student holds a row for in the active academic year. The grade is derived
        // server-side from the student's active enrolment — a caller-supplied grade could
        // disagree with the actual placement and silently return an empty or wrong union.
        // No active academic year or no active enrolment ⇒ 200 with an empty array, never 404.
        group.MapGet("/{studentId:guid}/subjects", async (
            Guid studentId,
            [FromServices] IQueryHandler<ListStudentSubjects, StudentSubjectEnrollmentDto[]> handler,
            CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListStudentSubjects(studentId), ct)));

        // ── Student Topic Assignments ───────────────────────────────────────────

        group.MapGet("/student-topics/by-student/{studentId:guid}/period/{periodId:guid}", async (
            Guid studentId,
            Guid periodId,
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<ListStudentTopicAssignmentsByStudent, SchoolCollab.Students.Core.DTOs.StudentTopicAssignmentDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListStudentTopicAssignmentsByStudent(studentId, periodId), ct)));

        group.MapGet("/student-topics/by-period/{periodId:guid}", async (
            Guid periodId,
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<ListStudentTopicAssignmentsByPeriod, SchoolCollab.Students.Core.DTOs.StudentTopicAssignmentDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListStudentTopicAssignmentsByPeriod(periodId), ct)));

        group.MapPost("/student-topics", async (
            [FromBody] AssignStudentTopic command,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<AssignStudentTopic, Guid> handler,
            CancellationToken ct) =>
        {
            try
            {
                var id = await handler.HandleAsync(command, ct);
                return Results.Created($"/student-topics/{id}", new { id });
            }
            catch (TopicAssignmentPeriodException ex)
            {
                return Results.Json(new { ex.Message }, statusCode: 422);
            }
            catch (PeriodNotOpenException ex)
            {
                return Results.Json(new { ex.Message }, statusCode: 409);
            }
        });

        group.MapDelete("/student-topics/{id:guid}", async (
            Guid id,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<RemoveStudentTopic> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new RemoveStudentTopic(id), ct);
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
