using SchoolCollab.CodedValues.Core.CQRS;

namespace SchoolCollab.CodedValues.Core.Commands.RecoverCodedValue;

public sealed record RecoverCodedValue(Guid Id) : ICommand;