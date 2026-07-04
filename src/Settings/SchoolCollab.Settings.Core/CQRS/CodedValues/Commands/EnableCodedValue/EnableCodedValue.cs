using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.EnableCodedValue;

public sealed record EnableCodedValue(Guid Id) : ICommand;
