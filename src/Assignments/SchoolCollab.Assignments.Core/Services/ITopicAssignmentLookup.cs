using SchoolCollab.Assignments.Core.DTOs;

namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// Cross-context port (Assignments → Students) that verifies an assignment's
/// subject is actually assigned to its target audience for a period covering the
/// assignment's effective date (spec activity-group-enrollment.md FR-58). For a
/// <c>SelectedGrades</c> assignment the topic must be assigned to the target grade;
/// for a <c>SelectedGroups</c> assignment it must be assigned to every linked group
/// (a null-<c>PeriodId</c> year-spanning assignment effective on the date, or a
/// period-aligned assignment whose period contains the date). The HTTP client
/// implementation lives in <c>SchoolCollab.Assignments.Api</c>.
/// </summary>
public interface ITopicAssignmentLookup
{
    /// <summary>
    /// Returns whether <paramref name="topicId"/> is assigned to the target for a
    /// period covering <paramref name="effectiveDate"/>. Exactly one of
    /// <paramref name="gradeLevelId"/> (SelectedGrades) or
    /// <paramref name="activityGroupIds"/> (SelectedGroups) is supplied.
    /// </summary>
    Task<bool> IsTopicAssignedAsync(
        Guid? gradeLevelId,
        IReadOnlyList<Guid> activityGroupIds,
        Guid topicId,
        DateOnly effectiveDate,
        CancellationToken cancellationToken = default);
}