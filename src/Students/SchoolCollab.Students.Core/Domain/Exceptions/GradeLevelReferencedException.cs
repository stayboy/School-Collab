namespace SchoolCollab.Students.Core.Domain.Exceptions;

/// <summary>
/// Thrown when attempting to delete a GradeLevel that has dependent records.
/// </summary>
public sealed class GradeLevelReferencedException : Exception
{
    public Guid GradeLevelId { get; }
    public string[] References { get; }

    public GradeLevelReferencedException(Guid gradeLevelId, string[] references)
        : base($"Grade level '{gradeLevelId}' cannot be deleted because it is referenced by: {string.Join(", ", references)}.")
    {
        GradeLevelId = gradeLevelId;
        References = references;
    }
}