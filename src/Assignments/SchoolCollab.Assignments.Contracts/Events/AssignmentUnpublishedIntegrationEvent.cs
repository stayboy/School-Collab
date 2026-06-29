namespace SchoolCollab.Assignments.Contracts.Events;

/// <summary>
/// Integration event raised when a previously published assignment
/// is unpublished (returned to draft). Published to the
/// <c>assignments</c> exchange after the underlying domain
/// transaction commits.
/// </summary>
/// <param name="AssignmentId">The unpublished assignment's id.</param>
/// <param name="Title">The assignment title.</param>
/// <param name="UpdatedAt">The server timestamp of the unpublish.</param>
public sealed record AssignmentUnpublishedIntegrationEvent(
    Guid AssignmentId,
    string Title,
    DateTimeOffset UpdatedAt);