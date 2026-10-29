using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Services;

/// <summary>
/// Cross-context port (issue #444, layer L2b) used by the student view to show a student's assignment
/// counts. The HTTP implementation lives in <c>SchoolCollab.Students.Api</c> and reads
/// <c>GET /assignments/{studentId}/assignment-stats</c> on the Assignments API through Aspire service
/// discovery.
/// <para>
/// It <b>throws</b> when the Assignments API cannot be reached or answers non-success, matching
/// <see cref="IActivityGroupAssignmentQuery"/>: the decision about what a failed hop means belongs to the
/// caller. Here the caller degrades — the Students endpoint turns the failure into a logged problem, and
/// the view renders an "unavailable" panel — so the reason stays visible in telemetry instead of being
/// flattened into a zero count.
/// </para>
/// </summary>
public interface IStudentAssignmentStatsReader
{
    /// <summary>
    /// Returns the six counts for the given student. Throws when the Assignments API is unreachable or
    /// answers non-success.
    /// </summary>
    Task<StudentAssignmentStats> GetAsync(Guid studentId, CancellationToken cancellationToken = default);
}
