namespace SchoolCollab.Students.Core.Domain.Exceptions;

/// <summary>
/// Thrown when a student is not eligible for membership in an
/// <see cref="ActivityGroup"/> because the group declares a grade-eligibility
/// set and the student's active grade-for-period is not in it (Rev. 2,
/// spec activity-group-enrollment.md FR-13/40). Maps to HTTP 422.
/// </summary>
public sealed class GradeNotEligibleException : Exception
{
    public Guid ActivityGroupId { get; }
    public Guid GradeLevelId { get; }

    public GradeNotEligibleException(Guid activityGroupId, Guid gradeLevelId)
        : base($"Activity group '{activityGroupId}' is not eligible for grade level '{gradeLevelId}'.")
    {
        ActivityGroupId = activityGroupId;
        GradeLevelId = gradeLevelId;
    }
}