namespace SchoolCollab.Settings.Application.Components.Pages.NotificationTemplates;

/// <summary>
/// Which half of the template lifecycle the shared editor is rendering (redesign spec §3.3/§3.4/§3.5).
/// Create: type and contact mode are editable, the draft is empty (or grade-prefilled), Save posts.
/// Edit: the two dropdowns are locked indicators, the draft is the stored row, Save replaces by id.
/// </summary>
public enum TemplateEditorMode
{
    /// <summary>A new template (route <c>/notification-templates/create</c>).</summary>
    Create = 0,

    /// <summary>An existing template (route <c>/notification-templates/{id}</c>).</summary>
    Edit = 1,
}