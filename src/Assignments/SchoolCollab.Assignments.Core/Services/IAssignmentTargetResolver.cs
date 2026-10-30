using SchoolCollab.Assignments.Core.Domain;

namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// One authored targeting constraint as the resolver consumes it — the
/// <see cref="AssignmentTarget"/> row's kind + reference, without the row identity
/// (documents/specs/assignment-authoring-compartments.md §7.1 TGT-1).
/// </summary>
public sealed record TargetConstraint(TargetKind Kind, Guid? RefId);

/// <summary>
/// One resolved target student with the display name the audience reads render.
/// <see cref="FullName"/> is <c>"{LastName}, {FirstName}"</c> — comma, surname first: the format the
/// audience panel already renders and the one its name sort assumes.
/// </summary>
public sealed record TargetStudent(Guid Id, string FullName);

/// <summary>
/// Cross-bounded-context port (Assignments → Students) that resolves the student-id union of
/// an assignment's authored targeting constraints (TGT-3…TGT-9). The interface lives in
/// <c>Assignments.Core</c>; the HTTP client implementation lives in <c>Assignments.Api</c>
/// (the <c>StudentsContactResolver</c> / <c>ActivityGroupLookupHttpClient</c> precedent).
///
/// <para><b>Failure posture (TGT-10 / D-6).</b> Fail-<b>closed</b>: HTTP / transport failures
/// <b>propagate</b> to the caller and block publish — they are never swallowed into an empty
/// set, and never degraded into "everyone". This is the deliberate mirror of the fail-open
/// policy resolvers (<c>documents/specs/assignment-policy.md</c> §2).</para>
/// </summary>
public interface IAssignmentTargetResolver
{
    /// <summary>
    /// Returns the deduped union of student ids matching <paramref name="targets"/>.
    /// <paramref name="includeAllStudents"/> is <see langword="true"/> exactly when the target
    /// set contains an <see cref="TargetKind.AllStudents"/> row, and opens the tenant-wide
    /// all-students leg (D-6). An empty constraint list with <paramref name="includeAllStudents"/>
    /// <see langword="false"/> short-circuits to an empty array without a round-trip.
    /// </summary>
    Task<Guid[]> ResolveStudentIdsAsync(
        IReadOnlyList<TargetConstraint> targets,
        bool includeAllStudents,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the deduped union of student ids matching <paramref name="targets"/> together with
    /// each student's display name — one round-trip for both, so a student with no subscribed
    /// contact still carries a name. Same short-circuit and failure posture as
    /// <see cref="ResolveStudentIdsAsync"/>; the names ride the <c>withNames=true</c> shape of the
    /// same Students route.
    /// </summary>
    Task<TargetStudent[]> ResolveStudentsAsync(
        IReadOnlyList<TargetConstraint> targets,
        bool includeAllStudents,
        CancellationToken cancellationToken = default);
}
