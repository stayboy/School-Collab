namespace SchoolCollab.Assignments.Core.Domain.Exceptions;

/// <summary>
/// WS-D1 / spec §3.3 — a ward attempted to submit an assignment that has
/// required content modules whose completion threshold has not been met.
/// Routes map this to <c>409 Conflict</c> via <c>Results.Problem</c> (the
/// <see cref="SubmissionAttemptsExhaustedException"/> posture). The gate is
/// additive: assignments with no required modules never throw it.
/// </summary>
public sealed class RequiredModuleIncompleteException : Exception
{
    public RequiredModuleIncompleteException(string message) : base(message) { }
}
