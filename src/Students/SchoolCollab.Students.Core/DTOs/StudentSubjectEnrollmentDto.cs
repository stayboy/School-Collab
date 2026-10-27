namespace SchoolCollab.Students.Core.DTOs;

/// <summary>
/// One row of a student's subject list for their current grade
/// (subject-enrollment-requirement-flag.md §6 / FR-ER-16). It is the union member the
/// student-page "Subjects" section and the rollover screen read, so it carries the three
/// things <see cref="TopicAssignmentDto"/> cannot: the topic's <c>Name</c>/<c>Code</c>, the
/// bridge row's <c>RequiresEnrollment</c> flag, and the student's own
/// <see cref="IsEnrolled"/> state.
/// <para><see cref="AssignmentId"/> is the grade↔topic bridge row id — the same value
/// <see cref="GradeTopicCurriculumDto.AssignmentId"/> carries, so a row can be acted on
/// (enrol/disenrol/enrol-remaining) without a second read.</para>
/// <para><see cref="IsEnrolled"/> is implied <c>true</c> for a <c>false</c> subject (FR-ER-1:
/// audience membership is the enrolment), and row-backed for a <c>true</c> subject
/// (FR-ER-3: a <c>StudentTopicAssignment</c> for the active academic year). A <c>true</c>
/// subject with no row is returned with <c>false</c> — that row is what the student page's
/// "Needs explicit enrolment" band draws, struck through, so the gap is visible rather
/// than absent.</para>
/// </summary>
public sealed record StudentSubjectEnrollmentDto(
    Guid TopicId,
    string Name,
    string? Code,
    Guid AssignmentId,
    bool RequiresEnrollment,
    bool IsEnrolled);
