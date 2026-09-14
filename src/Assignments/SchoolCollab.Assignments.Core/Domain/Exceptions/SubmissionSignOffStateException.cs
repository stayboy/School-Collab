namespace SchoolCollab.Assignments.Core.Domain.Exceptions;

/// <summary>
/// WS-C1 — a guardian sign-off transition ran from the wrong stage (sign from
/// <c>None</c>, finalize from <c>AwaitingSignature</c>/<c>None</c>, reassign from
/// <c>Signed</c>/<c>None</c>, or sign on an assignment whose
/// <c>RequiresSignature</c> is not set). Routes map this to
/// <c>409 Conflict</c> (the <c>SubmissionAttemptsExhaustedException</c> posture).
/// </summary>
public sealed class SubmissionSignOffStateException : Exception
{
    public SubmissionSignOffStateException(string message) : base(message) { }
}
