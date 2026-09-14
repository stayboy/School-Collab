namespace SchoolCollab.Assignments.Core.Domain.Exceptions;

/// <summary>
/// WS-C1/C4 — a resubmission attempt on a submission locked by a guardian
/// signature (<c>SignOffState</c> <c>Signed</c>, or a stamped
/// <c>FinalizedAt</c>). Spec §3.2 line 51 — Signed/Finalized is terminal for
/// content. Routes map this to <c>409 Conflict</c> (the
/// <c>SubmissionAttemptsExhaustedException</c> 409 posture).
/// </summary>
public sealed class SubmissionLockedException : Exception
{
    public SubmissionLockedException(Guid studentId)
        : base($"Submission for student '{studentId}' is locked by a guardian signature.") { }
}
