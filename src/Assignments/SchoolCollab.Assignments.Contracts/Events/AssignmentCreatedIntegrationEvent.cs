namespace SchoolCollab.Assignments.Contracts.Events;

/// <summary>
/// Integration event raised when a new assignment is created.
/// Published to the <c>assignments</c> exchange after the
/// underlying domain transaction commits.
/// </summary>
/// <param name="AssignmentId">The new assignment's id.</param>
/// <param name="Title">The assignment title.</param>
/// <param name="AssignmentNumber">The auto-generated assignment code (e.g. ASGA01) — spec §5.4.</param>
/// <param name="CreatedAt">The server timestamp of the create.</param>
public sealed record AssignmentCreatedIntegrationEvent(
    Guid AssignmentId,
    string Title,
    string? AssignmentNumber,
    DateTimeOffset CreatedAt);