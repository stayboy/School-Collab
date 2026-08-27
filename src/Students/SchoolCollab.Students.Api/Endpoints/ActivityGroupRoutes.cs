using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.ActivateActivityGroup;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.AddMembership;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.CreateActivityGroup;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.DeactivateActivityGroup;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.DeleteActivityGroup;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.ExitMembership;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.RemoveMembership;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.RolloverActivityGroup;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.SetActivityGroupNextWindow;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.SetMembershipAutoRenew;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.UpdateActivityGroup;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Queries.GetActivityGroupById;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Queries.GetGroupMembers;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Queries.GetStudentGroups;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Queries.ListActivityGroups;
using SchoolCollab.Students.Core.Domain;
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
            try
            {
                var id = await h.HandleAsync(new CreateActivityGroup(req.Name, req.Description,
                    req.Category, req.Capacity, req.Span, req.EnrollmentStartDate, req.EnrollmentEndDate,
                    req.AutoRenewDefault, req.EligibleGradeIds), ct);
                return Results.Created($"/activity-groups/{id}", new { id });
            }
            catch (EnrollmentSpanIncompatibleException ex)
            {
                return Results.Json(new { ex.Message }, statusCode: 422);
            }
        });

        group.MapPut("/activity-groups/{id:guid}", async (
            Guid id, [FromBody] UpdateActivityGroupRequest req,
            [FromServices] CoreCQRS.ICommandHandler<UpdateActivityGroup> h, CancellationToken ct) =>
        {
            try { await h.HandleAsync(new UpdateActivityGroup(id, req.Name, req.Description,
                req.Category, req.Capacity, req.EnrollmentStartDate, req.EnrollmentEndDate,
                req.AutoRenewDefault, req.EligibleGradeIds), ct); return Results.NoContent(); }
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

        group.MapPost("/activity-groups/{id:guid}/activate", async (
            Guid id, [FromServices] CoreCQRS.ICommandHandler<ActivateActivityGroup> h, CancellationToken ct) =>
        { try { await h.HandleAsync(new ActivateActivityGroup(id), ct); return Results.NoContent(); }
          catch (ActivityGroupNotFoundException) { return Results.NotFound(); } });

        group.MapPost("/activity-groups/{id:guid}/deactivate", async (
            Guid id, [FromServices] CoreCQRS.ICommandHandler<DeactivateActivityGroup> h, CancellationToken ct) =>
        { try { await h.HandleAsync(new DeactivateActivityGroup(id), ct); return Results.NoContent(); }
          catch (ActivityGroupNotFoundException) { return Results.NotFound(); } });

        // Rev. 5 FR-51/53: admin sets the next DateRange window in advance.
        group.MapPut("/activity-groups/{id:guid}/next-window", async (
            Guid id, [FromBody] SetNextWindowRequest req,
            [FromServices] CoreCQRS.ICommandHandler<SetActivityGroupNextWindow> h, CancellationToken ct) =>
        {
            try { await h.HandleAsync(new SetActivityGroupNextWindow(id, req.NextStartDate, req.NextEndDate), ct); return Results.NoContent(); }
            catch (ActivityGroupNotFoundException) { return Results.NotFound(); }
            catch (ArgumentException ex) { return Results.BadRequest(new { ex.Message }); }
        });

        // Rev. 5 FR-54: admin-forced rollover (scheduled job uses the same handler).
        group.MapPost("/activity-groups/{id:guid}/rollover", async (
            Guid id, [FromServices] CoreCQRS.ICommandHandler<RolloverActivityGroup> h, CancellationToken ct) =>
        {
            try { await h.HandleAsync(new RolloverActivityGroup(id), ct); return Results.NoContent(); }
            catch (ActivityGroupNotFoundException) { return Results.NotFound(); }
        });

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
                var id = await h.HandleAsync(new AddMembership(groupId, req.StudentId, req.PeriodId,
                    req.AutoRenew, req.WindowStartDate, req.WindowEndDate, req.JoinedOn), ct);
                return Results.Created($"/activity-groups/{groupId}/members/{req.StudentId}", new { id });
            }
            catch (ActivityGroupNotFoundException) { return Results.NotFound(); }
            catch (GroupAtCapacityException ex) { return Results.Conflict(new { ex.Message }); }
            catch (DuplicateActiveMembershipException ex) { return Results.Conflict(new { ex.Message }); }
            catch (InactiveGroupException ex) { return Results.Json(new { ex.Message }, statusCode: 422); }
            catch (GradeNotEligibleException ex) { return Results.Json(new { ex.Message }, statusCode: 422); }
            catch (EnrollmentWindowClosedException ex) { return Results.Json(new { ex.Message }, statusCode: 422); }
            catch (EnrollmentSpanMismatchException ex) { return Results.Json(new { ex.Message }, statusCode: 422); }
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

        // Rev. 5 FR-49: admin toggles a member's AutoRenew consent.
        group.MapPut("/activity-groups/members/{membershipId:guid}/auto-renew", async (
            Guid membershipId, [FromBody] SetAutoRenewRequest req,
            [FromServices] CoreCQRS.ICommandHandler<SetMembershipAutoRenew> h, CancellationToken ct) =>
        {
            try { await h.HandleAsync(new SetMembershipAutoRenew(membershipId, req.AutoRenew), ct); return Results.NoContent(); }
            catch (MembershipNotFoundException) { return Results.NotFound(); }
        });

        // ── Student → activity groups (spec §7.2) ─────────────────────────────
        group.MapGet("/students/{studentId:guid}/activity-groups", async (
            Guid studentId, [FromServices] CoreCQRS.IQueryHandler<GetStudentGroups, ActivityGroupDto[]> h, CancellationToken ct) =>
            Results.Ok(await h.HandleAsync(new GetStudentGroups(studentId), ct)));
    }
}

internal record CreateActivityGroupRequest(
    string Name, string? Description = null, string? Category = null,
    int? Capacity = null, EnrollmentSpan Span = EnrollmentSpan.OpenEnded,
    DateOnly? EnrollmentStartDate = null, DateOnly? EnrollmentEndDate = null,
    bool AutoRenewDefault = true, Guid[]? EligibleGradeIds = null);

internal record UpdateActivityGroupRequest(
    string Name, string? Description = null, string? Category = null,
    int? Capacity = null, DateOnly? EnrollmentStartDate = null, DateOnly? EnrollmentEndDate = null,
    bool? AutoRenewDefault = null, Guid[]? EligibleGradeIds = null);

internal record AddMemberRequest(
    Guid StudentId, Guid? PeriodId = null, bool? AutoRenew = null,
    DateOnly? WindowStartDate = null, DateOnly? WindowEndDate = null, DateOnly? JoinedOn = null);

internal record SetNextWindowRequest(DateOnly NextStartDate, DateOnly NextEndDate);

internal record SetAutoRenewRequest(bool AutoRenew);
