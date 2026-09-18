namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// Cross-bounded-context directory port (Assignments → Students, ar-20): answers
/// "does a teacher with this id exist?" so assignment write handlers can validate the
/// <c>teacher_id</c> claim against a real Students-boundary teacher row. The interface
/// lives in Assignments.Core; the HTTP implementation (<c>TeacherDirectoryHttpClient</c>)
/// calls the <c>students-api</c> named client's <c>GET /teachers/{id}</c> route and is
/// registered in Assignments.Api — mirroring <see cref="IStudentDirectory"/>. No direct
/// cross-context project references; teacher existence goes through the students-api port.
///
/// <para>Failure posture: fail-<b>closed</b> on the existence check — a network failure
/// or HTTP error returns <see langword="false"/> (unknown teacher), so a directory outage
/// blocks create-attribution rather than accepting an unverifiable <c>teacher_id</c>.</para>
/// </summary>
public interface ITeacherDirectory
{
    /// <summary>Whether a teacher with <paramref name="teacherId"/> exists in its tenant.
    /// Always <see langword="false"/> on 404 or network/HTTP failure (fail-closed).</summary>
    Task<bool> ExistsAsync(Guid teacherId, CancellationToken cancellationToken = default);
}
