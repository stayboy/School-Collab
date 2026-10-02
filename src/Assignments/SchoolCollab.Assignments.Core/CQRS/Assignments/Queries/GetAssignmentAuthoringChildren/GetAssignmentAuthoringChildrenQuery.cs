using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetAssignmentAuthoringChildren;

/// <summary>
/// Reads the persisted child collections the assignment Edit surface needs
/// (assignment-authoring-compartments P1 rework — the load half of Edit parity).
/// Null when no such assignment exists in the caller's tenant.
/// </summary>
public sealed record GetAssignmentAuthoringChildrenQuery(Guid AssignmentId)
    : IQuery<AssignmentAuthoringChildrenDto?>;
