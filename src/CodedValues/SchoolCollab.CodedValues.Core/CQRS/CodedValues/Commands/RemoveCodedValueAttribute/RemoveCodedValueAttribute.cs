using SchoolCollab.Core.CQRS;

namespace SchoolCollab.CodedValues.Core.CQRS.CodedValues.Commands.RemoveCodedValueAttribute;

public sealed record RemoveCodedValueAttribute(Guid Id, string Key) : ICommand;
