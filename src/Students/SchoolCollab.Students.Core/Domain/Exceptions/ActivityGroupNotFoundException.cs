namespace SchoolCollab.Students.Core.Domain.Exceptions;

/// <summary>
/// Thrown when an <see cref="ActivityGroup"/> is not found (spec
/// activity-group-enrollment.md). Mirrors <see cref="GradeLevelNotFoundException"/>.
/// </summary>
public sealed class ActivityGroupNotFoundException : Exception
{
    public Guid ActivityGroupId { get; }

    public ActivityGroupNotFoundException(Guid id)
        : base($"Activity group with ID '{id}' was not found.")
        => ActivityGroupId = id;
}
