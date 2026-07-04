namespace SchoolCollab.Settings.Core.Domain.Exceptions;

public class CodedValueHasChildrenException(Guid id, int childCount)
    : DomainException($"Cannot delete coded value '{id}' because it has {childCount} child(ren). Remove or reassign children first.")
{
    public Guid Id { get; } = id;
    public int ChildCount { get; } = childCount;
}