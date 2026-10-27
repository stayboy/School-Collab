using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.StudentTopicAssignments.Queries.ListStudentSubjects;

/// <summary>
/// The §6 "my subjects" union for one student: every <c>false</c> subject on the student's
/// grade plus every <c>true</c> subject they hold an enrolment row for in the active
/// academic year. The grade is derived server-side from the student's active enrolment —
/// there is no caller-supplied grade, because a mismatched grade would silently produce an
/// empty or wrong union (subject-enrollment-requirement-flag.md FR-ER-16, round 13 P3).
/// </summary>
public sealed record ListStudentSubjects(Guid StudentId) : IQuery<StudentSubjectEnrollmentDto[]>;
