using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.CreateCodedValue;

public sealed record CreateCodedValue(
    string Code,
    string Name,
    string? Description,
    Guid? ParentId,
    int DisplayOrder = 0) : ICommand;
