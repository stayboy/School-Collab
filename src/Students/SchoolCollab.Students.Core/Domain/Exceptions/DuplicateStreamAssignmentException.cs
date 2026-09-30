namespace SchoolCollab.Students.Core.Domain.Exceptions;

/// <summary>
/// Thrown when assigning a stream to a grade level would give that grade a second
/// stream carrying the same <c>streamVersion</c> label. Maps to HTTP 409 Conflict.
/// </summary>
/// <remarks>
/// The (grade, stream version) uniqueness rule moved here from the Settings
/// coded-value attribute write (<c>SetCodedValueAttributeHandler</c> /
/// <c>DuplicateStreamException</c>): the grade↔stream link is the
/// <see cref="Domain.GradeStreamAssignment"/> bridge row now, not the coded
/// value's <c>gradeLevel</c> attribute, so uniqueness is enforced at assignment
/// time. The Settings guard stays for direct writes through the coded-values
/// landing on any legacy row that still carries the attribute.
/// </remarks>
public sealed class DuplicateStreamAssignmentException(
    Guid gradeLevelId,
    string streamVersion,
    Guid existingCodedValueId)
    : Exception($"Grade level '{gradeLevelId}' already has a stream with version '{streamVersion}' (CodedValue '{existingCodedValueId}').")
{
    public Guid GradeLevelId { get; } = gradeLevelId;
    public string StreamVersion { get; } = streamVersion;
    public Guid ExistingCodedValueId { get; } = existingCodedValueId;
}
