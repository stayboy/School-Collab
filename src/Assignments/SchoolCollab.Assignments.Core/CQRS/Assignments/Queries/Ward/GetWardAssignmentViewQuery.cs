using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.Ward;

/// <summary>
/// WS-D1/WS-A5 — the ward-facing view of a single published assignment:
/// its modules in display order, each merged with the ward's progress
/// (<see cref="ModuleProgress"/>), plus the questions-unlocked gate flag
/// (all required modules have a <c>CompletedAt</c>). Returns null when the
/// assignment does not exist (route 404).
/// </summary>
public sealed record GetWardAssignmentView(Guid AssignmentId, Guid StudentId) : IQuery<WardAssignmentViewDto?>;
