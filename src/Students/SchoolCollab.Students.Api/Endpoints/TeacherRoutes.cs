using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;
using SchoolCollab.Students.Core.CQRS.Teachers.Commands.CreateTeacher;
using SchoolCollab.Students.Core.CQRS.Teachers.Commands.DeleteTeacher;
using SchoolCollab.Students.Core.CQRS.Teachers.Commands.LinkTeacherGradeLevel;
using SchoolCollab.Students.Core.CQRS.Teachers.Commands.LinkTeacherTopic;
using SchoolCollab.Students.Core.CQRS.Teachers.Commands.SetTeacherGradeLevelRole;
using SchoolCollab.Students.Core.CQRS.Teachers.Commands.UnlinkTeacherGradeLevel;
using SchoolCollab.Students.Core.CQRS.Teachers.Commands.UnlinkTeacherTopic;
using SchoolCollab.Students.Core.CQRS.Teachers.Commands.UpdateTeacher;
using SchoolCollab.Students.Core.CQRS.Teachers.Queries.GetTeacherById;
using SchoolCollab.Students.Core.CQRS.Teachers.Queries.ListGradeLevelsForTeacher;
using SchoolCollab.Students.Core.CQRS.Teachers.Queries.ListTeachersForGradeLevel;
using SchoolCollab.Students.Core.CQRS.Teachers.Queries.ListTopicsForTeacher;
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
                await handler.HandleAsync(new UpdateTeacher(id, req.FirstName, req.LastName, req.DisplayName, req.Email, req.ContactPhone), ct);
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

        // Topic links (spec §4.12). Subject->Topic rename (FR-13).
        group.MapGet("/{id:guid}/topics", async (
            Guid id,
            [FromServices] IQueryHandler<ListTopicsForTeacher, TopicDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListTopicsForTeacher(id), ct)));

        group.MapPost("/{id:guid}/topics", async (
            Guid id,
            [FromBody] LinkTeacherTopicRequest req,
            [FromServices] ICommandHandler<LinkTeacherTopic> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new LinkTeacherTopic(id, req.TopicId), ct);
                return Results.NoContent();
            }
            catch (TeacherNotFoundException) { return Results.NotFound(); }
            catch (TopicNotFoundException) { return Results.NotFound(); }
            catch (TeacherLinkAlreadyExistsException ex) { return Results.Conflict(new { ex.Message }); }
        });

        group.MapDelete("/{id:guid}/topics/{topicId:guid}", async (
            Guid id,
            Guid topicId,
            [FromServices] ICommandHandler<UnlinkTeacherTopic> handler,
            CancellationToken ct) =>
        {
            await handler.HandleAsync(new UnlinkTeacherTopic(id, topicId), ct);
            return Results.NoContent();
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
                await handler.HandleAsync(new LinkTeacherGradeLevel(id, req.GradeLevelId, req.TeacherRoleCodedValueId), ct);
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

        return group;
    }
}

internal record UpdateTeacherRequest(
    string FirstName,
    string LastName,
    string? DisplayName,
    string Email,
    string? ContactPhone);

internal record LinkTeacherTopicRequest(Guid TopicId);

internal record LinkTeacherGradeLevelRequest(Guid GradeLevelId, Guid? TeacherRoleCodedValueId = null);

internal record SetTeacherGradeLevelRoleRequest(Guid? TeacherRoleCodedValueId);
