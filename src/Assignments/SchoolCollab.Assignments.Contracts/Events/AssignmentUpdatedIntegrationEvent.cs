namespace SchoolCollab.Assignments.Contracts.Events;

/// <summary>
/// Integration event raised when an existing assignment is updated.
/// Published to the <c>assignments</c> exchange after the
/// underlying domain transaction commits.
/// </summary>
/// <param name="AssignmentId">The updated assignment's id.</param>
/// <param name="Title">The assignment title.</param>
/// <param name="UpdatedAt">The server timestamp of the update.</param>
public sealed record AssignmentUpdatedIntegrationEvent(
    Guid AssignmentId,
    string Title,
    DateTimeOffset UpdatedAt);