namespace SchoolCollab.Assignments.Contracts.Events;

/// <summary>
/// Integration event raised when an assignment is closed (no
/// longer accepts submissions). Published to the
/// <c>assignments</c> exchange after the underlying domain
/// transaction commits.
/// </summary>
/// <param name="AssignmentId">The closed assignment's id.</param>
/// <param name="Title">The assignment title.</param>
/// <param name="UpdatedAt">The server timestamp of the close.</param>
public sealed record AssignmentClosedIntegrationEvent(
    Guid AssignmentId,
    string Title,
    DateTimeOffset UpdatedAt);