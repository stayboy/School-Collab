using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetGuardianGate;

/// <summary>
/// Guardian portal view of a submission gate for a (assignment, student) pair
/// (spec §4.10). Returns null when no gate exists yet.
/// </summary>
public sealed record GetGuardianGate(Guid AssignmentId, Guid StudentId) : IQuery<GuardianGateDto?>;
