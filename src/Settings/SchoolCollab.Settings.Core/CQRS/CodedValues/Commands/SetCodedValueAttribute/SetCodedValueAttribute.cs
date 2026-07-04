using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Settings.Core.CQRS.CodedValues.Commands.SetCodedValueAttribute;

public sealed record SetCodedValueAttribute(Guid Id, string Key, string Value) : ICommand;

