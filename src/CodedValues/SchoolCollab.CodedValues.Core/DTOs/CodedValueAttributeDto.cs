using SchoolCollab.CodedValues.Core.Domain;

namespace SchoolCollab.CodedValues.Core.DTOs;

/// <summary>
/// Represents a single attribute on a coded value, including type metadata
/// needed by UI components for rendering and validation.
/// </summary>
public record CodedValueAttributeDto(
    string Key,
    string Value,
    AttributeDataType DataType,

    /// <summary>
    /// When <see cref="DataType"/> is <see cref="AttributeDataType.CodedValue"/>,
    /// this holds the code of the parent coded value whose children are the valid options.
    /// </summary>
    string? SourceCode);
