using SchoolCollab.CodedValues.Core.CQRS;

namespace SchoolCollab.CodedValues.Core.Commands.EnableCodedValue;

public sealed record EnableCodedValue(Guid Id) : ICommand;
