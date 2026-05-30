using SchoolCollab.CodedValues.Core.Domain;

namespace SchoolCollab.CodedValues.Core.DTOs;

/// <summary>
/// Describes an attribute slot defined on a parent coded-value, including type metadata
/// used by UI components to render and validate child attribute values.
/// </summary>
public record CodedValueAttributeDefinitionDto(
    string Key,
    string? DisplayName,
    AttributeDataType DataType,

    /// <summary>
    /// When <see cref="DataType"/> is <see cref="AttributeDataType.CodedValue"/>,
    /// the code of the parent coded-value whose children are the valid options.
    /// </summary>
    string? SourceCode,

    bool IsRequired,

    /// <summary>
    /// When true, the attribute value is expected to be an array (multi-select).
    /// When false, a single scalar value is expected.
    /// </summary>
    bool AllowMultiple);
