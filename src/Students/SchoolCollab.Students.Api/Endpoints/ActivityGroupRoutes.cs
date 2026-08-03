using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.AddMembership;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.ArchiveActivityGroup;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.CreateActivityGroup;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.DeleteActivityGroup;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.ExitMembership;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.RemoveMembership;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.SuspendActivityGroup;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.UpdateActivityGroup;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Queries.GetActivityGroupById;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Queries.GetGroupMembers;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Queries.GetStudentGroups;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Queries.ListActivityGroups;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.DTOs;
using CoreCQRS = SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Api.Endpoints;

public static class ActivityGroupRoutes
{
    public static RouteGroupBuilder MapActivityGroupRoutes(this RouteGroupBuilder group)
    {
        // ── Activity group CRUD (spec §7.1) ────────────────────────────────────
        group.MapGet("/activity-groups", async (
            [FromServices] CoreCQRS.IQueryHandler<ListActivityGroups, ActivityGroupDto[]> h, CancellationToken ct) =>
            Results.Ok(await h.HandleAsync(new ListActivityGroups(), ct)));

        group.MapGet("/activity-groups/{id:guid}", async (
            Guid id, [FromServices] CoreCQRS.IQueryHandler<GetActivityGroupById, ActivityGroupDto?> h, CancellationToken ct) =>
        {
            var r = await h.HandleAsync(new GetActivityGroupById(id), ct);
            return r is null ? Results.NotFound() : Results.Ok(r);
        });

        group.MapPost("/activity-groups", async (
            [FromBody] CreateActivityGroupRequest req,
            [FromServices] CoreCQRS.ICommandHandler<CreateActivityGroup, Guid> h, CancellationToken ct) =>
        {
            var id = await h.HandleAsync(new CreateActivityGroup(req.Name, req.Description,
                req.Category, req.PeriodId, req.Capacity), ct);
            return Results.Created($"/activity-groups/{id}", new { id });
        });

        group.MapPut("/activity-groups/{id:guid}", async (
            Guid id, [FromBody] UpdateActivityGroupRequest req,
            [FromServices] CoreCQRS.ICommandHandler<UpdateActivityGroup> h, CancellationToken ct) =>
        {
            try { await h.HandleAsync(new UpdateActivityGroup(id, req.Name, req.Description,
                req.Category, req.PeriodId, req.Capacity), ct); return Results.NoContent(); }
            catch (ActivityGroupNotFoundException) { return Results.NotFound(); }
            catch (ConcurrencyException ex) { return Results.Conflict(new { ex.Message }); }
        });

        group.MapDelete("/activity-groups/{id:guid}", async (
            Guid id, [FromServices] CoreCQRS.ICommandHandler<DeleteActivityGroup> h, CancellationToken ct) =>
        {
            try { await h.HandleAsync(new DeleteActivityGroup(id), ct); return Results.NoContent(); }
            catch (ActivityGroupNotFoundException) { return Results.NotFound(); }
            catch (ActivityGroupReferencedException ex) { return Results.Conflict(new { ex.Message, ex.References }); }
        });

        group.MapPost("/activity-groups/{id:guid}/archive", async (
            Guid id, [FromServices] CoreCQRS.ICommandHandler<ArchiveActivityGroup> h, CancellationToken ct) =>
        { try { await h.HandleAsync(new ArchiveActivityGroup(id), ct); return Results.NoContent(); }
          catch (ActivityGroupNotFoundException) { return Results.NotFound(); } });

        group.MapPost("/activity-groups/{id:guid}/suspend", async (
            Guid id, [FromServices] CoreCQRS.ICommandHandler<SuspendActivityGroup> h, CancellationToken ct) =>
        { try { await h.HandleAsync(new SuspendActivityGroup(id), ct); return Results.NoContent(); }
          catch (ActivityGroupNotFoundException) { return Results.NotFound(); } });

        MapMembershipRoutes(group);
        return group;
    }

    private static void MapMembershipRoutes(RouteGroupBuilder group)
    {
        // ── Membership (spec §7.2) ────────────────────────────────────────────
        group.MapGet("/activity-groups/{groupId:guid}/members", async (
            Guid groupId, [FromServices] CoreCQRS.IQueryHandler<GetGroupMembers, MembershipDto[]> h, CancellationToken ct) =>
            Results.Ok(await h.HandleAsync(new GetGroupMembers(groupId), ct)));

        group.MapPost("/activity-groups/{groupId:guid}/members", async (
            Guid groupId, [FromBody] AddMemberRequest req,
            [FromServices] CoreCQRS.ICommandHandler<AddMembership, Guid> h, CancellationToken ct) =>
        {
            try
            {
                var id = await h.HandleAsync(new AddMembership(groupId, req.StudentId, req.JoinedOn), ct);
                return Results.Created($"/activity-groups/{groupId}/members/{req.StudentId}", new { id });
            }
            catch (ActivityGroupNotFoundException) { return Results.NotFound(); }
            catch (GroupAtCapacityException ex) { return Results.Conflict(new { ex.Message }); }
            catch (DuplicateActiveMembershipException ex) { return Results.Conflict(new { ex.Message }); }
            catch (ArchivedGroupException ex) { return Results.Json(new { ex.Message }, statusCode: 422); }
            catch (StudentNotFoundException ex) { return Results.Json(new { ex.Message }, statusCode: 422); }
        });

        group.MapDelete("/activity-groups/{groupId:guid}/members/{studentId:guid}", async (
            Guid groupId, Guid studentId,
            [FromServices] CoreCQRS.ICommandHandler<RemoveMembership> h, CancellationToken ct) =>
        { try { await h.HandleAsync(new RemoveMembership(groupId, studentId), ct); return Results.NoContent(); }
          catch (MembershipNotFoundException) { return Results.NotFound(); } });

        group.MapPost("/activity-groups/{groupId:guid}/members/{studentId:guid}/exit", async (
            Guid groupId, Guid studentId,
            [FromServices] CoreCQRS.ICommandHandler<ExitMembership> h, CancellationToken ct) =>
        { try { await h.HandleAsync(new ExitMembership(groupId, studentId), ct); return Results.NoContent(); }
          catch (MembershipNotFoundException) { return Results.NotFound(); } });

        // ── Student → activity groups (spec §7.2) ─────────────────────────────
        group.MapGet("/students/{studentId:guid}/activity-groups", async (
            Guid studentId, [FromServices] CoreCQRS.IQueryHandler<GetStudentGroups, ActivityGroupDto[]> h, CancellationToken ct) =>
            Results.Ok(await h.HandleAsync(new GetStudentGroups(studentId), ct)));
    }
}

internal record CreateActivityGroupRequest(
    string Name, string? Description = null, string? Category = null,
    Guid? PeriodId = null, int? Capacity = null);

internal record UpdateActivityGroupRequest(
    string Name, string? Description = null, string? Category = null,
    Guid? PeriodId = null, int? Capacity = null);

internal record AddMemberRequest(Guid StudentId, DateOnly? JoinedOn = null);

