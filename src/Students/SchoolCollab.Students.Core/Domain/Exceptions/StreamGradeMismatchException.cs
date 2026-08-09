namespace SchoolCollab.Students.Core.Domain.Exceptions;

/// <summary>
/// Thrown when the <c>StreamCodedValueId</c> on a <c>StudentEnrollment</c>
/// references a stream whose <c>gradeLevel</c> attribute does not match the
/// enrollment's <c>GradeLevelId</c>. See spec §4.3 / FR-9.
/// </summary>
public sealed class StreamGradeMismatchException : Exception
{
    public Guid? StreamCodedValueId { get; }
    public Guid GradeLevelId { get; }

    public StreamGradeMismatchException(Guid? streamCodedValueId, Guid gradeLevelId)
        : base($"Stream '{streamCodedValueId}' does not reference grade level '{gradeLevelId}'.")
    {
        StreamCodedValueId = streamCodedValueId;
        GradeLevelId = gradeLevelId;
    }
}