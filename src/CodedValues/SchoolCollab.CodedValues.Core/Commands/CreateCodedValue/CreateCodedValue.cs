using SchoolCollab.CodedValues.Core.CQRS;

namespace SchoolCollab.CodedValues.Core.Commands.CreateCodedValue;

public sealed record CreateCodedValue(
    string Code,
    string Name,
    string? Description,
    Guid? ParentId,
    int DisplayOrder = 0) : ICommand;
