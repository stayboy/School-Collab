namespace SchoolCollab.Assignments.Core.DTOs;

/// <summary>
/// Summary of an assignment that references a given activity group, returned by
/// <c>GET /api/activity-groups/{id}/assignments</c> (spec §7.3
/// <c>AssignmentSummary[]</c>). Consumed by the Students-context delete-guard port
/// <c>IActivityGroupAssignmentQuery</c> (FR-6).
/// </summary>
public sealed record AssignmentGroupSummaryDto(
    Guid Id,
    string Title,
    string Status);
