using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Core.RichText;

namespace SchoolCollab.Settings.Application.Components.Pages.NotificationTemplates;

/// <summary>
/// The landing grid's filtering, extracted so it is testable at a pure seam (redesign spec 3.2): the
/// grade-deep-link rule, the type / contact-mode / owner filters and the subject-and-body search all
/// live here, once, instead of inside the page's render path.
/// <para>
/// The grade rule is the 3.11 applicability rule the old page carried: a template applies to a grade
/// when it has <b>no</b> GradeLevel constraint, or one of them is that grade (trimmed, case-insensitive -
/// the matcher's own comparison).
/// </para>
/// </summary>
public static class TemplateLandingFilter
{
    /// <summary>The owner-filter value for system-blueprint rows.</summary>
    public const string SystemOwner = "system";

    /// <summary>The owner-filter value for rows owned by the session's own tenant.</summary>
    public const string TenantOwner = "tenant";

    /// <summary>Applies every active filter. Empty strings mean "no filter" (the toolbar's All entry).</summary>
    public static TenantNotificationTemplateDto[] Apply(
        IReadOnlyList<TenantNotificationTemplateDto> rows,
        Guid? gradeLevelId,
        string kindFilter,
        string modeFilter,
        string ownerFilter,
        string search)
    {
        IEnumerable<TenantNotificationTemplateDto> filtered = rows;

        if (gradeLevelId is { } grade)
            filtered = filtered.Where(r => AppliesToGrade(r, grade));

        if (kindFilter.Length > 0)
            filtered = filtered.Where(r => string.Equals(r.Kind.ToString(), kindFilter, StringComparison.Ordinal));

        if (modeFilter.Length > 0)
            filtered = filtered.Where(r => string.Equals(r.Channel.ToString(), modeFilter, StringComparison.Ordinal));

        // A tenant session's list already contains only its own rows plus the blueprint, so "tenant"
        // simply means not-system - it never widens what the session can see (spec 3.7).
        if (ownerFilter.Length > 0)
        {
            filtered = ownerFilter switch
            {
                SystemOwner => filtered.Where(r => r.IsSystem),
                TenantOwner => filtered.Where(r => !r.IsSystem),
                _ => filtered,
            };
        }

        if (search.Length > 0)
        {
            filtered = filtered.Where(r =>
                (r.Subject?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                || RichInstructionSanitizer.ToPlainText(r.Body)?.Contains(search, StringComparison.OrdinalIgnoreCase) == true);
        }

        return filtered.ToArray();
    }    /// <summary>Whether the template can apply to <paramref name="gradeLevelId"/> (spec 3.11).</summary>
    public static bool AppliesToGrade(TenantNotificationTemplateDto row, Guid gradeLevelId)
    {
        var grade = gradeLevelId.ToString("D");
        var gradeConstraints = row.Constraints
            .Where(c => c.Dimension == TemplateConstraintDimension.GradeLevel)
            .ToArray();

        return gradeConstraints.Length == 0
            || gradeConstraints.Any(c => string.Equals(c.Value.Trim(), grade, StringComparison.OrdinalIgnoreCase));
    }
}