using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.SubmitAssignmentOnBehalf;

/// <summary>
/// A guardian submits an assignment on behalf of their ward (spec §4.7 / §4.11).
/// Requires an existing, enabled <see cref="SchoolCollab.Assignments.Core.Domain.GuardianSubmissionGate"/>.
/// </summary>
public sealed record SubmitAssignmentOnBehalfCommand(
    Guid AssignmentId,
    Guid StudentId,
    Guid GuardianId,
    string? Content) : ICommand;
