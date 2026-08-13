using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;
using SchoolCollab.Students.Core.CQRS.Teachers.Commands.CreateTeacher;
using SchoolCollab.Students.Core.CQRS.Teachers.Commands.CreateTeacherWithAssignments;
using SchoolCollab.Students.Core.CQRS.Teachers.Commands.DeleteTeacher;
using SchoolCollab.Students.Core.CQRS.Teachers.Commands.LinkTeacherGradeLevel;
using SchoolCollab.Students.Core.CQRS.Teachers.Commands.LinkTeacherActivityAssignment;
using SchoolCollab.Students.Core.CQRS.Teachers.Commands.DeleteTeacherGradeAssignment;
using SchoolCollab.Students.Core.CQRS.Teachers.Commands.DeleteTeacherActivityAssignment;
using SchoolCollab.Students.Core.CQRS.Teachers.Queries.ListTeacherGradeAssignments;
using SchoolCollab.Students.Core.CQRS.Teachers.Queries.ListTeacherActivityAssignments;
using SchoolCollab.Students.Core.CQRS.Teachers.Commands.SetTeacherGradeLevelRole;
using SchoolCollab.Students.Core.CQRS.Teachers.Commands.UnlinkTeacherGradeLevel;
using SchoolCollab.Students.Core.CQRS.Teachers.Commands.UpdateTeacher;
using SchoolCollab.Students.Core.CQRS.Teachers.Queries.GetTeacherById;
using SchoolCollab.Students.Core.CQRS.Teachers.Queries.ListGradeLevelsForTeacher;
using SchoolCollab.Students.Core.CQRS.Teachers.Queries.ListTeachersForGradeLevel;
using SchoolCollab.Students.Core.CQRS.Teachers.Queries.ListTeachers;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Api.Endpoints;

/// <summary>Teacher onboarding + subject/grade links (spec §4.12 / Phase 8). G2: admin/teacher-only.</summary>
public static class TeacherRoutes
{
    public static RouteGroupBuilder MapTeacherRoutes(this RouteGroupBuilder group)
    {
        group.MapPost("/", async (
            [FromBody] CreateTeacher command,
            [FromServices] ICommandHandler<CreateTeacher, Guid> handler,
            CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(command, ct);
            return Results.Created($"/teachers/{id}", new { id });
        });

        // Atomic create: teacher + qualifications + grade/activity assignments in
        // one transaction (Unit of Work). Any failure rolls back the whole batch.
        group.MapPost("/with-assignments", async (
            [FromBody] CreateTeacherWithAssignments command,
            [FromServices] ICommandHandler<CreateTeacherWithAssignments, Guid> handler,
            CancellationToken ct) =>
        {
            try
            {
                var id = await handler.HandleAsync(command, ct);
                return Results.Created($"/teachers/{id}", new { id });
            }
            catch (GradeLevelNotFoundException) { return Results.NotFound(); }
            catch (ActivityGroupNotFoundException) { return Results.NotFound(); }
            catch (TeacherLinkAlreadyExistsException ex) { return Results.Conflict(new { ex.Message }); }
        });

        group.MapGet("/", async (
            [FromServices] IQueryHandler<ListTeachers, TeacherDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListTeachers(), ct)));

        group.MapGet("/{id:guid}", async (
            Guid id,
            [FromServices] IQueryHandler<GetTeacherById, TeacherDto?> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetTeacherById(id), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapPut("/{id:guid}", async (
            Guid id,
            [FromBody] UpdateTeacherRequest req,
            [FromServices] ICommandHandler<UpdateTeacher> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new UpdateTeacher(
                    id, req.FirstName, req.LastName, req.DisplayName,
                    req.GenderCodedValueId, req.DateOfBirth, req.LevelOfEducationCodedValueId,
                    req.QualificationCodedValueIds), ct);
                return Results.NoContent();
            }
            catch (TeacherNotFoundException)
            {
                return Results.NotFound();
            }
        });

