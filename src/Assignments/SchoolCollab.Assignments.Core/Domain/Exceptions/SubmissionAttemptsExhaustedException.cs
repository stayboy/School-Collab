namespace SchoolCollab.Assignments.Core.Domain.Exceptions;

/// <summary>
/// WS-A3 / spec §7 Q4 — the student (or on-behalf guardian) hit the
/// assignment's <c>MaxAttempts</c> cap on submission. Routes map this
/// to <c>409 Conflict</c> via <c>Results.Problem</c>. A teacher
/// override (<c>OverrideStudentSubmissionAttempts</c>) clears the cap
/// for the affected submission; raising <c>MaxAttempts</c> on a draft
/// assignment helps via the standard edit path.
/// </summary>
public sealed class SubmissionAttemptsExhaustedException : Exception
{
    public SubmissionAttemptsExhaustedException(string message) : base(message) { }
}
