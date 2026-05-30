namespace SchoolCollab.CodedValues.Core.Domain;

public sealed class CodedValueAttribute
{
    public Guid CodedValueId { get; private set; }
    public string Key { get; private set; } = default!;
    public string Value { get; private set; } = default!;

    /// <summary>The expected data type of this attribute value, used for UI component selection.</summary>
    public AttributeDataType DataType { get; private set; }

    /// <summary>
    /// When <see cref="DataType"/> is <see cref="AttributeDataType.CodedValue"/>, identifies
    /// the parent coded value whose children represent the set of valid options.
    /// </summary>
    public string? SourceCode { get; private set; }

    private CodedValueAttribute() { }

    internal CodedValueAttribute(
        Guid codedValueId,
        string key,
        string value,
        AttributeDataType dataType,
        string? sourceCode)
    {
        CodedValueId = codedValueId;
        Key = key;
        Value = value;
        DataType = dataType;
        SourceCode = sourceCode;
    }
}
