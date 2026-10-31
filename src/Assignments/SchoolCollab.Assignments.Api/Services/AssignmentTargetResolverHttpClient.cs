using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Services;

namespace SchoolCollab.Assignments.Api.Services;

/// <summary>
/// HTTP-backed <see cref="IAssignmentTargetResolver"/>
/// (documents/specs/assignment-authoring-compartments.md §7.2 TGT-8): calls the Students API's
/// <c>GET /students/by-target</c> through the existing named client <c>students-api</c> (Aspire
/// service discovery, the same registration <c>StudentsContactResolver</c> uses — bearer
/// forwarding only, no tenant-forwarding handler; the Students side is tenant-scoped by its own
/// request context, D-7).
///
/// <para><b>Fail-closed (TGT-10).</b> There is deliberately NO try/catch around the call: a
/// transport failure propagates to the publish handler and blocks the sendout. An empty
/// constraint list with no all-students row short-circuits to an empty array without a
/// round-trip.</para>
/// </summary>
public sealed class AssignmentTargetResolverHttpClient(
    IHttpClientFactory httpClientFactory,
    ILogger<AssignmentTargetResolverHttpClient> logger) : IAssignmentTargetResolver
{
    public async Task<Guid[]> ResolveStudentIdsAsync(
        IReadOnlyList<TargetConstraint> targets,
        bool includeAllStudents,
        CancellationToken cancellationToken = default)
    {
        if (targets.Count == 0 && !includeAllStudents)
        {
            return [];
        }

        var url = BuildUrl(targets, includeAllStudents);

        var client = httpClientFactory.CreateClient("students-api");
        logger.LogDebug("Resolving target students via {Url}", url);
        var studentIds = await client.GetFromJsonAsync<Guid[]>(url, cancellationToken) ?? [];
        logger.LogInformation(
            "Target resolution matched {Count} student(s) for {ConstraintCount} constraint(s)",
            studentIds.Length, targets.Count);

        return studentIds;
    }

    public async Task<TargetStudent[]> ResolveStudentsAsync(
        IReadOnlyList<TargetConstraint> targets,
        bool includeAllStudents,
        CancellationToken cancellationToken = default)
    {
        if (targets.Count == 0 && !includeAllStudents)
        {
            return [];
        }

        // The additive shape of the same route: identical query string, plus the names flag.
        var url = $"{BuildUrl(targets, includeAllStudents)}&withNames=true";

        var client = httpClientFactory.CreateClient("students-api");
        logger.LogDebug("Resolving target students with names via {Url}", url);
        var rows = await client.GetFromJsonAsync<TargetStudentRow[]>(url, cancellationToken) ?? [];
        logger.LogInformation(
            "Target resolution matched {Count} named student(s) for {ConstraintCount} constraint(s)",
            rows.Length, targets.Count);

        return rows
            .Select(row => new TargetStudent(row.Id, $"{row.LastName}, {row.FirstName}"))
            .ToArray();
    }

    private static string BuildUrl(IReadOnlyList<TargetConstraint> targets, bool includeAllStudents)
    {
        var query = new List<string> { $"allStudents={includeAllStudents.ToString().ToLowerInvariant()}" };
        foreach (var target in targets)
        {
            if (target.RefId is not Guid refId)
            {
                continue;
            }

            query.Add(target.Kind switch
            {
                TargetKind.GradeLevel => $"gradeLevelIds={refId}",
                TargetKind.Stream => $"streamCodedValueIds={refId}",
                TargetKind.Student => $"studentIds={refId}",
                TargetKind.ActivityGroup => $"activityGroupIds={refId}",
                _ => throw new InvalidOperationException(
                    $"An {target.Kind} target must not carry a reference id.")
            });
        }

        return $"students/by-target?{string.Join("&", query)}";
    }

    /// <summary>The <c>withNames=true</c> wire shape of <c>GET /students/by-target</c>.</summary>
    private sealed record TargetStudentRow(Guid Id, string FirstName, string LastName);
}
