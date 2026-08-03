namespace SchoolCollab.Students.Core.Domain.Exceptions;

/// <summary>
/// Thrown when attempting to delete a Topic that has dependent records.
/// </summary>
public sealed class TopicReferencedException : Exception
{
    public Guid TopicId { get; }
    public string[] References { get; }

    public TopicReferencedException(Guid topicId, string[] references)
        : base($"Topic '{topicId}' cannot be deleted because it is referenced by: {string.Join(", ", references)}.")
    {
        TopicId = topicId;
        References = references;
    }
}