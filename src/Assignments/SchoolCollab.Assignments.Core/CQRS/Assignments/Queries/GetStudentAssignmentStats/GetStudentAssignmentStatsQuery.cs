using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetStudentAssignmentStats;

/// <summary>
/// Issue #444 (layer L2a) — the six counts a student's assignment record implies. Deliberately carries
/// only the student: the counts join on the student, so there is no period (or placement-window)
/// parameter that a caller could narrow into an empty result set (Q139).
/// </summary>
public sealed record GetStudentAssignmentStats(Guid StudentId) : IQuery<StudentAssignmentStatsDto>;
