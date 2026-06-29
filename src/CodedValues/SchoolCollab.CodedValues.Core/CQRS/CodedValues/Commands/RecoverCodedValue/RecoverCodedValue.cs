using SchoolCollab.Core.CQRS;

namespace SchoolCollab.CodedValues.Core.CQRS.CodedValues.Commands.RecoverCodedValue;

public sealed record RecoverCodedValue(Guid Id) : ICommand;