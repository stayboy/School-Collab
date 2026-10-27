using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.TopicAssignments.Commands.SetRequiresEnrollment;

/// <summary>
/// Switches a grade's subject between implied enrolment (<c>false</c>) and explicit
/// per-student enrolment (<c>true</c>) — subject-enrollment-requirement-flag.md FR-ER-7/9/10,
/// round 13 P6. <see cref="AssignmentId"/> is the grade↔topic bridge row.
/// </summary>
public sealed record SetRequiresEnrollment(Guid AssignmentId, bool Value) : ICommand;