        group.MapDelete("/{id:guid}", async (
            Guid id,
            [FromServices] ICommandHandler<DeleteTeacher> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new DeleteTeacher(id), ct);
                return Results.NoContent();
            }
            catch (TeacherNotFoundException)
            {
                return Results.NotFound();
            }
        });

        // Grade-level links (spec §4.12).
        group.MapGet("/{id:guid}/grade-levels", async (
            Guid id,
            [FromServices] IQueryHandler<ListGradeLevelsForTeacher, GradeLevelDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListGradeLevelsForTeacher(id), ct)));

        group.MapPost("/{id:guid}/grade-levels", async (
            Guid id,
            [FromBody] LinkTeacherGradeLevelRequest req,
            [FromServices] ICommandHandler<LinkTeacherGradeLevel> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new LinkTeacherGradeLevel(id, req.GradeLevelId, null, req.TeacherRoleCodedValueId), ct);
                return Results.NoContent();
            }
            catch (TeacherNotFoundException) { return Results.NotFound(); }
            catch (GradeLevelNotFoundException) { return Results.NotFound(); }
            catch (TeacherLinkAlreadyExistsException ex) { return Results.Conflict(new { ex.Message }); }
        });

        group.MapDelete("/{id:guid}/grade-levels/{gradeLevelId:guid}", async (
            Guid id,
            Guid gradeLevelId,
            [FromServices] ICommandHandler<UnlinkTeacherGradeLevel> handler,
            CancellationToken ct) =>
        {
            await handler.HandleAsync(new UnlinkTeacherGradeLevel(id, gradeLevelId), ct);
            return Results.NoContent();
        });

        // Set/clear the coded-value role a teacher holds on a grade level
        // (grade-level-detail-view-plan.md §3.1). Idempotent at the domain layer.
        group.MapPatch("/{id:guid}/grade-levels/{gradeLevelId:guid}/role", async (
            Guid id,
            Guid gradeLevelId,
            [FromBody] SetTeacherGradeLevelRoleRequest req,
            [FromServices] ICommandHandler<SetTeacherGradeLevelRole> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new SetTeacherGradeLevelRole(id, gradeLevelId, req.TeacherRoleCodedValueId), ct);
                return Results.NoContent();
            }
            catch (TeacherLinkNotFoundException) { return Results.NotFound(); }
        });

        // v4 grade-scoped assignments (grade + optional subject + role).
        group.MapGet("/{id:guid}/grade-assignments", async (
            Guid id,
            [FromServices] IQueryHandler<ListTeacherGradeAssignments, TeacherGradeAssignmentDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListTeacherGradeAssignments(id), ct)));

        group.MapPost("/{id:guid}/grade-assignments", async (
            Guid id,
            [FromBody] LinkTeacherGradeAssignmentRequest req,
            [FromServices] ICommandHandler<LinkTeacherGradeLevel> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new LinkTeacherGradeLevel(id, req.GradeLevelId, req.SubjectId, req.RoleCodedValueId), ct);
                return Results.NoContent();
            }
            catch (TeacherNotFoundException) { return Results.NotFound(); }
            catch (GradeLevelNotFoundException) { return Results.NotFound(); }
            catch (TeacherLinkAlreadyExistsException ex) { return Results.Conflict(new { ex.Message }); }
        });

        group.MapDelete("/{id:guid}/grade-assignments/{rowId:guid}", async (
            Guid id,
            Guid rowId,
            [FromServices] ICommandHandler<DeleteTeacherGradeAssignment> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new DeleteTeacherGradeAssignment(id, rowId), ct);
                return Results.NoContent();
            }
            catch (TeacherLinkNotFoundException) { return Results.NotFound(); }
        });

        // v4 teacher↔activity assignments (activity + role + optional grades).
        group.MapGet("/{id:guid}/activity-assignments", async (
            Guid id,
            [FromServices] IQueryHandler<ListTeacherActivityAssignments, TeacherActivityAssignmentDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListTeacherActivityAssignments(id), ct)));

        group.MapPost("/{id:guid}/activity-assignments", async (
            Guid id,
            [FromBody] LinkTeacherActivityAssignmentRequest req,
            [FromServices] ICommandHandler<LinkTeacherActivityAssignment> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new LinkTeacherActivityAssignment(id, req.ActivityGroupId, req.RoleCodedValueId, req.GradeLevelIds), ct);
                return Results.NoContent();
            }
            catch (TeacherNotFoundException) { return Results.NotFound(); }
            catch (ActivityGroupNotFoundException) { return Results.NotFound(); }
        });

        group.MapDelete("/{id:guid}/activity-assignments/{rowId:guid}", async (
            Guid id,
            Guid rowId,
            [FromServices] ICommandHandler<DeleteTeacherActivityAssignment> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new DeleteTeacherActivityAssignment(id, rowId), ct);
                return Results.NoContent();
            }
            catch (TeacherLinkNotFoundException) { return Results.NotFound(); }
        });

        return group;
    }
}

internal record UpdateTeacherRequest(
    string FirstName,
    string LastName,
    string? DisplayName,
    Guid? GenderCodedValueId = null,
    DateOnly? DateOfBirth = null,
    Guid? LevelOfEducationCodedValueId = null,
    Guid[]? QualificationCodedValueIds = null);

internal record LinkTeacherGradeLevelRequest(Guid GradeLevelId, Guid? TeacherRoleCodedValueId = null);

internal record LinkTeacherGradeAssignmentRequest(Guid GradeLevelId, Guid? SubjectId = null, Guid? RoleCodedValueId = null);

internal record LinkTeacherActivityAssignmentRequest(Guid ActivityGroupId, Guid? RoleCodedValueId = null, Guid[]? GradeLevelIds = null);

internal record SetTeacherGradeLevelRoleRequest(Guid? TeacherRoleCodedValueId);
