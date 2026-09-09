namespace SchoolCollab.Assignments.Core.Domain.Exceptions;

/// <summary>
/// WS-A3 — inbound <see cref="Contracts.SubmissionAnswerDto"/> list failed
/// validation (unknown question id, selected option not on that
/// question, duplicate question ids). Routes map this to
/// <c>400 Bad Request</c> with <c>{ message }</c> per the group's catch
/// pattern. Thrown by <c>SubmissionAnswerValidator</c> before any
/// aggregate mutation so a partial submission state can never persist.
/// </summary>
public sealed class SubmissionAnswerValidationException : Exception
{
    public SubmissionAnswerValidationException(string message) : base(message) { }
}
