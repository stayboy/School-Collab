using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using SchoolCollab.Students.Core.DTOs;
using SchoolCollab.Students.Core.Services;

namespace SchoolCollab.Students.Api.Endpoints;

/// <summary>
/// Issue #444 (layer L2b) — the student assignment-stats read: the six counts the student view's rail
/// panel renders. Mapped with a <b>relative</b> path on whichever group the orchestrator hands it, so it
/// inherits that group's prefix (<c>/students</c>) and its <c>RequireAuthIfOidc</c> policy; the route
/// carries no authorization of its own and no feature-flag branch.
/// </summary>
public static class StudentStatsRoutes
{
    public static RouteGroupBuilder MapStudentStatsRoutes(this RouteGroupBuilder group)
    {
        // GET /students/{id}/assignment-stats — a typed result, so the generated document carries a real
        // response schema. A hop failure becomes a logged problem (502) rather than a bare 500 or a 200
        // carrying zeros: zeros are a claim, unavailable is a fact, and the failure must stay visible to
        // telemetry (round doc, Open decision A).
        group.MapGet("/{id:guid}/assignment-stats",
            async Task<Results<Ok<StudentAssignmentStats>, ProblemHttpResult>> (
                Guid id,
                [FromServices] IStudentAssignmentStatsReader reader,
                [FromServices] ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                try
                {
                    return TypedResults.Ok(await reader.GetAsync(id, ct));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    loggerFactory
                        .CreateLogger(typeof(StudentStatsRoutes).FullName!)
                        .LogError(ex,
                            "Assignment stats hop to the Assignments API failed for student {StudentId}; "
                            + "the view renders the panel as unavailable",
                            id);

                    return TypedResults.Problem(
                        title: "Assignment stats are unavailable",
                        detail: "The Assignments API could not be reached for this student's assignment counts.",
                        statusCode: StatusCodes.Status502BadGateway);
                }
            });

        return group;
    }
}
