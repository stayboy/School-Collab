using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.LinkAssignmentGroups;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.ListAssignmentGroups;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.ListAssignmentsForGroup;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.DTOs;
using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Api.Endpoints;

/// <summary>
/// Assignment ↔ activity-group link endpoints (spec §7.3). Mounted on a
/// flag-gated root group so <c>/activity-groups/{groupId}/assignments</c> is at
/// the same level as the Students API's group routes (FR-6 delete-guard contract).
/// </summary>
public static class ActivityGroupLinkRoutes
{
    public static RouteGroupBuilder MapActivityGroupLinkRoutes(this RouteGroupBuilder group)
    {
        // PUT /assignments/{assignmentId}/groups (replace set; FR-17, §7.3)
        group.MapPut("/assignments/{assignmentId:guid}/groups", async (
            Guid assignmentId,
            [FromBody] AssignmentGroupLinkRequest req,
            [FromServices] ICommandHandler<LinkAssignmentGroups> handler,
            CancellationToken ct) =>
        {
            try
            {
                await handler.HandleAsync(new LinkAssignmentGroups(assignmentId, req.ActivityGroupIds), ct);
                return Results.NoContent();
            }
            catch (AssignmentNotFoundException) { return Results.NotFound(); }
            catch (ArgumentException ex) { return Results.Json(new { ex.Message }, statusCode: 422); }
        });

        // GET /assignments/{assignmentId}/groups (spec §7.3)
        group.MapGet("/assignments/{assignmentId:guid}/groups", async (
            Guid assignmentId,
            [FromServices] IQueryHandler<ListAssignmentGroups, ActivityGroupRefDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListAssignmentGroups(assignmentId), ct)));

        // GET /activity-groups/{groupId}/assignments (spec §7.3) — consumed by the
        // Students-context FR-6 delete guard.
        group.MapGet("/activity-groups/{groupId:guid}/assignments", async (
            Guid groupId,
            [FromServices] IQueryHandler<ListAssignmentsForGroup, AssignmentGroupSummaryDto[]> handler,
            CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListAssignmentsForGroup(groupId), ct)));

        return group;
    }
}

internal record AssignmentGroupLinkRequest(IReadOnlyList<Guid> ActivityGroupIds);
