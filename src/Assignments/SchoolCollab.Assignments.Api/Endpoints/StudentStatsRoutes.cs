using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetStudentAssignmentStats;
using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Api.Endpoints;

/// <summary>
/// Issue #444 (layer L2a) — the student assignment-stats read: the six counts in one round trip. Mapped
/// with a <b>relative</b> path on whichever group the orchestrator hands it, so it inherits that group's
/// prefix (<c>/assignments</c>) and its <c>RequireAuthIfOidc</c> policy; the route carries no authorization
/// of its own and no feature-flag branch.
/// </summary>
public static class StudentStatsRoutes
{
    public static RouteGroupBuilder MapStudentStatsRoutes(this RouteGroupBuilder group)
    {
        // GET /assignments/{studentId}/assignment-stats — typed result, so the generated document carries
        // a real response schema (R3).
        group.MapGet("/{studentId:guid}/assignment-stats", async Task<Ok<StudentAssignmentStatsDto>> (
            Guid studentId,
            [FromServices] IQueryHandler<GetStudentAssignmentStats, StudentAssignmentStatsDto> handler,
            CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetStudentAssignmentStats(studentId), ct)));

        return group;
    }
}
