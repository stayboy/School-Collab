using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.StudentTopicAssignments.Queries.ListStudentSubjects;

/// <summary>
/// The §6 "my subjects" union for one student: every subject on the student's grade, with
/// <see cref="StudentSubjectEnrollmentDto.IsEnrolled"/> telling the caller whether the student
/// holds an enrolment row for it (a <c>false</c> subject is enrolled by implication, FR-ER-1).
/// The grade is derived server-side from the student's active enrolment —
/// there is no caller-supplied grade, because a mismatched grade would silently produce an
/// empty or wrong union (subject-enrollment-requirement-flag.md FR-ER-16, round 13 P3).
/// </summary>
public sealed record ListStudentSubjects(Guid StudentId) : IQuery<StudentSubjectEnrollmentDto[]>;
