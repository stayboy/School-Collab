namespace SchoolCollab.Settings.Core.Domain;

public sealed class CodedValueAttribute
{
    public Guid CodedValueId { get; private set; }
    public string Key { get; private set; } = default!;
    public string Value { get; private set; } = default!;

    private CodedValueAttribute() { }

    internal CodedValueAttribute(Guid codedValueId, string key, string value)
    {
        CodedValueId = codedValueId;
        Key = key;
        Value = value;
    }
}

