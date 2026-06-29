using SchoolCollab.Core.CQRS;

namespace SchoolCollab.CodedValues.Core.CQRS.CodedValues.Commands.RemoveCodedValueAttributeDefinition;

public sealed record RemoveCodedValueAttributeDefinition(Guid Id, string Key) : ICommand;
