namespace SchoolCollab.Settings.Core.Domain;

/// <summary>
/// Indicates the expected data type of a coded-value attribute value,
/// used by UI components to select the appropriate input control and validation.
/// </summary>
public enum AttributeDataType
{
    Text = 0,
    Integer = 1,
    Decimal = 2,
    Boolean = 3,
    Date = 4,
    DateTime = 5,
    Time = 6,

    /// <summary>
    /// Valid values are drawn from a child list of a coded value.
    /// Use <see cref="CodedValueAttribute.SourceCode"/> to identify the parent code.
    /// </summary>
    CodedValue = 7
}
