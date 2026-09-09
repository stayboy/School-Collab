using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.OverrideStudentSubmissionAttempts;

/// <summary>
/// WS-A3 (spec §7 Q4) — a teacher cleared the
/// <c>Assignment.MaxAttempts</c> cap for one submission. The route
/// resolves (assignmentId, studentId) → submissionId and 404s before
/// dispatching (the enable-submission route resolution precedent);
/// the command id-precise shape mirrors <c>EnableStudentSubmissionCommand</c>
/// and <c>ReviewSubmissionCommand</c>. <see cref="TeacherId"/> is the
/// identity placeholder until identity wiring lands (D-6, the
/// <c>ReviewAssignmentRequest.TeacherId</c> posture).
/// </summary>
public sealed record OverrideStudentSubmissionAttemptsCommand(
    Guid SubmissionId,
    Guid TeacherId) : ICommand;
