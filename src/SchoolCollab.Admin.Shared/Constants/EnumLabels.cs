using System.ComponentModel;
using System.Reflection;

namespace SchoolCollab.Admin.Shared.Constants;

/// <summary>
/// The one display-label reader for an enum member's <see cref="DescriptionAttribute"/> text, so a
/// shared enum such as <c>NotificationChannel</c> is spelled once in the UI instead of once per page.
/// </summary>
/// <remarks>
/// The attribute is the label's home: a page that hand-writes a <c>switch</c> over the members drifts
/// from it the first time wording changes, and two such switches drift from each other. Falls back to
/// the member name when no attribute is present, so an undescribed member still renders something
/// readable rather than throwing.
/// </remarks>
public static class EnumLabels
{
    /// <summary>
    /// The <see cref="DescriptionAttribute"/> text of <paramref name="value"/>'s member, or the member
    /// name when the member carries no attribute.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static string For(Enum value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var attribute = value.GetType()
            .GetField(value.ToString(), BindingFlags.Public | BindingFlags.Static)?
            .GetCustomAttribute<DescriptionAttribute>();

        return attribute?.Description ?? value.ToString();
    }
}
