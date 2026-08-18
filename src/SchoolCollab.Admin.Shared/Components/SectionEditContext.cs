using Microsoft.AspNetCore.Components;

namespace SchoolCollab.Admin.Shared.Components;

/// <summary>Describes the edit form a section publishes up to its host so
/// the host can render it inside a single shared drawer (e.g.
/// <see cref="DialogDrawer"/>) instead of the section rendering its own
/// drawer. The fragment captures the section's in-place edit state via
/// closure, so the section remains the owner of its fields and validation
/// — the host is just chrome.
/// <para>Instances are typically built by <c>ContactsEditor</c> /
/// <c>GuardianSection</c> and pushed to <c>StudentFormFields</c> via
/// <c>EventCallback&lt;SectionEditContext&gt;</c> when an edit begins;
/// the form forwards the latest context to <c>StudentEditDialog</c>,
/// which renders it inside the single <see cref="DialogDrawer"/>.</para>
/// </summary>
/// <param name="SectionKey">Stable identifier of the publishing section
/// (e.g. <c>"Contacts"</c>, <c>"Guardians"</c>). Used by the host to detect
/// a genuine cross-section swap (different key) vs. the same section
/// re-publishing an updated fragment (same key). On a cross-section swap the
/// host cancels the previous context first; on a same-section re-publish it
/// just adopts the new fragment, so the section can drive reactive UI inside
/// the published fragment by re-publishing on state changes.</param>
/// <param name="Title">Drawer header text.</param>
/// <param name="Content">Renderable edit form body. Captures the
/// section's edit state via closure.</param>
/// <param name="Submit">Invoked when the drawer Submit button is clicked.
/// Return <c>true</c> to auto-close the drawer on success; <c>false</c>
/// to keep it open when validation fails.</param>
/// <param name="Cancel">Invoked when the drawer Cancel / × / backdrop /
/// Escape closes the drawer. The section should discard its working
/// copy and clear its edit state.</param>
public sealed record SectionEditContext(
    string SectionKey,
    string Title,
    RenderFragment Content,
    Func<Task<bool>> Submit,
    Func<Task> Cancel);