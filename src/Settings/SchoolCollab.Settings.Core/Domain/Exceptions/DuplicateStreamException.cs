namespace SchoolCollab.Settings.Core.Domain.Exceptions;

/// <summary>
/// Thrown when setting a <c>streamVersion</c> attribute on a stream coded value
/// (child of <c>GRSTREAMS</c>) whose <c>gradeLevel</c> attribute references a grade
/// that already has a stream with the same version label.
/// </summary>
public class DuplicateStreamException(string gradeCode, string streamVersion, Guid existingCodedValueId)
    : DomainException($"Grade '{gradeCode}' already has a stream with version '{streamVersion}' (CodedValue '{existingCodedValueId}').")
{
    public string GradeCode { get; } = gradeCode;
    public string StreamVersion { get; } = streamVersion;
    public Guid ExistingCodedValueId { get; } = existingCodedValueId;
}