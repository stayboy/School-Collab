using SchoolCollab.Core.CQRS;

namespace SchoolCollab.CodedValues.Core.CQRS.CodedValues.Commands.SetCodedValueAttribute;

public sealed record SetCodedValueAttribute(Guid Id, string Key, string Value) : ICommand;

