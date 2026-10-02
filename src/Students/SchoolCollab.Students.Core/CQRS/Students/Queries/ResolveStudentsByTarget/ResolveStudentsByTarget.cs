using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Students.Queries.ResolveStudentsByTarget;

/// <summary>
/// Resolves the union of students matching an assignment's authored targeting constraints
/// (documents/specs/assignment-authoring-compartments.md §7.2 TGT-3…TGT-9; the single
/// Students-side leg of <c>IAssignmentTargetResolver</c>).
///
/// <para><b>Union semantics (TGT-3)</b> — a student matches if it satisfies <i>any</i> leg;
/// the returned ids are deduped. Every leg is tenant-scoped by the context's global query
/// filter.</para>
///
/// <para><b>Legs.</b> <see cref="AllStudents"/> opens the tenant-wide leg — all non-soft-deleted
/// students, with <b>no</b> grade, period or group predicate (D-6). <see cref="GradeLevelIds"/>
/// and <see cref="StreamCodedValueIds"/> resolve through active enrollments in the
/// <b>current</b> period, exactly as <c>ListStudentsByGradeHandler</c> derives it; the stream leg
/// carries no grade predicate (TGT-5). <see cref="StudentIds"/> selects individual
/// non-soft-deleted students (TGT-6). <see cref="ActivityGroupIds"/> selects active members of
/// active groups (TGT-7 / EC-4) with <b>no</b> period check — the pre-R2 publish semantics.</para>
/// </summary>
public sealed record ResolveStudentsByTarget(
    bool AllStudents,
    IReadOnlyList<Guid> GradeLevelIds,
    IReadOnlyList<Guid> StreamCodedValueIds,
    IReadOnlyList<Guid> StudentIds,
    IReadOnlyList<Guid> ActivityGroupIds) : IQuery<Guid[]>;
