namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// One grade-scoped teaching row — a local mirror of the Students
/// <c>TeacherGradeAssignmentDto</c> shape (grade + optional subject + optional
/// teaching-role coded value). It lives in Assignments.Core rather than referencing a
/// <c>Students.Core</c>/<c>Students.Application</c> type, because the Assignments → Students
/// edge is an HTTP port (see <see cref="ITeacherScopeProvider"/>); the open
/// <c>Assignments.Core → Students.Core</c> violation recorded in
/// <c>cross-context-rule-followups.md</c> §1 must not be deepened.
/// </summary>
/// <param name="GradeLevelId">The grade the teacher teaches.</param>
/// <param name="TopicId">The subject taught in that grade; <c>null</c> means a
/// <b>grade-wide</b> row (every subject of the grade) — <c>TeacherGradeLevel.TopicId</c> is
/// optional, so an exact pair match would silently drop every grade-only teacher ([P1-5]).</param>
/// <param name="RoleCodedValueId">The optional teaching-role coded value. Metadata on the
/// teaching row, not an assignment dimension — the visibility rule needs no leg for it.</param>
public sealed record TeacherSubjectGrade(Guid GradeLevelId, Guid? TopicId, Guid? RoleCodedValueId);

/// <summary>
/// The caller's assignment-visibility scope (round <c>teacher-scope-auth</c> D2/D3), resolved at
/// the endpoint by <see cref="ITeacherScopeProvider"/> and threaded onto the read queries —
/// never fetched inside a Core handler.
///
/// <para>Visibility rule (D3): a row is visible when the caller created it
/// (<c>CreatedByTeacherId</c>) <b>or</b> its grade/subject is in <see cref="Taught"/>.</para>
///
/// <para>The three legal postures ([P1-4]): <see cref="Unrestricted"/> for staff/admins and for
/// a principal carrying no recognised role (which is what keeps CI, the Integration suite and
/// the dev TestAuth surface working — it cannot fail open in real auth, because the reader policy
/// gates every teacher-scoped read before the filter is reached), a scoped value built by
/// <see cref="ForTeacher"/> for a teacher whose claim resolved, and <see cref="Empty"/> for a
/// teacher-only principal with no usable <c>teacher_id</c> claim (or an unresolved taught set) —
/// never tenant-wide.</para>
/// </summary>
public sealed class TeacherScope
{
    /// <summary>staff / admin / no recognised role: the tenant-wide read.</summary>
    public static TeacherScope Unrestricted { get; } = new(isUnrestricted: true, teacherId: null, taught: []);

    /// <summary>A teacher-only principal whose scope could not be established: grants nothing,
    /// and is never tenant-wide. Fail-closed.</summary>
    public static TeacherScope Empty { get; } = new(isUnrestricted: false, teacherId: null, taught: []);

    private TeacherScope(bool isUnrestricted, Guid? teacherId, IReadOnlyList<TeacherSubjectGrade> taught)
    {
        IsUnrestricted = isUnrestricted;
        TeacherId = teacherId;
        Taught = taught;
    }

    /// <summary>When true the scope restricts nothing — the tenant-wide read.</summary>
    public bool IsUnrestricted { get; }

    /// <summary>The calling teacher's id, taken from the authenticated principal (never from a
    /// request body or query string). <c>null</c> when unrestricted or unresolved.</summary>
    public Guid? TeacherId { get; }

    /// <summary>The resolved grade/subject teaching rows; empty when the caller teaches nothing
    /// or the taught set is unresolved.</summary>
    public IReadOnlyList<TeacherSubjectGrade> Taught { get; }

    /// <summary>Whether this scope grants nothing at all — the caller's own creations included.</summary>
    public bool IsEmpty => !IsUnrestricted && TeacherId is null && Taught.Count == 0;

    /// <summary>
    /// The resolved scope for a teacher: their own creations plus every taught grade/subject.
    /// </summary>
    public static TeacherScope ForTeacher(Guid teacherId, IReadOnlyList<TeacherSubjectGrade> taught)
    {
        ArgumentNullException.ThrowIfNull(taught);
        return new TeacherScope(isUnrestricted: false, teacherId, taught);
    }

    /// <summary>
    /// The D3 visibility rule for one assignment row: the caller created it, or its grade is
    /// taught by the caller — with a <c>null</c> teaching <see cref="TeacherSubjectGrade.TopicId"/>
    /// matching the whole grade ([P1-5]) and a non-null one matching that subject only.
    /// </summary>
    /// <param name="createdByTeacherId">The assignment's creator.</param>
    /// <param name="gradeLevelId">The assignment's grade (a null grade matches no teaching row).</param>
    /// <param name="topicId">The assignment's subject.</param>
    public bool Allows(Guid createdByTeacherId, Guid? gradeLevelId, Guid? topicId)
    {
        if (IsUnrestricted)
        {
            return true;
        }

        if (TeacherId is { } teacherId && createdByTeacherId == teacherId)
        {
            return true;
        }

        if (gradeLevelId is null)
        {
            return false;
        }

        foreach (var row in Taught)
        {
            if (row.GradeLevelId == gradeLevelId && (row.TopicId is null || row.TopicId == topicId))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The D3 visibility rule for one assignment row whose grade scope is its authored
    /// <b>targets</b> (round <c>drop-primary-grade</c>): the caller created it, or <b>any</b> of its
    /// grade-target ids is taught by the caller — subject-compatible through
    /// <see cref="Allows"/> exactly as a single grade is (a null teaching subject matches the whole
    /// grade).
    /// <para>A row with no grade target stays visible via the creator leg only (the null-grade
    /// posture <see cref="Allows"/> already applies); a row whose grade-target key-set is not
    /// known (<paramref name="targetGradeIds"/> null — a cached payload from before the deploy) is
    /// fail-closed: invisible unless the caller created it. Never widened to every grade, never
    /// narrowed to one derived grade.</para>
    /// <para>Declared here, once, so the by-id read, the list filter and the review queue cannot
    /// diverge.</para>
    /// </summary>
    /// <param name="createdByTeacherId">The assignment's creator.</param>
    /// <param name="targetGradeIds">The assignment's grade-target ids; null when that key-set is
    /// unknown (fail-closed).</param>
    /// <param name="topicId">The assignment's subject.</param>
    public bool AllowsAnyTargetGrade(
        Guid createdByTeacherId, IReadOnlyList<Guid>? targetGradeIds, Guid? topicId)
    {
        if (Allows(createdByTeacherId, null, topicId))
        {
            // The caller's own creation (and the unrestricted posture) — the grade leg plays no
            // part.
            return true;
        }

        return targetGradeIds is not null
            && targetGradeIds.Any(gradeId => Allows(createdByTeacherId, gradeId, topicId));
    }
}
