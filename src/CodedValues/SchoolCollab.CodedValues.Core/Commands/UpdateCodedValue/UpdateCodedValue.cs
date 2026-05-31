using SchoolCollab.CodedValues.Core.CQRS;

namespace SchoolCollab.CodedValues.Core.Commands.UpdateCodedValue;

public sealed record UpdateCodedValue(Guid Id, string Name, string? Description, int DisplayOrder) : ICommand;
