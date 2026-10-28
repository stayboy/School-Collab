using SchoolCollab.Assignments.Core.DTOs;

namespace SchoolCollab.Assignments.Core.Data.Repositories;

/// <summary>
/// Issue #444 (layer L2a) — the student-stats candidate set: every assignment
/// <b>recipient-linked</b> to the student, <b>any status</b>, with its submission and guardian-gate
/// flags folded to per-row values.
/// <para>
/// Deliberately NOT <see cref="IWardAssignmentProjectionRepository.ListWardAssignmentsAsync"/>: that read
/// answers "what may this ward see?" and its WHERE clause already applies the published/scheduled-active
/// predicate, so reusing it would make the candidate set identical to the sent set and every status rule
/// in the fold would pass vacuously. The two reads answer two different questions by design; do not
/// collapse them.
/// </para>
/// </summary>
public interface IStudentAssignmentStatsRepository
{
    /// <summary>One row per recipient-linked assignment of <paramref name="studentId"/>, of any
    /// status. The sent test (Q126) is left to
    /// <c>GetStudentAssignmentStatsQueryHandler</c>'s fold, never to this query.</summary>
    Task<List<StudentAssignmentStatRow>> ListStudentAssignmentStatRowsAsync(Guid studentId, CancellationToken ct = default);
}
