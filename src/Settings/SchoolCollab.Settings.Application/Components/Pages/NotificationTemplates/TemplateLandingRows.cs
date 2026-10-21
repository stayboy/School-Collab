using SchoolCollab.Admin.Shared.Services;

namespace SchoolCollab.Settings.Application.Components.Pages.NotificationTemplates;

/// <summary>
/// Row mutations the notification-template landing applies optimistically
/// (`.github/copilot/rules/blazor-components.md` § "Landing-page performance pattern", rule 2): a
/// single-row action changes the in-memory list and re-renders, and only rolls back on failure — it
/// never re-reads the list, which would reset the operator's sort and scroll.
/// <para>
/// Kept as a pure seam because the reset action is opened by a dialog and dialog INVOCATION is not
/// renderable in bUnit (the area the round recorded as deferred); the mutation itself is what needs
/// proving, and it is provable here.
/// </para>
/// </summary>
public static class TemplateLandingRows
{
    /// <summary>The list with the reset row removed, order preserved.</summary>
    public static TenantNotificationTemplateDto[] WithoutReset(
        IReadOnlyList<TenantNotificationTemplateDto> rows, Guid resetId) =>
        rows.Where(r => r.Id != resetId).ToArray();
}
