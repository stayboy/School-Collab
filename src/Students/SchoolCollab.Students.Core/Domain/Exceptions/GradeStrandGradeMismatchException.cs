namespace SchoolCollab.Students.Core.Domain.Exceptions;

/// <summary>
/// Thrown when the <c>GradeStrandCodedValueId</c> on a <c>StudentEnrollment</c>
/// references a strand whose <c>gradeLevel</c> attribute does not match the
/// enrollment's <c>GradeLevelId</c>. See spec §4.3 / FR-9.
/// </summary>
public sealed class GradeStrandGradeMismatchException : Exception
{
    public Guid? GradeStrandCodedValueId { get; }
    public Guid GradeLevelId { get; }

    public GradeStrandGradeMismatchException(Guid? gradeStrandCodedValueId, Guid gradeLevelId)
        : base($"Grade strand '{gradeStrandCodedValueId}' does not reference grade level '{gradeLevelId}'.")
    {
        GradeStrandCodedValueId = gradeStrandCodedValueId;
        GradeLevelId = gradeLevelId;
    }
}