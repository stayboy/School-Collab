using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Students.Core.CQRS.GradeSubjectAssignments.Commands.AssignGradeSubject;
using SchoolCollab.Students.Core.CQRS.GradeSubjectAssignments.Commands.RemoveGradeSubject;
using SchoolCollab.Students.Core.CQRS.GradeSubjectAssignments.Commands.UpdateGradeSubjectTags;
using SchoolCollab.Students.Core.CQRS.GradeSubjectAssignments.Queries.ListGradeSubjectAssignmentsByGradeLevel;
using SchoolCollab.Students.Core.CQRS.GradeSubjectAssignments.Queries.ListGradeSubjectAssignmentsByPeriod;

namespace SchoolCollab.Students.Api.Endpoints;

public static class GradeSubjectAssignmentRoutes
{
    public static RouteGroupBuilder MapGradeSubjectAssignmentRoutes(this RouteGroupBuilder group)
    {
        // ── Grade Subject Assignments ─────────────────────────────────────────────

        group.MapGet("/grade-subjects/by-period/{periodId:guid}", async (
            Guid periodId,
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<ListGradeSubjectAssignmentsByPeriod, SchoolCollab.Students.Core.DTOs.GradeSubjectAssignmentDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListGradeSubjectAssignmentsByPeriod(periodId), ct)));

        group.MapGet("/grade-subjects/by-grade/{gradeLevelId:guid}/period/{periodId:guid}", async (
            Guid gradeLevelId,
            Guid periodId,
            [FromServices] SchoolCollab.Core.CQRS.IQueryHandler<ListGradeSubjectAssignmentsByGradeLevel, SchoolCollab.Students.Core.DTOs.GradeSubjectAssignmentDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListGradeSubjectAssignmentsByGradeLevel(gradeLevelId, periodId), ct)));

        group.MapPost("/grade-subjects", async (
            [FromBody] AssignGradeSubject command,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<AssignGradeSubject, Guid> handler,
            CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(command, ct);
            return Results.Created($"/grade-subjects/{id}", new { id });
        });

        group.MapPut("/grade-subjects/{id:guid}/tags", async (
            Guid id,
            [FromBody] UpdateGradeSubjectTagsRequest req,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<UpdateGradeSubjectTags, SchoolCollab.Students.Core.DTOs.GradeSubjectAssignmentDto> handler,
            CancellationToken ct) =>
        {
            try
            {
                var result = await handler.HandleAsync(new UpdateGradeSubjectTags(id, req.SubjectStrandId, req.SubjectLessonId), ct);
                return Results.Ok(result);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });

        group.MapDelete("/grade-subjects/{id:guid}", async (
            Guid id,
            [FromServices] SchoolCollab.Core.CQRS.ICommandHandler<RemoveGradeSubject> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new RemoveGradeSubject(id), ct);
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

internal record UpdateGradeSubjectTagsRequest(Guid? SubjectStrandId, Guid? SubjectLessonId);
