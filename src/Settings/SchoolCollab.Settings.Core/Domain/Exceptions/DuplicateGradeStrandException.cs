namespace SchoolCollab.Settings.Core.Domain.Exceptions;

/// <summary>
/// Thrown when setting a <c>strandVersion</c> attribute on a strand coded value
/// (child of <c>GRSTRNDS</c>) whose <c>gradeLevel</c> attribute references a grade
/// that already has a strand with the same version label.
/// </summary>
public class DuplicateGradeStrandException(string gradeCode, string strandVersion, Guid existingCodedValueId)
    : DomainException($"Grade '{gradeCode}' already has a strand with version '{strandVersion}' (CodedValue '{existingCodedValueId}').")
{
    public string GradeCode { get; } = gradeCode;
    public string StrandVersion { get; } = strandVersion;
    public Guid ExistingCodedValueId { get; } = existingCodedValueId;
}