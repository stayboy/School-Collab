namespace SchoolCollab.Students.Core.Domain.Exceptions;

/// <summary>
/// Thrown when attempting to delete a Subject that has dependent records.
/// </summary>
public sealed class SubjectReferencedException : Exception
{
    public Guid SubjectId { get; }
    public string[] References { get; }

    public SubjectReferencedException(Guid subjectId, string[] references)
        : base($"Subject '{subjectId}' cannot be deleted because it is referenced by: {string.Join(", ", references)}.")
    {
        SubjectId = subjectId;
        References = references;
    }
}