using SchoolCollab.Assignments.Core.DTOs;
using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.ListAssignmentGroups;

/// <summary>
/// Returns the activity groups linked to an assignment (spec §7.3
/// <c>GET /api/assignments/{assignmentId}/groups</c>).
/// </summary>
public sealed record ListAssignmentGroups(Guid AssignmentId) : IQuery<ActivityGroupRefDto[]>;
