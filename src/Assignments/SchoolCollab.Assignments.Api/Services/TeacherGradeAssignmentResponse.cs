namespace SchoolCollab.Assignments.Api.Services;

/// <summary>
/// The wire shape of one row from the Students route
/// <c>GET /teachers/{teacherId}/grade-assignments</c> (Students' <c>TeacherGradeAssignmentDto</c>).
///
/// <para>Deliberately a LOCAL mirror: an Assignments.Core/Assignments.Api signature must never
/// carry a <c>Students.Core</c>/<c>Students.Application</c> type (the open
/// <c>Assignments.Core → Students.Core</c> violation in <c>cross-context-rule-followups.md</c> §1
/// must not be deepened). Only the three fields the scope needs are mirrored; the display fields
/// the Students row also carries (RowId, GradeName, SubjectName, …) are ignored on read.</para>
///
/// <para><c>SubjectId</c> is the Students name for the scope's
/// <c>TeacherSubjectGrade.TopicId</c>; <c>null</c> is a grade-wide row.</para>
/// </summary>
/// <param name="GradeLevelId">The taught grade.</param>
/// <param name="SubjectId">The taught subject; <c>null</c> = grade-wide.</param>
/// <param name="RoleCodedValueId">The optional teaching-role coded value.</param>
internal sealed record TeacherGradeAssignmentResponse(
    Guid GradeLevelId,
    Guid? SubjectId,
    Guid? RoleCodedValueId);
