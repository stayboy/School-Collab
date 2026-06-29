using SchoolCollab.Core.CQRS;
using SchoolCollab.CodedValues.Core.Domain;

namespace SchoolCollab.CodedValues.Core.CQRS.CodedValues.Commands.SetCodedValueAttributeDefinition;

public sealed record SetCodedValueAttributeDefinition(
    Guid Id,
    string Key,
    string? DisplayName,
    AttributeDataType DataType,
    string? SourceCode,
    bool IsRequired,
    bool AllowMultiple = false,
    int? MinLength = null,
    int? MaxLength = null,
    string? RegexPattern = null) : ICommand;
