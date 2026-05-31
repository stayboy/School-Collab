using SchoolCollab.CodedValues.Core.CQRS;

namespace SchoolCollab.CodedValues.Core.Commands.RemoveCodedValueAttributeDefinition;

public sealed record RemoveCodedValueAttributeDefinition(Guid Id, string Key) : ICommand;
