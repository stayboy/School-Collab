namespace SchoolCollab.Assignments.Contracts.Events;

/// <summary>
/// Integration event raised when an assignment is published (made
/// visible to students). Published to the <c>assignments</c>
/// exchange after the underlying domain transaction commits.
/// </summary>
/// <param name="AssignmentId">The published assignment's id.</param>
/// <param name="Title">The assignment title.</param>
/// <param name="UpdatedAt">The server timestamp of the publish.</param>
public sealed record AssignmentPublishedIntegrationEvent(
    Guid AssignmentId,
    string Title,
    DateTimeOffset UpdatedAt);