using SchoolCollab.Assignments.Core.DTOs;
using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.ListAssignmentsForGroup;

/// <summary>
/// Returns the assignments targeting a given activity group (spec §7.3
/// <c>GET /api/activity-groups/{groupId}/assignments</c> — consumed by the
/// Students-context FR-6 delete guard).
/// </summary>
public sealed record ListAssignmentsForGroup(Guid ActivityGroupId) : IQuery<AssignmentGroupSummaryDto[]>;
