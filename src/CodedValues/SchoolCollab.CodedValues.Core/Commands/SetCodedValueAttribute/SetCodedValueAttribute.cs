using SchoolCollab.CodedValues.Core.CQRS;

namespace SchoolCollab.CodedValues.Core.Commands.SetCodedValueAttribute;

public sealed record SetCodedValueAttribute(Guid Id, string Key, string Value) : ICommand;

