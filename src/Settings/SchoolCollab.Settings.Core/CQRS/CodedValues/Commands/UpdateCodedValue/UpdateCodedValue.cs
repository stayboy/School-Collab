using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.UpdateCodedValue;

public sealed record UpdateCodedValue(Guid Id, string Name, string? Description, int DisplayOrder) : ICommand;
