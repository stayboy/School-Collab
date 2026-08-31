using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Students.Core.CQRS.StudentTopicAssignments.Commands.AssignStudentTopic;
using SchoolCollab.Students.Core.CQRS.StudentTopicAssignments.Commands.RemoveStudentTopic;
using SchoolCollab.Students.Core.CQRS.StudentTopicAssignments.Queries.ListStudentTopicAssignmentsByPeriod;
using SchoolCollab.Students.Core.CQRS.StudentTopicAssignments.Queries.ListStudentTopicAssignmentsByStudent;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Api.Endpoints;

public static class StudentTopicAssignmentRoutes
{
    public static RouteGroupBuilder MapStudentTopicAssignmentRoutes(this RouteGroupBuilder group)
    {
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
