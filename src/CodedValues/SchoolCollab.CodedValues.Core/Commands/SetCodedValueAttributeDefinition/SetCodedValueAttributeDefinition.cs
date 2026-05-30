using SchoolCollab.CodedValues.Core.CQRS;
using SchoolCollab.CodedValues.Core.Domain;

namespace SchoolCollab.CodedValues.Core.Commands.SetCodedValueAttributeDefinition;

public sealed record SetCodedValueAttributeDefinition(
    Guid Id,
    string Key,
    string? DisplayName,
    AttributeDataType DataType,
    string? SourceCode,
    bool IsRequired) : ICommand;
