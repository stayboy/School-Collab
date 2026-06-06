using SchoolCollab.CodedValues.Core.CQRS;

namespace SchoolCollab.CodedValues.Core.Commands.DeleteCodedValue;

public sealed record DeleteCodedValue(Guid Id) : ICommand;