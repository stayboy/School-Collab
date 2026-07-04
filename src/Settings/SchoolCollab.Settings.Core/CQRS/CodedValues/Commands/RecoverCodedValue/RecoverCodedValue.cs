using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.RecoverCodedValue;

public sealed record RecoverCodedValue(Guid Id) : ICommand;