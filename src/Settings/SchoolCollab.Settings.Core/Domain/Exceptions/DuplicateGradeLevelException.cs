namespace SchoolCollab.Settings.Core.Domain.Exceptions;

/// <summary>
/// Thrown when creating or updating a coded value under the <c>GRADE</c> parent
/// with a <c>DisplayOrder</c> that is already used by a sibling.
/// </summary>
public class DuplicateGradeLevelException(int level, Guid existingCodedValueId)
    : DomainException($"Grade level {level} already exists (CodedValue '{existingCodedValueId}').")
{
    public int Level { get; } = level;
    public Guid ExistingCodedValueId { get; } = existingCodedValueId;
}