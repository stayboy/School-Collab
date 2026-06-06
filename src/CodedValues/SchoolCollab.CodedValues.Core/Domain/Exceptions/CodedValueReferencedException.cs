namespace SchoolCollab.CodedValues.Core.Domain.Exceptions;

public class CodedValueReferencedException(Guid id, string[] referencingCodes)
    : DomainException($"Cannot delete coded value '{id}' because it is referenced as a source by: {string.Join(", ", referencingCodes)}. Remove references first.")
{
    public Guid Id { get; } = id;
    public string[] ReferencingCodes { get; } = referencingCodes;
}