using SchoolCollab.Core.CQRS;

namespace SchoolCollab.CodedValues.Core.CQRS.CodedValues.Commands.EnableCodedValue;

public sealed record EnableCodedValue(Guid Id) : ICommand;
