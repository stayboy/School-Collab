namespace SchoolCollab.CodedValues.Core.Domain;

/// <summary>
/// Defines a named attribute slot that children of a <see cref="CodedValue"/> are expected to populate.
/// Carries the data-type and optional source-code so UI components know how to render and validate the value.
/// </summary>
public sealed class CodedValueAttributeDefinition
{
    public Guid CodedValueId { get; private set; }

    /// <summary>The attribute key that child values must use when setting this attribute.</summary>
    public string Key { get; private set; } = default!;

    /// <summary>Display-friendly label shown in UI forms.</summary>
    public string? DisplayName { get; private set; }

    /// <summary>Expected data type; drives UI component selection and value validation.</summary>
    public AttributeDataType DataType { get; private set; }

    /// <summary>
    /// When <see cref="DataType"/> is <see cref="AttributeDataType.CodedValue"/>, identifies
    /// the parent coded-value whose children are the set of valid options.
    /// </summary>
    public string? SourceCode { get; private set; }

    /// <summary>When true, every child coded-value should supply a value for this attribute.</summary>
    public bool IsRequired { get; private set; }

    private CodedValueAttributeDefinition() { }

    internal CodedValueAttributeDefinition(
        Guid codedValueId,
        string key,
        AttributeDataType dataType,
        string? sourceCode,
        bool isRequired,
        string? displayName)
    {
        CodedValueId = codedValueId;
        Key = key;
        DataType = dataType;
        SourceCode = sourceCode;
        IsRequired = isRequired;
        DisplayName = displayName;
    }
}
