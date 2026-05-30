using SchoolCollab.CodedValues.Core.CQRS;

namespace SchoolCollab.CodedValues.Core.Commands.RemoveCodedValueAttribute;

public sealed record RemoveCodedValueAttribute(Guid Id, string Key) : ICommand;
