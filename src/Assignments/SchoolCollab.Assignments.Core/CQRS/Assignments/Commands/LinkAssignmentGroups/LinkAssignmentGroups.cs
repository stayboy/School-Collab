using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.LinkAssignmentGroups;

/// <summary>
/// Replaces the activity-group link set of an assignment (spec §7.3
/// <c>PUT /api/assignments/{assignmentId}/groups</c>). Replace-set semantics:
/// existing links are removed and the supplied set is written fresh (FR-17).
/// </summary>
public sealed record LinkAssignmentGroups(
    Guid AssignmentId,
    IReadOnlyList<Guid> ActivityGroupIds) : ICommand;
