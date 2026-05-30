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

    /// <summary>
    /// When true, the attribute value on a child coded-value is expected to be stored and
    /// interpreted as an array (e.g. comma-separated or JSON array). When false, a single
    /// scalar value is expected. UI components use this flag to render multi-select or
    /// tag-style inputs instead of a single-value field.
    /// </summary>
    public bool AllowMultiple { get; private set; }

    /// <summary>Minimum character length for the attribute value. Null means no minimum.</summary>
    public int? MinLength { get; private set; }

    /// <summary>Maximum character length for the attribute value. Null means no maximum.</summary>
    public int? MaxLength { get; private set; }

    /// <summary>
    /// Optional regular expression the attribute value must match.
    /// Null means no pattern constraint. UI components display this as
    /// an inline format hint and validators enforce it before saving.
    /// </summary>
    public string? RegexPattern { get; private set; }

    private CodedValueAttributeDefinition() { }

    internal CodedValueAttributeDefinition(
        Guid codedValueId,
        string key,
        AttributeDataType dataType,
        string? sourceCode,
        bool isRequired,
        bool allowMultiple,
        string? displayName,
        int? minLength,
        int? maxLength,
        string? regexPattern)
    {
        CodedValueId = codedValueId;
        Key = key;
        DataType = dataType;
        SourceCode = sourceCode;
        IsRequired = isRequired;
        AllowMultiple = allowMultiple;
        DisplayName = displayName;
        MinLength = minLength;
        MaxLength = maxLength;
        RegexPattern = regexPattern;
    }
}
