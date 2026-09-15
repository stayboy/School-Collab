namespace SchoolCollab.Assignments.Core.Domain.Exceptions;

/// <summary>
/// WS-B2 (spec §3.4 line 73) — a questions-draft operation is invalid: the
/// staged blob is missing, fails to parse, or the assignment is not in
/// <see cref="Domain.AssignmentStatus.Draft"/> (the draft surface's state
/// guard). Mapped to 409 at the draft-conformant routes.
/// </summary>
public sealed class InvalidQuestionsDraftException : Exception
{
    public InvalidQuestionsDraftException(string message) : base(message) { }
}
