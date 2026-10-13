using System.ComponentModel;

namespace SchoolCollab.Mobile.Logic;

/// <summary>
/// Reads <see cref="DescriptionAttribute"/> values from enum members, falling back to the member name —
/// the assignments screens' display formatter for the slice's enum columns.
/// </summary>
/// <remarks>
/// <b>A conscious copy</b> of <c>Assignments.Application.Helpers.EnumHelper</c> (17 BCL-only lines), not
/// a move. The helper lives in <c>Assignments.Application</c>, which the head must not reference (that
/// project's closure carries the <c>Microsoft.AspNetCore.App</c> FrameworkReference that broke
/// <c>net10.0-android</c>), and moving it out would churn the nine <c>src/Assignments/</c> pages plus
/// <c>QuestionPromptComposer.cs</c> that call it — exactly the diff the L4 round keeps out of that
/// project (open PR #342 touches it). The duplication's drift cost is bounded to display text: a
/// description added on one side only shows a raw enum name on the other, never a wrong value.
/// </remarks>
public static class AssignmentEnumDisplay
{
    /// <summary>
    /// The <see cref="DescriptionAttribute"/> text of <paramref name="value"/>'s member, or the member
    /// name when the member carries no attribute.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static string GetDescription(Enum value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var field = value.GetType().GetField(value.ToString());
        var attribute = field?
            .GetCustomAttributes(typeof(DescriptionAttribute), false)
            .Cast<DescriptionAttribute>()
            .FirstOrDefault();

        return attribute?.Description ?? value.ToString();
    }
}
