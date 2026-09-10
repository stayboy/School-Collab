namespace SchoolCollab.Assignments.Core.Domain.Exceptions;

/// <summary>
/// Typed rejection for inbound content-module / resource / attachment
/// payloads on the create + update command surface (WS-A1). Mirrors
/// <see cref="AssignmentQuestionValidationException"/> per the ar-1
/// lesson — typed exceptions only, never <c>InvalidOperationException</c>
/// from a new code path. Thrown by
/// <c>AssignmentContentValidator</c> BEFORE any child is added to the
/// aggregate (FR-252 / EC-7) so a partial state can never be persisted.
/// </summary>
public sealed class AssignmentContentValidationException : Exception
{
    public AssignmentContentValidationException(string message) : base(message) { }
}
