using SchoolCollab.Core.CQRS;

namespace SchoolCollab.CodedValues.Core.CQRS.CodedValues.Commands.DeleteCodedValue;

public sealed record DeleteCodedValue(Guid Id) : ICommand;