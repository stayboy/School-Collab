using SchoolCollab.CodedValues.Core.CQRS;
using SchoolCollab.CodedValues.Core.Domain;

namespace SchoolCollab.CodedValues.Core.Commands.SetCodedValueAttribute;

public sealed record SetCodedValueAttribute(
    Guid Id,
    string Key,
    string Value,
    AttributeDataType DataType = AttributeDataType.Text,
    string? SourceCode = null) : ICommand;
