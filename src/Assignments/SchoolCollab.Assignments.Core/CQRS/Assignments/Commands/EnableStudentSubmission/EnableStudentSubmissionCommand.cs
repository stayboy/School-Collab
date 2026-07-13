using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.EnableStudentSubmission;

/// <summary>Teacher/admin enables a student to self-submit directly, bypassing the
/// guardian review (spec §9 <c>enable-submission</c>; the gate is optional when
/// <c>Assignment.MandatoryReview == false</c>).</summary>
public sealed record EnableStudentSubmissionCommand(Guid GateId, Guid? ReviewerGuardianId) : ICommand;