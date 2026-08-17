namespace SchoolCollab.Students.Application.Components.Students;

/// <summary>How the guardians section is rendered. <see cref="Inline"/>
/// (default) is the historical panel-switch UX — used when the form is
/// embedded inside a DIALOG (the wizard's inline "new student" form,
/// StudentPickerDialog's create flow, etc.). <see cref="Linked"/> is the
/// compact page-side "Guardians (N) — Manage" section used by
/// <c>/students/{id}/edit</c> and any future page that owns its own Save
/// flow + <see cref="IDialogService"/>. The two modes share the same
/// underlying state (links, drafts, error) so the page can hand the
/// component the same data it loads itself; the page-side mode simply
/// suppresses the panel switch in favour of modal dialog opens.
/// <para>Declared as a standalone type (rather than nested inside
/// <c>StudentFormFields</c>) so the extracted <c>GuardianSection</c>
/// component can consume it without referencing the form component.</para></summary>
public enum StudentFormFieldsMode
{
    /// <summary>Historical panel-switch UX (dialog callers; the default).</summary>
    Inline,
    /// <summary>Compact page-side section (page callers — Edit.razor, etc.).</summary>
    Linked,
}
