using SchoolCollab.Core.CQRS;

namespace SchoolCollab.CodedValues.Core.CQRS.CodedValues.Commands.UpdateCodedValue;

public sealed record UpdateCodedValue(Guid Id, string Name, string? Description, int DisplayOrder) : ICommand;
