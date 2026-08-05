using System.ComponentModel;

namespace SchoolCollab.Assignments.Application.Helpers;

/// <summary>
/// Reads <see cref="DescriptionAttribute"/> values from enum members via reflection.
/// Falls back to the enum member name when no attribute is present.
/// </summary>
public static class EnumHelper
{
    public static string GetDescription(Enum value)
    {
        var field = value.GetType().GetField(value.ToString());
        var attr = field?.GetCustomAttributes(typeof(DescriptionAttribute), false)
            .Cast<DescriptionAttribute>().FirstOrDefault();
        return attr?.Description ?? value.ToString();
    }
}