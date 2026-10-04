namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// Teacher-scope port (Assignments → Students, round <c>teacher-scope-auth</c> D2/Q7): answers
/// "which grades/subjects does this teacher teach?", which the assignment reads need to decide
/// visibility. The interface lives in Assignments.Core; the HTTP implementation
/// (<c>TeacherScopeHttpClient</c>, registered in Assignments.Api) calls the existing
/// <c>students-api</c> route <c>GET /teachers/{teacherId}/grade-assignments</c> and maps the
/// response onto the local <see cref="TeacherSubjectGrade"/> mirror — mirroring
/// <see cref="ITeacherDirectory"/>. No cross-context project reference.
///
/// <para>Live-consistency, not replication, is deliberate: this data is <b>authorization
/// input</b>, so a replicated copy would keep a teacher whose teaching link was revoked able to
/// see those assignments until replication caught up — a fail-open window on an access decision.
/// Cross-context events remain the mechanism for state changes, not for a read that gates
/// access.</para>
///
/// <para>Failure posture: fail-<b>closed</b> — a transport failure, a non-2xx response, a
/// malformed body or an unknown/empty teacher id yields <see cref="TeacherScope.Empty"/> (or, on
/// a resolved 2xx, a scope with no taught rows); never <see cref="TeacherScope.Unrestricted"/>.</para>
/// </summary>
public interface ITeacherScopeProvider
{
    /// <summary>The scope for <paramref name="teacherId"/>. Never throws on a transport or
    /// payload failure — an unresolvable taught set comes back as an empty scope.</summary>
    Task<TeacherScope> GetScopeAsync(Guid teacherId, CancellationToken cancellationToken = default);
}
