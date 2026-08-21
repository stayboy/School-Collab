using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// Source-level tests for the ContactsEditor / GuardianSection two-mode
/// (Readonly/Edit) refactor in the student edit dialog
/// (plan 2026-08-18-contact-and-guardian-editors-readonly-edit-modes.md).
///
/// The structural invariants (both editors expose View { Readonly, Edit } as
/// plain Razor markup; the dialog renders Readonly summaries on the main
/// content and the active editor inside a single DialogDrawer; no fragment
/// publish-up remains) are asserted at the SOURCE level by reading the .razor
/// files from disk, the same pattern as <see cref="StudentDetailSectionsTests"/>.
/// </summary>
[TestClass]
public class StudentFormFieldsSectionEditTests
{
    private static string ReadSource(string relative)
    {
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var srcPath = Path.GetFullPath(Path.Combine(
            asmDir, "..", "..", "..", "..", "..", "src", relative));
        File.Exists(srcPath).Should().BeTrue(
            $"source should exist at '{srcPath}' — check the path resolution");
        return File.ReadAllText(srcPath);
    }

    private static string ReadFormFieldsSource() => ReadSource(
        "Students/SchoolCollab.Students.Application/Components/Students/StudentFormFields.razor");

    private static string ReadEditDialogSource() => ReadSource(
        "Students/SchoolCollab.Students.Application/Components/Students/StudentEditDialog.razor");

    private static string ReadContactsEditorSource() => ReadSource(
        "SchoolCollab.Admin.Shared/Components/ContactsEditor.razor");

    private static string ReadContactFormFieldsSource() => ReadSource(
        "SchoolCollab.Admin.Shared/Components/ContactFormFields.razor");

    private static string ReadGuardianSectionSource() => ReadSource(
        "Students/SchoolCollab.Students.Application/Components/Students/GuardianSection.razor");

    private static string ReadFormFieldsCssSource() => ReadSource(
        "Students/SchoolCollab.Students.Application/Components/Students/StudentFormFields.razor.css");

    private static string ReadDetailSource() => ReadSource(
        "Students/SchoolCollab.Students.Application/Components/Pages/Students/GradeLevels/Detail.razor");

    private static string ReadDialogServiceExtensionsSource() => ReadSource(
        "SchoolCollab.Admin.Shared/Components/Dialogs/DialogServiceExtensions.cs");

    private static string ReadGuardianSectionCssSource() => ReadSource(
        "Students/SchoolCollab.Students.Application/Components/Students/GuardianSection.razor.css");

    private static string ReadGuardianEditFieldsSource() => ReadSource(
        "SchoolCollab.Admin.Shared/Components/GuardianEditFields.razor");

    // ---- Both editors expose View { Readonly, Edit } ----

    [TestMethod]
    public void ContactsEditor_ExposesReadonlyAndEditViewsAsPlainRazor()
    {
        var source = ReadContactsEditorSource();

        // View dimension includes the new Full default (existing callers) plus
        // the two surfaces used in the student edit dialog.
        source.Should().Contain("public enum ContactsView { Full, Readonly, Edit }",
            "the editor exposes the Full/Readonly/Edit presentation dimension");
        source.Should().Contain("[Parameter] public ContactsView View { get; set; } = ContactsView.Full;",
            "View defaults to Full so existing callers render the full surface unchanged");
        source.Should().Contain("View == ContactsView.Readonly",
            "the Readonly view is rendered as plain Razor (no RenderTreeBuilder)");
        source.Should().Contain("View == ContactsView.Edit",
            "the Edit view (focused per-item form) is rendered as plain Razor");

        source.Should().NotContain("BuildEditFragment", "the RenderTreeBuilder builder is gone");
        source.Should().NotContain("SectionEditContextChanged", "no publish-up channel remains");
        source.Should().NotContain("EventCallback<bool> IsEditingChanged", "no editing-state callback remains");
        source.Should().NotContain("PublishEditContextAsync", "no publish helper remains");

        // The Readonly summary raises per-card Edit/Add and an "Add" anchor;
        // host receives IsAdd / InitialEditKey to drive the focused edit form.
        source.Should().Contain("OnEditContact.InvokeAsync(c.Key)",
            "the Readonly view's per-card Edit button raises the host callback with the contact's key");
        source.Should().Contain("OnAddContact.InvokeAsync()",
            "the Readonly view's Add anchor raises the host callback");
        source.Should().Contain("[Parameter] public bool IsAdd",
            "the Edit view accepts an IsAdd flag for the blank add form");
        source.Should().Contain("[Parameter] public Guid? InitialEditKey",
            "the Edit view accepts an InitialEditKey for focused per-item edit");
        source.Should().Contain("[Parameter] public bool EditDisabled",
            "the Readonly view's per-card Edit/Delete and the Add anchor respect EditDisabled while a drawer is open");
    }

    [TestMethod]
    public void GuardianSection_ExposesReadonlyAndEditViewsAsPlainRazor()
    {
        var source = ReadGuardianSectionSource();

        source.Should().Contain("public enum GuardianView { Full, Readonly, Edit }",
            "the section exposes the Full/Readonly/Edit presentation dimension");
        source.Should().Contain("[Parameter] public GuardianView View { get; set; } = GuardianView.Full;",
            "View defaults to Full so existing callers render the full surface unchanged");
        source.Should().Contain("View == GuardianView.Readonly",
            "the Readonly view is rendered as plain Razor (no RenderTreeBuilder)");
        source.Should().Contain("View == GuardianView.Edit",
            "the Edit view (focused per-item form) is rendered as plain Razor");

        source.Should().NotContain("BuildEditFragment", "the RenderTreeBuilder builder is gone");
        source.Should().NotContain("SectionEditContextChanged", "no publish-up channel remains");
        source.Should().NotContain("EventCallback<bool> IsEditingChanged", "no editing-state callback remains");
        source.Should().NotContain("PublishEditContextAsync", "no publish helper remains");

        source.Should().Contain("OnEditGuardian.InvokeAsync(",
            "the Readonly view's per-card Edit button raises the host callback with the guardian's index");
        source.Should().Contain("OnAddGuardian.InvokeAsync()",
            "the Readonly view's Add guardian anchor raises the host callback");
        source.Should().Contain("[Parameter] public int InitialEditIndex",
            "the Edit view accepts an InitialEditIndex for focused per-item edit");
        source.Should().Contain("[Parameter] public bool EditDisabled",
            "the Readonly view's per-card Edit/Delete and the Add anchor respect EditDisabled while a drawer is open");
    }

    // ---- StudentFormFields renders Readonly summaries with per-card Edit/Delete + Add anchor ----
    // No section-level "Manage" button — entry to the drawer is per-card Edit or
    // the "Add contact" / "Add guardian" FluentAnchor.

    [TestMethod]
    public void StudentFormFields_RendersReadonlySummariesWithoutManageButtons()
    {
        var source = ReadFormFieldsSource();

        source.Should().Contain("[Parameter] public bool SectionsReadonly { get; set; }",
            "the form opt-in controls the Readonly summary mode");
        source.Should().Contain("View=\"ContactsEditor.ContactsView.Readonly\"",
            "the contacts summary renders the editor in Readonly view");
        source.Should().Contain("View=\"GuardianSection.GuardianView.Readonly\"",
            "the guardians summary renders the section in Readonly view");

        // Per-card / Add anchors raised through to host (not section-level buttons)
        source.Should().Contain("[Parameter] public EventCallback<Guid> OnEditContact { get; set; }",
            "the form forwards per-card contact Edit to the host");
        source.Should().Contain("[Parameter] public EventCallback OnAddContact { get; set; }",
            "the form forwards the contacts Add anchor to the host");
        source.Should().Contain("[Parameter] public EventCallback<int> OnEditGuardian { get; set; }",
            "the form forwards per-card guardian Edit to the host");
        source.Should().Contain("[Parameter] public EventCallback OnAddGuardian { get; set; }",
            "the form forwards the guardians Add anchor to the host");

        // EditDisabled is forwarded so per-card triggers dim while a drawer is open.
        source.Should().Contain("EditDisabled=\"AreProfileFieldsDisabled\"",
            "the Readonly editor's per-card triggers and Add anchor respect the EditDisabled signal");
        source.Should().Contain("OnEditContact=\"OnEditContact\"",
            "the form forwards per-card contact Edit into the Readonly ContactsEditor");
        source.Should().Contain("OnAddContact=\"OnAddContact\"",
            "the form forwards the Add contact anchor into the Readonly ContactsEditor");
        source.Should().Contain("OnEditGuardian=\"OnEditGuardian\"",
            "the form forwards per-card guardian Edit into the Readonly GuardianSection");
        source.Should().Contain("OnAddGuardian=\"OnAddGuardian\"",
            "the form forwards the Add guardian anchor into the Readonly GuardianSection");

        // No section-level Manage button rendered as a FluentButton. The
        // strings "Manage contacts" / "Manage guardians" / the click handlers
        // must not appear inside a <FluentButton ...>Manage ...</FluentButton>
        // markup on the Readonly path.
        source.Should().NotContain(">Manage contacts<",
            "no section-level Manage contacts button — entry is per-card Edit or the Add anchor");
        source.Should().NotContain(">Manage guardians<",
            "no section-level Manage guardians button — entry is per-card Edit or the Add anchor");
        source.Should().NotContain("OnManageContactsClick",
            "the old Manage contacts click handler is gone");
        source.Should().NotContain("OnManageGuardiansClick",
            "the old Manage guardians click handler is gone");

        source.Should().Contain("Label=\"Contacts\"", "the contacts section renders");
        source.Should().Contain("Label=\"Guardians\"", "the guardians section renders");
    }
[TestMethod]
    public void ProfileFieldsAndActions_DisabledWhileSectionEditing()
    {
        var source = ReadFormFieldsSource();
        var css = ReadFormFieldsCssSource();

        source.Should().Contain("[Parameter] public bool ProfileFieldsDisabled { get; set; }",
            "the host drives profile/action disabling from 'a drawer is open'");
        source.Should().Contain("private bool AreProfileFieldsDisabled => ProfileFieldsDisabled;",
            "AreProfileFieldsDisabled mirrors the external signal");
        source.Should().Contain("Disabled=\"@AreProfileFieldsDisabled\"",
            "profile fields are disabled while a section is being edited");
        source.Should().Contain("Disabled=\"@(Submitting || AreProfileFieldsDisabled)\"",
            "the dialog action buttons are disabled (not removed) while a drawer is open");
        css.Should().NotContain("student-form-fields__section-row--hidden",
            "no CSS hides a sibling section - both summaries stay visible");
    }

    // ---- StudentEditDialog: single drawer hosting a focused per-item edit form ----
    // The dialog owns InitialEditKey / InitialEditIndex / IsAdd; the Readonly
    // summary raises per-card Edit/Add which the dialog translates into drawer
    // parameters. No fragment publish-up.

    [TestMethod]
    public void StudentEditDialog_ShowsReadonlyMain_AndFocusedEditInSingleDrawer()
    {
        var source = ReadEditDialogSource();
        var css = ReadSource(
            "Students/SchoolCollab.Students.Application/Components/Students/StudentEditDialog.razor.css");

        source.Should().Contain("SectionsReadonly=\"true\"", "the dialog opts into Readonly summaries");
        source.Should().Contain("ProfileFieldsDisabled=\"@(_editor != ActiveEditor.None)\"",
            "the dialog disables profile fields while a drawer is open");

        source.Should().Contain("<DialogDrawer", "the dialog renders a single DialogDrawer");
        source.Should().Contain("Side=\"DialogDrawerSide.Right\"", "the drawer anchors right");
        source.Should().Contain("<ContactsEditor View=\"ContactsEditor.ContactsView.Edit\"",
            "the focused per-item contacts editor is hosted directly in the drawer");
        source.Should().Contain("<GuardianSection View=\"GuardianSection.GuardianView.Edit\"",
            "the focused per-item guardians editor is hosted directly in the drawer");

        // Drawer parameters carry the focused-edit target into the section editors.
        source.Should().Contain("InitialEditKey=\"@_editingContactKey\"",
            "the contacts editor in the drawer receives the focused contact key");
        source.Should().Contain("InitialEditIndex=\"@_editingGuardianIndex\"",
            "the guardians editor in the drawer receives the focused guardian index");
        source.Should().Contain("IsAdd=\"@_isAdd\"",
            "the drawer editors receive the IsAdd flag (blank form for Add, populated for Edit)");

        // Host translates per-card triggers into drawer state — no section-level Manage buttons.
        source.Should().Contain("OnEditContact=\"OpenEditContactAsync\"",
            "per-card contact Edit raises the dialog's drawer opener");
        source.Should().Contain("OnAddContact=\"OpenAddContactFormAsync\"",
            "the Add contact anchor raises the dialog's drawer opener");
        source.Should().Contain("OnEditGuardian=\"OpenEditGuardianAsync\"",
            "per-card guardian Edit raises the dialog's drawer opener");
        source.Should().Contain("OnAddGuardian=\"OpenAddGuardianAsync\"",
            "the Add guardian anchor raises the dialog's drawer opener");

        source.Should().NotContain("OnManageContacts=\"OpenContactsEditorAsync\"",
            "the old section-level Manage contacts path is gone");
        source.Should().NotContain("OnManageGuardians=\"OpenGuardiansEditorAsync\"",
            "the old section-level Manage guardians path is gone");

        source.Should().Contain("class=\"student-edit-dialog-root @RootModifierClasses\"",
            "the dialog content is wrapped in the positioned root (with the conditional height modifier)");

        // Drawer title reflects which section is open and whether it's Add or Edit.
        source.Should().Contain("Title=\"@GetDrawerTitle()\"",
            "the drawer title is computed from the active editor + Add/Edit mode");
        source.Should().Contain("Edit contact", "the title is 'Edit contact' when editing an existing contact");
        source.Should().Contain("Add contact", "the title is 'Add contact' when adding a new contact");
        source.Should().Contain("Edit Guardian", "the title is 'Edit Guardian' when editing an existing guardian");
        source.Should().Contain("Add guardian", "the title is 'Add guardian' when adding a new guardian");

        css.Should().Contain(".student-edit-dialog-root {", "the positioned-root CSS rule exists");
        css.Should().Contain("position: relative;", "the positioned root is the drawer's containing block");

        // No fragment-publish-up scaffolding.
        source.Should().NotContain("SectionEditContent", "no fragment publish-up param remains");
        source.Should().NotContain("_sectionEditContent", "no published-context field remains");
        source.Should().NotContain("StudentEditSection", "the old section-state machine is gone");
        source.Should().NotContain("previous.SectionKey", "the swap-cancel guard is gone");
        source.Should().NotContain("EnableSectionEdit", "EnableSectionEdit is gone");
    }

    // ---- Focused-edit cancellation: closing the drawer clears the focused-edit state ----

    [TestMethod]
    public void StudentEditDialog_DrawerCloseClearsFocusedEditState()
    {
        var source = ReadEditDialogSource();

        // OnDrawerOpenChangedAsync clears the per-card state so the next open
        // is a fresh drawer and the per-card triggers re-enable.
        source.Should().Contain("OnDrawerOpenChangedAsync",
            "the dialog wires a drawer OpenChanged handler");
        source.Should().Contain("_editingContactKey = null;",
            "closing the drawer clears the focused-contact key");
        source.Should().Contain("_editingGuardianIndex = -1;",
            "closing the drawer clears the focused-guardian index (using GuardianSection's -1 convention)");
        source.Should().Contain("_isAdd = false;",
            "closing the drawer clears the IsAdd flag");
    }

    // ---- Edit view stacks fields via FormRow; the drawer owns Cancel ----

    [TestMethod]
    public void ContactsEditor_EditView_UsesFormRowAndDropsInlineCancel()
    {
        var source = ReadContactsEditorSource();

        // The Edit view is hosted inside the 420px student-edit-dialog side
        // drawer. The drawer already exposes a Close button (DialogDrawer's
        // ShowCancel="true" CancelText="Close"), so the inline Cancel button
        // is redundant. The fields below are stacked vertically using the
        // shared <FormRow> primitive (label top, input below) so the narrow
        // drawer breathes. The Add and Edit branches share ONE field group
        // (ContactFormFields) — so the vertical FormRow rows live in
        // ContactFormFields.razor rather than being duplicated inline.
        source.Should().Contain("class=\"contacts-edit-form\"",
            "the Edit view wraps its fields in a dedicated edit-form container");
        source.Should().Contain("<ContactFormFields",
            "the Add and Edit branches render the shared ContactFormFields field group");

        // Channel / Value / Label are the three rows in the shared group, each
        // a Vertical FormRow. The channel FormRow also holds the country-code
        // selector (a CodedValueDropdown, shown for SMS / WhatsApp) so the two
        // short selectors share one "Channel" label.
        var fields = ReadContactFormFieldsSource();
        fields.Should().Contain("<FormRow Label=\"Channel\" Orientation=\"RowOrientation.Vertical\">",
            "the channel field is rendered as a vertical FormRow (explicit Orientation for the narrow drawer)");
        fields.Should().Contain("<FormRow Label=\"@ValueLabel\" Required Orientation=\"RowOrientation.Vertical\">",
            "the value field is rendered as a vertical FormRow with channel-aware label and Required");
        fields.Should().Contain("<FormRow Label=\"Label\" Orientation=\"RowOrientation.Vertical\">",
            "the optional label field is rendered as a vertical FormRow");

        // The country-code selector now lives INSIDE the Channel FormRow
        // (combined under the "Channel" label), not in its own row.
        fields.Should().Contain("Width=\"FieldWidth.W5\"",
            "the country-code selector keeps its canonical W5 width");
        fields.Should().Contain("@if (Model.Channel is ContactChannel.SMS or ContactChannel.WhatsApp)",
            "the country-code selector stays channel-gated (SMS / WhatsApp only)");

        // Channel-aware label/placeholder properties stay on the editor; the
        // shared group is parameterised with them.
        source.Should().Contain("private string ValueLabel",
            "the Add branch has a channel-aware label property");
        source.Should().Contain("private string EditValueLabel",
            "the Edit branch has a channel-aware label property");

        // No inline Cancel button on the Edit branch. The Drawer footer
        // (Close) is the Cancel affordance; rendering a Cancel inside the
        // drawer body duplicates chrome and competes for the same action.
        //
        // We slice the source at the Edit view's opening 'else if (View ==
        // ContactsView.Edit)' so a Cancel button rendered elsewhere (e.g. the
        // Full view's Buffered inline-edit form, line ~339) doesn't
        // false-pass. The Buffered inline-edit Cancel is still required by
        // its tests and must NOT be removed.
        var editViewStart = source.IndexOf("else if (View == ContactsView.Edit)", StringComparison.Ordinal);
        editViewStart.Should().BeGreaterThan(-1, "the Edit view branch exists");
        // Slice to the next 'else' (the Full-view branch opener). The slice
        // MUST contain the Edit view body and only the Edit view body.
        var fullViewStart = source.IndexOf("else\r\n    {\r\n        @if (Mode == EditorMode.Live && _loading)", editViewStart, StringComparison.Ordinal);
        // Fallback: a less strict slice — to the next 'else' at the same indent
        // depth (column 0 of a line with 'else'). Use IndexOf with Ordinal and
        // a hard cap (the Edit-view block is < ~100 lines).
        if (fullViewStart < 0) fullViewStart = source.IndexOf("\n    }\n    else\n    {\n", editViewStart, StringComparison.Ordinal);
        fullViewStart.Should().BeGreaterThan(editViewStart,
            "the Edit view slice has a well-defined end (the next 'else' opens the Full view)");
        var editViewBody = source.Substring(editViewStart, fullViewStart - editViewStart);

        editViewBody.Should().NotContain("CancelInlineEditAsync",
            "the Edit view branch no longer calls CancelInlineEditAsync — the drawer owns Close");
        editViewBody.Should().NotContain(">Cancel<",
            "the Edit view branch no longer renders an inline Cancel button");

        // The Add / Save button now lives in the shared DialogDrawer's footer
        // (next to Close), NOT in the drawer body. The Edit view therefore must
        // NOT render an inline action row or an inline Add / Save button; the
        // host drawer calls the editor's public submit entry points instead.
        editViewBody.Should().NotContain("contacts-edit-form__actions",
            "the Edit view no longer renders the inline actions row — Save moved to the drawer footer");
        editViewBody.Should().NotContain("OnClick=\"AddAsync\"",
            "the Add branch no longer renders an inline Add button");
        editViewBody.Should().NotContain("SaveInlineEditAsync",
            "the Edit branch no longer renders an inline Save button");

        // Public commit entry points that the drawer footer dispatches to.
        source.Should().Contain("public async Task<bool> SubmitAddAsync()",
            "the drawer footer dispatches Add via SubmitAddAsync");
        source.Should().Contain("public async Task<bool> SubmitInlineEditAsync()",
            "the drawer footer dispatches Save via SubmitInlineEditAsync");
    }

    // ---- GuardianSection Edit view: nested contacts editor cannot open its own dialog ----
    // The GuardianSection Edit view is hosted inside the shared DialogDrawer. Its
    // nested ContactsEditor (Live for existing guardians, Buffered for drafts)
    // must NOT open a nested FluentDialog or its own drawer — the per-row Edit
    // and Delete buttons would compete with the host drawer's backdrop and
    // z-index. The Edit view therefore mirrors the Buffered editor's
    // EditDisabled="true" on the Live editor too, so the user sees the
    // contacts list (read-only triage) but cannot mutate it from inside the
    // focused editing form. Contacts management belongs on the guardian's own
    // detail page; the student-edit dialog only owns the link metadata
    // (relationship / role).

    // ---- GuardianSection Edit view: compact contact manager (no nested dialog) ----
    // The GuardianSection Edit view is hosted inside the shared DialogDrawer. It
    // used to nest a full <ContactsEditor> (Live for existing guardians, Buffered
    // for drafts) gated with EditDisabled="true" so the per-row Edit/Delete could
    // not open a nested FluentDialog inside the host drawer. That is now replaced
    // by a compact single-line contact manager (docs/plans/2026-08-18-...-
    // contact-compact.md §4.2): Draft guardians get full inline add/edit/remove/
    // reorder; existing guardians get a LiveReadOnly list + reorder only
    // (add/edit/remove/verify deferred to the guardian detail page). Either way
    // the Edit view must NOT render a nested <ContactsEditor>.

    [TestMethod]
    public void GuardianSection_EditView_NestedContactsEditorIsDisabled()
    {
        var source = ReadGuardianSectionSource();

        // Slice to the Edit-view branch (everything from the View=="Edit"
        // opening to the next `else if`).
        var editStart = source.IndexOf("else if (View == GuardianView.Edit)", StringComparison.Ordinal);
        editStart.Should().BeGreaterThan(-1, "the Edit view branch exists");
        var editEnd = source.IndexOf("else if (Mode == StudentFormFieldsMode.Linked)", editStart, StringComparison.Ordinal);
        editEnd.Should().BeGreaterThan(editStart, "the Edit view slice has a defined end");
        var editBody = source.Substring(editStart, editEnd - editStart);

        // Normalize whitespace in the slice so assertions are robust against
        // CRLF/LF drift and indent changes from automated edits. Strip Razor
        // @* ... *@ comments first so defensive NotContain scans only the
        // actual markup (the section's comments freely name the legacy
        // <ContactsEditor> for migration context).
        var noComments = System.Text.RegularExpressions.Regex.Replace(
            editBody, @"@\*.*?\*@", " ", System.Text.RegularExpressions.RegexOptions.Singleline);
        var normalized = System.Text.RegularExpressions.Regex.Replace(noComments, @"\s+", " ");

        // The Edit view must no longer nest a full <ContactsEditor> (its add-row
        // + per-row action list was the nested-dialog / cramped-drawer problem).
        normalized.Should().NotContain("<ContactsEditor",
            "the Edit view replaces the nested ContactsEditor with the compact manager");

        // Both guardian kinds route through the shared compact manager fragment:
        // drafts get the full inline set, existing guardians get reorder-only.
        // (The expected strings preserve the single space after `(` introduced
        // by the .\s+ to " " collapse — the call is multi-line in source.)
        normalized.Should().Contain(
            "RenderCompactContactManager( mode: ContactManagerMode.Draft, contactList: _editContacts, showAddAnchor: true)",
            "a draft guardian's contacts use the Draft (Buffered) compact manager with the Add-contact anchor");
        normalized.Should().Contain(
            "RenderCompactContactManager( mode: ContactManagerMode.LiveReadOnly, contactList: _liveContacts, showAddAnchor: true, liveOwnerId: gid)",
            "an existing guardian's contacts use the LiveReadOnly compact manager (list + reorder + add, edit/remove deferred)");

        // Defensive: a re-introduced bare nested ContactsEditor would silently
        // break the drawer again. Guard against both the Live and Buffered tags.
        normalized.Should().NotContain("<ContactsEditor OwnerType=\"ContactOwnerType.Guardian\"",
            "no bare existing-guardian Live ContactsEditor may reappear in the Edit view");
        normalized.Should().NotContain("<ContactsEditor Mode=\"ContactsEditor.EditorMode.Buffered\"",
            "no bare draft-guardian Buffered ContactsEditor may reappear in the Edit view");
    }

    // ---- StudentEditDialog.GetDrawerTitle: static "Edit Guardian" chrome (§4.4) ----
    // The chrome (toolbar) title for an existing-guardian edit is the static
    // "Edit Guardian" — the operator names the guardian in the body identity
    // header (name + salutation), not the toolbar. Add guardians read
    // "Add guardian". When a contact sub-screen is open the chrome names the
    // mode: "Add/Edit/Remove Guardian Contact". No BuildGuardianTitle helper
    // remains (the controller no longer prepares "Edit · {name}").

    [TestMethod]
    public void StudentEditDialog_GetDrawerTitle_ChromeIsStaticEditGuardian()
    {
        var source = ReadEditDialogSource();

        // The static title helper no longer exists — the chrome is a fixed
        // string, not a per-guardian name projection.
        source.Should().NotContain("BuildGuardianTitle",
            "no per-guardian title helper remains (body owns the name)");
        source.Should().NotContain("Edit · ",
            "the chrome no longer prefixes the guardian name");

        // The base Guardians branch (no contact sub-screen) is the static
        // string; the Add branch stays 'Add guardian'. The sub-screen titles
        // replace the base title while a contact sub-screen is open.
        source.Should().Contain("_ => _isAdd ? \"Add guardian\" : \"Edit Guardian\"",
            "the base Guardians title is static (no index/name lookup)");
        source.Should().Contain("\"Add Guardian Contact\"",
            "the chrome names the add-contact sub-screen");
        source.Should().Contain("\"Edit Guardian Contact\"",
            "the chrome names the edit-contact sub-screen");
        source.Should().Contain("\"Remove Guardian Contact\"",
            "the chrome names the remove-confirm sub-screen");

        // The four base title strings the dialog surfaces (Contacts + Guardians × Add/Edit).
        source.Should().Contain("\"Add contact\"",
            "the Contacts Add title is 'Add contact'");
        source.Should().Contain("\"Edit contact\"",
            "the Contacts Edit title is 'Edit contact'");
        source.Should().Contain("\"Add guardian\"",
            "the Guardians Add title is 'Add guardian'");
        source.Should().Contain("\"Edit Guardian\"",
            "the Guardians Edit title is 'Edit Guardian'");
    }

    // ---- DialogServiceExtensions: height is forwarded to DialogParameters.Height (§4.1) ----
    // The explicit-height open path depends on `BuildShellParameters` setting
    // `Height` on the DialogParameters it returns, and on
    // `ShowReadonlyDialogAsync` threading the `height` argument through to it.
    // Both helpers must default to null so callers that omit height keep the
    // 480px host default.

    [TestMethod]
    public void DialogServiceExtensions_BuildShellParameters_ForwardsHeightToDialogParameters()
    {
        var source = ReadDialogServiceExtensionsSource();

        // BuildShellParameters accepts a height argument and forwards it to
        // DialogParameters.Height. Default is null so other read-only dialogs
        // keep the 480px host default.
        source.Should().Contain("BuildShellParameters(string title, DialogSize size = DialogSize.Small, string? height = null)",
            "BuildShellParameters accepts an optional height (default null = 480px host default)");
        source.Should().Contain("Height = height",
            "BuildShellParameters forwards height to DialogParameters.Height (FluentUI's --dialog-height)");

        // ShowReadonlyDialogAsync threads the height argument through to
        // BuildShellParameters. The signature grows the new parameter; the
        // body passes it on.
        source.Should().Contain("ShowReadonlyDialogAsync<TComponent>(",
            "the read-only-dialog helper exists");
        source.Should().MatchRegex(
            @"public\s+static\s+async\s+Task<IDialogReference>\s+ShowReadonlyDialogAsync<TComponent>\(\s*this\s+IDialogService\s+dialogService,\s*string\s+title,",
            "the helper signature starts with the canonical (this IDialogService dialogService, string title)");
        source.Should().Contain("string? height = null",
            "the read-only helper accepts an optional height parameter");
        source.Should().Contain("BuildShellParameters(title, size, height)",
            "the read-only helper passes height through to BuildShellParameters");

        // The XML doc on height must name --dialog-height so future readers
        // see the FluentUI CSS-var mapping.
        source.Should().Contain("--dialog-height",
            "the XML doc names FluentUI's --dialog-height CSS var so the mapping is explicit");
    }

    // ---- GradeLevels/Detail: opens StudentEditDialog WITHOUT pinning a fixed height (§4.1) ----
    // The content-fill approach caps the dialog body via CSS on
    // .student-edit-dialog-root; the open call must NOT pass a height argument.
    // Other read-only dialogs on the page also keep the default (no height).

    [TestMethod]
    public void GradeLevelsDetail_OpensStudentEditDialog_WithoutPinnedHeight()
    {
        var source = ReadDetailSource();

        // The StudentEditDialog open call uses the read-only helper but does
        // NOT pass a height argument (the CSS wrapper now caps the body).
        source.Should().Contain("ShowReadonlyDialogAsync<StudentEditDialog>",
            "the page opens StudentEditDialog via the read-only helper");
        source.Should().Contain("StudentEditDialog.StudentIdKey",
            "the dialog parameter dictionary uses the StudentIdKey constant");

        // The height argument must be absent for StudentEditDialog. We assert
        // this by checking that the specific call block does not contain
        // `height:` between the StudentEditDialog call and its closing `);`.
        var callStart = source.IndexOf("ShowReadonlyDialogAsync<StudentEditDialog>", StringComparison.Ordinal);
        callStart.Should().BeGreaterThan(-1, "the StudentEditDialog call exists");
        var callEnd = source.IndexOf(");", callStart, StringComparison.Ordinal);
        callEnd.Should().BeGreaterThan(callStart, "the StudentEditDialog call has a closing );");
        var callBlock = source.Substring(callStart, callEnd - callStart);
        callBlock.Should().NotContain("height:",
            "the StudentEditDialog call must not pin a fixed height — the CSS wrapper caps the body");

        // All other read-only dialogs on the page must also NOT pass a height.
        var lines = source.Split('\n');
        foreach (var line in lines)
        {
            if (line.Contains("ShowReadonlyDialogAsync<") && !line.Contains("StudentEditDialog>"))
            {
                line.Should().NotContain("height:",
                    "non-StudentEditDialog read-only calls must not pass a height (keep the default). Offending line: " + line.Trim());
            }
        }
    }

    // ---- StudentEditDialog.razor.css: content-fill root wrapper (§4.1) ----
    // The root wrapper must be the height authority (max-height: 72vh;
    // min-height: 320px; overflow: hidden) so the absolute DialogDrawer is
    // clamped to the body. The old `height: 100%` rule is gone because the
    // FluentUI body is `height: auto` and would have resolved it to `auto`,
    // letting the drawer overshoot.

    [TestMethod]
    public void StudentEditDialog_RootWrapper_IsContentFillFlexColumn()
    {
        var css = ReadSource(
            "Students/SchoolCollab.Students.Application/Components/Students/StudentEditDialog.razor.css");

        css.Should().Contain(".student-edit-dialog-root {",
            "the positioned-root CSS rule exists");
        css.Should().Contain("position: relative;",
            "the root is the drawer's positioned containing block");
        css.Should().Contain("display: flex;",
            "the root is a flex container");
        css.Should().Contain("flex-direction: column;",
            "the root stacks its children vertically");
        css.Should().Contain("max-height: 72vh;",
            "the root caps the dialog body so it never grows off-screen");
        css.Should().Contain("min-height: 320px;",
            "the root has a floor so the bare form is not cramped");
        css.Should().Contain("overflow: hidden;",
            "the root clips internal scroll regions to the capped body");

        // The form region inside the root scrolls while the action row stays pinned.
        css.Should().Contain("form.student-form-fields--wide",
            "the StudentFormFields wide variant is targeted as the scrollable body");
        css.Should().Contain(".student-form-fields__content-stack",
            "the content stack is the scrolling region");
        css.Should().Contain(".form-actions",
            "the action row is kept as a non-scrolling footer");

        // The root-cause rule is gone.
        css.Should().NotContain("height: 100%;",
            "the old `height: 100%` rule must be removed — it caused the drawer overshoot");
    }

    // ---- GuardianSection compact manager: single-line selectable list (§4.2) ----
    // Each contact renders one .guardian-contact-line with a glyph + value +
    // optional label. No reorder buttons live inside a line (reorder is an
    // outside toolbar).

    [TestMethod]
    public void GuardianSection_CompactContactManager_RendersSingleLineSelectableList()
    {
        var source = ReadGuardianSectionSource();

        // The compact manager's list surface.
        source.Should().Contain("guardian-contact-single-lines",
            "the compact manager renders an unordered list of single contact lines");
        source.Should().Contain("guardian-contact-line",
            "each contact renders as a .guardian-contact-line (li)");
        source.Should().Contain("guardian-contact-line--selected",
            "the selected line carries the --selected modifier (click-to-select highlight)");
        source.Should().Contain("SelectContact(key)",
            "clicking a line calls SelectContact(key) to set _selectedContactKey");
        source.Should().Contain("aria-selected=\"@(selected ? \"true\" : \"false\")\"",
            "the selected state is exposed to assistive tech via aria-selected");

        // In-row content: glyph + value + optional label.
        source.Should().Contain("guardian-contact-glyph",
            "each line renders a channel glyph");
        source.Should().Contain("guardian-contact-value",
            "each line renders the formatted value");
        source.Should().Contain("guardian-contact-label",
            "each line renders an optional label (parenthesised)");

        // Defensive: no reorder buttons live inside a contact line. The
        // outside toolbar uses ChevronUp/ChevronDown — those ids must not
        // appear inside the <li> markup. Source-assert by scanning the
        // <li>...</li> block for the @foreach variable.
        // (Cheaper alternative: assert the line block does not reference
        // MoveContactUpAsync / MoveContactDownAsync.)
        var lineBlockStart = source.IndexOf("guardian-contact-line ", StringComparison.Ordinal);
        lineBlockStart.Should().BeGreaterThan(-1, "the line markup exists");
        // Find the closing </li> of the first contact line.
        var lineBlockEnd = source.IndexOf("</li>", lineBlockStart, StringComparison.Ordinal);
        lineBlockEnd.Should().BeGreaterThan(lineBlockStart, "the first </li> closes the line block");
        var lineBlock = source.Substring(lineBlockStart, lineBlockEnd - lineBlockStart);
        lineBlock.Should().NotContain("MoveContactUpAsync",
            "no reorder-up button is rendered inside a contact line");
        lineBlock.Should().NotContain("MoveContactDownAsync",
            "no reorder-down button is rendered inside a contact line");
        lineBlock.Should().NotContain("ChevronUp",
            "no reorder-up icon is rendered inside a contact line");
        lineBlock.Should().NotContain("ChevronDown",
            "no reorder-down icon is rendered inside a contact line");
    }

    // ---- GuardianSection compact manager: outside reorder toolbar (§4.2) ----
    // The reorder toolbar lives OUTSIDE the <ul>, holds two Lightweight
    // ChevronUp/ChevronDown icon buttons, and is disabled when nothing is
    // selected or the selected line is at the ends.

    [TestMethod]
    public void GuardianSection_CompactContactManager_OutsideReorderToolbar()
    {
        var source = ReadGuardianSectionSource();

        // The toolbar's class and its two buttons exist.
        source.Should().Contain("guardian-contact-reorder-bar",
            "the outside reorder toolbar has its own .guardian-contact-reorder-bar wrapper");
        source.Should().Contain("IconStart=\"@FluentIcons.ChevronUp\"",
            "the up button uses the ChevronUp icon");
        source.Should().Contain("IconStart=\"@FluentIcons.ChevronDown\"",
            "the down button uses the ChevronDown icon");
        source.Should().Contain("MoveContactUpAsync(mode, contactList, liveOwnerId)",
            "the up button calls MoveContactUpAsync with the active mode + list + live owner");
        source.Should().Contain("MoveContactDownAsync(mode, contactList, liveOwnerId)",
            "the down button calls MoveContactDownAsync with the active mode + list + live owner");

        // Disabled when nothing selected or at the ends.
        source.Should().Contain("IsContactFirst(contactList, _selectedContactKey.Value)",
            "the up button is disabled when the selected line is already first");
        source.Should().Contain("IsContactLast(contactList, _selectedContactKey.Value)",
            "the down button is disabled when the selected line is already last");

        // Defensive: the toolbar must live OUTSIDE the <ul>. Source-assert
        // by checking the .guardian-contact-reorder-bar block appears AFTER
        // the closing </ul> of the list.
        var listEnd = source.IndexOf("</ul>", StringComparison.Ordinal);
        var toolbarStart = source.IndexOf("guardian-contact-reorder-bar", StringComparison.Ordinal);
        listEnd.Should().BeGreaterThan(-1, "the contact list has a closing </ul>");
        toolbarStart.Should().BeGreaterThan(listEnd,
            "the reorder toolbar (outside the list) must appear after the list's closing </ul>");
    }

    // ---- GuardianSection compact manager: Edit/Add flip the inner switch to ContactFormFields (§4.2) ----
    // Clicking a row's Edit icon or the "Add contact" anchor sets
    // _contactEditTarget and renders <ContactFormFields> in the same inner
    // div (NOT a second DialogDrawer). A small Cancel + Add/Save row
    // commits back to the list.

    [TestMethod]
    public void GuardianSection_CompactContactManager_EditAndAddAnchorsFlipInnerSwitch()
    {
        var source = ReadGuardianSectionSource();

        // Inner switch renders ContactFormFields (not a nested DialogDrawer).
        // The slice is scoped to the Edit view (the compact manager lives
        // here only); comments are stripped so the No-DialogDrawer assertion
        // doesn't trip on the section's "the host owns <DialogDrawer>" prose.
        source.Should().Contain("_contactEditTarget is null",
            "the inner switch renders the list surface when _contactEditTarget is null");
        source.Should().Contain("<ContactFormFields Model=\"_contactEditModel\"",
            "the inner switch renders <ContactFormFields> in the same div when _contactEditTarget is set");

        // Edit/Add anchors toggle the switch.
        source.Should().Contain("StartContactEditAsync(contactList.FirstOrDefault(c => ContactKey(c) == _selectedContactKey)!)",
            "the toolbar Edit button calls StartContactEditAsync on the selected contact");
        source.Should().Contain("StartContactAddAsync",
            "the Add contact anchor calls StartContactAddAsync");
        source.Should().Contain("CommitContactAsync(mode, contactList, liveOwnerId)",
            "the inner switch's commit button calls CommitContactAsync with the active mode + list + owner");
        source.Should().Contain("CancelContactEditAsync",
            "the inner switch's cancel button calls CancelContactEditAsync");

        // The Add contact affordance is a FluentAnchor (hypertext), gated
        // by `showAddAnchor` (drafts only — spec §4.3).
        source.Should().Contain("Appearance=\"Appearance.Hypertext\"",
            "the Add contact affordance is a FluentAnchor with the Hypertext appearance");
        source.Should().Contain("guardian-contact-add-anchor",
            "the Add anchor has its own .guardian-contact-add-anchor class");

        // The Edit icon and the controls fire outside the list. The inner
        // switch is opened via the row's Edit click; selection follows the
        // row's identity so the highlight persists across cancel.
        source.Should().Contain("_selectedContactKey = ContactKey(c)",
            "opening the Edit switch for a row pins the selection to that row's key");

        // No nested DialogDrawer inside the Edit view (strip comments first so
        // the section's prose <DialogDrawer> reference doesn't trip).
        var editStart = source.IndexOf("else if (View == GuardianView.Edit)", StringComparison.Ordinal);
        editStart.Should().BeGreaterThan(-1, "the Edit view branch exists");
        var editEnd = source.IndexOf("else if (Mode == StudentFormFieldsMode.Linked)", editStart, StringComparison.Ordinal);
        editEnd.Should().BeGreaterThan(editStart, "the Edit view slice has a defined end");
        var editBody = source.Substring(editStart, editEnd - editStart);
        var noComments = System.Text.RegularExpressions.Regex.Replace(
            editBody, @"@\*.*?\*@", " ", System.Text.RegularExpressions.RegexOptions.Singleline);
        var normalized = System.Text.RegularExpressions.Regex.Replace(noComments, @"\s+", " ");
        normalized.Should().NotContain("<DialogDrawer",
            "no nested DialogDrawer is opened inside the guardian edit view");
    }

    // ---- GuardianSection compact manager: identity header above the fields (§4.4) ----
    // The Edit view renders a small identity banner (name + relationship)
    // above the compact manager so the operator can see which guardian the
    // drawer is editing, even if the dialog title is truncated.

    [TestMethod]
    public void GuardianSection_CompactContactManager_IdentityHeader()
    {
        var source = ReadGuardianSectionSource();

        // Both Edit branches (IsAdd and edit-existing) render the identity
        // header above the compact manager.
        source.Should().Contain("guardian-edit-identity",
            "the identity header has its own .guardian-edit-identity banner");
        source.Should().Contain("guardian-edit-identity-name",
            "the header renders the name in .guardian-edit-identity-name");
        source.Should().Contain("guardian-edit-identity-rel",
            "the header renders the optional relationship in .guardian-edit-identity-rel");
        source.Should().Contain("@editedGuardianDisplayName",
            "the header is data-bound to editedGuardianDisplayName");

        // The identity header must live OUTSIDE the gray .guardian-edit-form
        // container so it sits in the white drawer-body area before the darker
        // field region. On the Add surface the identity now uses the
        // .guardian-drawer-add-title row (which also carries the far-right
        // existing-guardian toggle); the edit-existing surface keeps the plain
        // .guardian-edit-identity banner. We source-assert the Add title row
        // opens before the Add form container it belongs to.
        var identityIdx = source.IndexOf("<div class=\"guardian-drawer-add-title\">", StringComparison.Ordinal);
        var formIdx = source.IndexOf("<div class=\"guardian-edit-form\">", StringComparison.Ordinal);
        identityIdx.Should().BeGreaterThan(-1, "an identity header block exists (Add title row)");
        formIdx.Should().BeGreaterThan(-1, "a .guardian-edit-form block exists");
        identityIdx.Should().BeLessThan(formIdx,
            "the Add identity title row is rendered before (outside) the .guardian-edit-form container");

        // The display name includes the salutation (spec §4.4 item 3) when
        // the working copy has a TitleCodedValueId that resolves to a
        // salutation. We assert the helper is called from the property
        // getter.
        // Use a regex-tolerant substring: the getter must reference
        // ResolveSalutation with the TitleCodedValueId.
        source.Should().Contain("ResolveSalutation(_editModel.TitleCodedValueId)",
            "the draft display name includes the salutation (spec §4.4 item 3)");
        source.Should().Contain("ResolveSalutation(g.TitleCodedValueId)",
            "the existing-guardian display name includes the salutation (spec §4.4 item 3)");

        // The CSS exists for the header (style is required for the visual
        // acceptance criterion §6 row 10).
        var css = ReadGuardianSectionCssSource();
        css.Should().Contain(".guardian-edit-identity",
            "the identity header has a CSS rule in GuardianSection.razor.css");
        css.Should().Contain(".guardian-edit-identity-name",
            "the header name span has a CSS rule");
        css.Should().Contain(".guardian-edit-identity-rel",
            "the header relationship span has a CSS rule");
    }

    // ---- GuardianSection Edit view: no inline Cancel (drawer owns Close) ----
    // The GuardianSection Edit view is hosted inside the shared DialogDrawer.
    // The drawer already exposes a Close button (DialogDrawer's ShowCancel="true"
    // CancelText="Close"), so an inline Cancel button inside the drawer body is
    // redundant — it duplicates the same affordance and competes for the same
    // action. Only the primary Save action lives in the body; Cancel comes from
    // the drawer footer (× / backdrop / Escape). This mirrors the
    // ContactsEditor Edit-view contract above.

    [TestMethod]
    public void GuardianSection_EditView_DropsInlineCancel()
    {
        var source = ReadGuardianSectionSource();

        // Slice the source to the Edit view branch only, so a Cancel button
        // rendered in the Full view inline panel doesn't false-pass.
        var editViewStart = source.IndexOf("else if (View == GuardianView.Edit)", StringComparison.Ordinal);
        editViewStart.Should().BeGreaterThan(-1, "the Edit view branch exists");
        var linkedViewStart = source.IndexOf("else if (Mode == StudentFormFieldsMode.Linked)", editViewStart, StringComparison.Ordinal);
        linkedViewStart.Should().BeGreaterThan(editViewStart, "the Edit view slice has a well-defined end");
        var editViewBody = source.Substring(editViewStart, linkedViewStart - editViewStart);

        editViewBody.Should().NotContain("CancelEditFormAsync",
            "the Edit view branch no longer calls CancelEditFormAsync — the drawer owns Close");
        editViewBody.Should().NotContain(">Cancel<",
            "the Edit view branch no longer renders an inline Cancel button");

        // The guardian Save now lives in the shared DialogDrawer's footer (next
        // to Close), NOT in the drawer body. The Edit view therefore must NOT
        // render an inline Save button; the host drawer dispatches to the
        // public commit methods instead.
        editViewBody.Should().NotContain("OnClick=\"SaveAddGuardianAsync\"",
            "the Add branch no longer renders an inline Save button — the drawer footer owns it");
        editViewBody.Should().NotContain("OnClick=\"SaveEditGuardianAsync\"",
            "the Edit branch no longer renders an inline Save button — the drawer footer owns it");

        // The commit methods are public so the drawer footer can dispatch to them.
        source.Should().Contain("public async Task<bool> SaveAddGuardianAsync()",
            "SaveAddGuardianAsync is public for the drawer footer dispatch");
        source.Should().Contain("public async Task<bool> SaveEditGuardianAsync()",
            "SaveEditGuardianAsync is public for the drawer footer dispatch");
    }

    // ---- GuardianSection compact manager: CSS covers the new classes (§4.2 / §10.1) ----
    // The CSS file must style the new compact-manager classes. Without
    // these, the selection highlight, reorder-bar layout, anchor spacing,
    // and identity header render unstyled.

    [TestMethod]
    public void GuardianSection_CompactContactManager_CssStylesCoverNewClasses()
    {
        var css = ReadGuardianSectionCssSource();

        // One rule per surface (selection highlight requires the --selected
        // modifier to be present alongside the base class).
        css.Should().Contain(".guardian-contact-manager",
            "the compact manager container has a layout rule");
        css.Should().Contain(".guardian-contact-single-lines",
            "the contact list has a layout rule (no bullets, tight gap)");
        css.Should().Contain(".guardian-contact-line",
            "each contact line has a layout rule (flex row, cursor pointer)");
        css.Should().Contain(".guardian-contact-line--selected",
            "the selected line has a highlight rule (accent background + border)");
        css.Should().Contain(".guardian-contact-glyph",
            "the glyph span has a sizing rule");
        css.Should().Contain(".guardian-contact-value",
            "the value span has a flex / ellipsis rule");
        css.Should().Contain(".guardian-contact-label",
            "the label span has a muted / italic rule");
        css.Should().Contain(".guardian-contact-actions",
            "the per-row actions cluster has a layout rule");
        css.Should().Contain(".guardian-contact-reorder-bar",
            "the outside reorder toolbar has a layout rule");
        css.Should().Contain(".guardian-contact-add-anchor",
            "the Add anchor has a spacing rule");
        css.Should().Contain(".guardian-contacts-empty",
            "the empty-state span has a muted / italic rule");
    }

    // ---- R6: guardian role is a CC checkbox, not a role dropdown ----
    // The spec restricts the guardian role to two states (Primary / CC) and
    // renders it as a FluentCheckbox (checked=CC, unchecked=Primary) to save
    // vertical space in the drawer and avoid a full dropdown for two values.

    [TestMethod]
    public void GuardianEditFields_RoleIsCCCheckboxNotDropdown()
    {
        var source = ReadGuardianEditFieldsSource();

        source.Should().Contain("FluentCheckbox",
            "the role is rendered as a FluentCheckbox");
        source.Should().Contain("Model.IsCC",
            "the checkbox binds to the model's IsCC convenience property");
        source.Should().Contain("guardian-role-checkbox",
            "the checkbox is wrapped in the .guardian-role-checkbox alignment class");
        source.Should().Contain("\"CC\"",
            "the checkbox is labeled CC");
        source.Should().NotContain("DropdownForEnum TEnum=\"GuardianRole\"",
            "the role is no longer a GuardianRole dropdown");
        source.Should().NotContain("@bind-SelectedValue=\"Model.Role\"",
            "the role is no longer two-way bound via a dropdown SelectedValue");

        var css = ReadGuardianSectionCssSource();
        css.Should().Contain(".guardian-role-checkbox",
            "the CSS aligns the role checkbox");
    }

    // ---- R2: dynamic relationship title binding ----
    // GuardianEditFields fires RelationshipChanged after the relationship
    // dropdown changes; GuardianSection re-renders the drawer identity header
    // (bound to _editModel.RelationshipCodedValueId) so it updates live.

    [TestMethod]
    public void GuardianEditFields_RaisesRelationshipChangedCallback()
    {
        var source = ReadGuardianEditFieldsSource();

        source.Should().Contain("@bind-SelectedId:after=\"NotifyRelationshipChanged\"",
            "the relationship dropdown fires the callback after selection changes");
        source.Should().Contain("EventCallback RelationshipChanged",
            "the component exposes a RelationshipChanged parameterless EventCallback");
    }

    [TestMethod]
    public void GuardianSection_WiresRelationshipChangedToReRenderIdentityHeader()
    {
        var source = ReadGuardianSectionSource();

        // The drawer identity header binds to the live model, not the static
        // link snapshot, so the relationship reflects current selection.
        source.Should().Contain(
            "ResolveRelName(_editModel.RelationshipCodedValueId)",
            "the identity header relationship binds to the live working-copy model (R2)");
        source.Should().Contain(
            "RelationshipChanged=\"OnGuardianRelationshipChanged\"",
            "every GuardianEditFields usage passes the re-render callback");
        source.Should().Contain(
            "private Task OnGuardianRelationshipChanged()",
            "GuardianSection defines the callback that re-renders on relationship change");
    }

    // ---- R5: contact sub-screen hides identity + relationship/role (spec §6.2) ----
    // When the operator is adding / editing a contact, the drawer body shows
    // ONLY the contact sub-screen: the identity header, the relationship+role
    // GuardianEditFields rows, and the guardian Save button all hide so the
    // contact form gets the full vertical space.

    [TestMethod]
    public void GuardianSection_ContactSubScreenKeepsIdentityHidesRelationshipRole()
    {
        var source = ReadGuardianSectionSource();

        // The identity banner is NOT gated: it stays visible during a contact
        // sub-screen so the operator still sees the guardian name + salutation
        // while adding/editing a contact. The relationship/role rows and the
        // guardian Save are what hide.
        source.Should().Contain(
            "InContactSubScreen",
            "the Edit view gates content behind the contact sub-screen guard");
        source.Should().Contain(
            "@if (!InContactSubScreen)",
            "the relationship/role + Save actions render only when no sub-screen is open");

        // Identity banner stays unconditionally visible on the Edit/Add surfaces.
        source.Should().Contain(
            "guardian-edit-identity",
            "the identity banner exists for the list surface");
        source.Should().Contain(
            "guardian-edit-identity-name",
            "the identity banner renders the guardian name");

        // The relationship/role field group and the guardian Save action each
        // hide inside a !InContactSubScreen guard.
        source.Should().Contain(
            "<GuardianEditFields Model=\"_editModel\"",
            "the relationship/role field group is rendered on the list surface");
        source.Should().Contain(
            "guardian-edit-actions",
            "the guardian Save action still exists on the list surface");

        // The inner contact sub-screen (ContactFormFields) is what fills the
        // body while _contactEditTarget is set.
        source.Should().Contain(
            "<ContactFormFields Model=\"_contactEditModel\"",
            "the contact sub-screen renders ContactFormFields");
    }

    // ---- D1: inline reason for Live edit / remove (spec §6.2, §7) ----
    // Existing-guardian (Live) contact edit and remove require an inline
    // reason field in the contact sub-screen; add needs none. The commit
    // handlers must call IContactsClient with that reason.

    [TestMethod]
    public void GuardianSection_ContactSubScreen_RemoveConfirmHasInlineReason()
    {
        var source = ReadGuardianSectionSource();

        // Remove contact" now names the chrome (drawer) title via
        // OnContactSubScreenChanged, not an in-body heading (the sub-screen
        // reclaims vertical space). The sub-screen still renders the summary
        // + inline reason + commit path.
        source.Should().NotContain("guardian-contact-subscreen-title",
            "the remove-confirm sub-screen has no in-body heading");
        source.Should().Contain("guardian-contact-subscreen",
            "the remove-confirm sub-screen uses the shared sub-screen wrapper");
        source.Should().Contain("guardian-contact-reason",
            "the sub-screen renders an inline reason field");
        source.Should().Contain("CommitContactRemoveAsync",
            "the remove-confirm commit handler exists");
        source.Should().Contain("DeleteContactAsync(cid, _contactReason!",
            "Live remove calls IContactsClient.DeleteContactAsync with the inline reason");
    }

    [TestMethod]
    public void GuardianSection_LiveEditRequiresReasonAndCallsUpdateContactAsync()
    {
        var source = ReadGuardianSectionSource();

        source.Should().Contain("A reason is required to change a contact.",
            "Live edit blocks commit on a blank reason");
        source.Should().Contain("UpdateContactAsync(cid, new UpdateContactRequest(",
            "Live edit calls IContactsClient.UpdateContactAsync");
        source.Should().Contain("AddContactAsync(new AddContactRequest(",
            "Live add calls IContactsClient.AddContactAsync (no reason needed)");
        source.Should().Contain("LoadLiveContactsAsync(gid)",
            "Live commits reload the contact list to reflect server truth");
    }

    // ---- D3/D4: contact sub-screen titles are chrome-driven (spec §6.3 / §9) ----
    // The sub-screen has NO in-body heading (it reclaims vertical space); the
    // drawer chrome title (StudentEditDialog.GetDrawerTitle) names the mode
    // instead when a contact sub-screen is open: GuardianSection reports the
    // active sub-screen up via OnContactSubScreenChanged and the chrome shows
    // "Add/Edit/Remove Guardian Contact". The guardian body identity header
    // stays visible above the sub-screen.

    [TestMethod]
    public void GuardianSection_ContactSubScreenTitlesAreChromeDriven()
    {
        var section = ReadGuardianSectionSource();
        var dialog = ReadEditDialogSource();

        // GUARDIAN_SECTION: the sub-screen has NO in-body title heading; it
        // reports the active mode up so the host can drive the chrome.
        section.Should().NotContain("guardian-contact-subscreen-title",
            "the sub-screen has no in-body heading (collapses the top space)");
        section.Should().Contain("NotifyContactSubScreenChanged",
            "the section reports the sub-screen mode up to the host");
        section.Should().Contain("OnContactSubScreenChanged",
            "the section surfaces a host-facing callback for the sub-screen mode");
        section.Should().Contain("GuardianContactSubScreen",
            "the section exposes a sub-screen mode enum");

        // HOST (StudentEditDialog): GetDrawerTitle resolves the guardians
        // branch through the reported sub-screen mode.
        dialog.Should().Contain("_guardianContactSubScreen switch",
            "the drawer title keys off the reported sub-screen mode");
        dialog.Should().Contain("\"Add Guardian Contact\"",
            "chrome shows 'Add Guardian Contact' when add is active");
        dialog.Should().Contain("\"Edit Guardian Contact\"",
            "chrome shows 'Edit Guardian Contact' when edit is active");
        dialog.Should().Contain("\"Remove Guardian Contact\"",
            "chrome shows 'Remove Guardian Contact' when remove is active");
        dialog.Should().Contain("OnContactSubScreenChanged=\"OnGuardianContactSubScreenChanged\"",
            "the dialog wires the section's sub-screen callback");
    }

    // ---- Follow-up: refresh the dialog height when a guardian contact sub-screen
    // opens so the drawer body (contact form + inline reason) fits without a
    // scrollbar. The root wrapper keeps its 320px default floor (so a bare
    // surface is not cramped) but raises it while a contact sub-screen is active;
    // the content-filled dialog box then grows to fit and re-collapses on return
    // to the guardian list. The class is driven by the same reported sub-screen
    // state as the chrome title, so it toggles on the same re-render.

    [TestMethod]
    public void StudentEditDialog_GuardianContactSubScreen_RaisesRootMinHeight()
    {
        var dialog = ReadEditDialogSource();
        var css = ReadSource(
            "Students/SchoolCollab.Students.Application/Components/Students/StudentEditDialog.razor.css");

        // MARKUP: the root wrapper applies the modifier class via the helper.
        dialog.Should().Contain("student-edit-dialog-root @RootModifierClasses",
            "the root wrapper binds the conditional modifier class");

        // HELPER: the class is applied only while a guardian contact sub-screen is
        // active (Add / Edit / Remove), not for the guardian list or other editors.
        dialog.Should().Contain("private string RootModifierClasses =>",
            "the root modifier class helper exists");
        dialog.Should().Contain("_guardianContactSubScreen != GuardianSection.GuardianContactSubScreen.None",
            "the class keys off a non-None guardian contact sub-screen");
        dialog.Should().Contain("student-edit-dialog-root--guardian-contact",
            "the helper emits the guardian-contact modifier class");

        // CSS: the modifier raises the root's min-height above the default floor,
        // but the default 320px floor and the 72vh cap are both retained.
        css.Should().Contain(".student-edit-dialog-root--guardian-contact {",
            "the guardian-contact modifier CSS rule exists");
        css.Should().Contain("min-height: 320px;",
            "the default 320px floor is retained");
        css.Should().Contain("min-height: 480px;",
            "the guardian-contact rule raises the floor so the contact form fits without a scrollbar");
        css.Should().Contain("max-height: 72vh;",
            "the 72vh cap is retained so the dialog never grows off-screen");
    }

    // ---- Drawer Add-branch: existing-guardian screen switch ----
    // docs/plans/2026-08-20-guardian-drawer-existing-guardian-selection.md.
    // The drawer Add branch offers a body-mode switch between the blank
    // new-guardian form and an existing-guardian selection surface: a far-right
    // Hypertext anchor toggles _drawerAddMode; the existing surface reuses the
    // typeahead + Contact|Student radio + relationship dropdown + role/CC.

    [TestMethod]
    public void GuardianSection_AddBranch_RendersTitleRowsAnchorToggle()
    {
        var source = ReadGuardianSectionSource();

        // The Add branch title row carries the "New guardian" identity on the
        // left and the far-right Hypertext toggle anchor.
        source.Should().Contain("guardian-drawer-add-title",
            "the Add branch wraps the identity in a dedicated title row");
        source.Should().Contain("Appearance=\"Appearance.Hypertext\"",
            "the toggle is a native hypertext anchor");
        source.Should().Contain("class=\"guardian-drawer-add-toggle\"",
            "the anchor is positioned far-right via the toggle class");
        source.Should().Contain("Select existing guardian",
            "the anchor label invites switching to the existing-guardian surface");
        source.Should().Contain("OnClick=\"ToggleDrawerAddModeAsync\"",
            "the anchor flips the drawer Add mode");
        source.Should().Contain("DrawerAddMode.NewGuardian",
            "the add mode enum is referenced in the Add branch");
        source.Should().Contain("ToggleDrawerAddModeAsync()",
            "the toggle handler is implemented");
    }

    [TestMethod]
    public void GuardianSection_Add_ExistingSelectionReusesTypeaheadAndRole()
    {
        var source = ReadGuardianSectionSource();

        // The existing-guardian surface reuses the typeahead + radio search
        // machinery already proven in the Full mode.
        source.Should().Contain("guardian-drawer-existing",
            "the existing-selection screen renders its container");
        source.Should().Contain("<FluentAutocomplete TOption=\"GuardianSearchRow\"",
            "the selection screen reuses the guardian typeahead");
        source.Should().Contain("SelectedOptionChanged=\"OnTypeaheadSelectedAsync\"",
            "a typeahead pick resolves via the shared handler");
        source.Should().Contain("OnTypeaheadSearchAsync",
            "the typeahead search uses the shared handler");
        source.Should().Contain("FluentRadioGroup",
            "the selection screen keeps the Contact | Student search radio");
        source.Should().Contain("class=\"guardian-role-checkbox\"",
            "the CC checkbox is wrapped in the alignment class");
        source.Should().Contain("@bind-Value=\"_pickedIsCC\"",
            "the CC checkbox binds the role-capture field");
    }

    [TestMethod]
    public void GuardianSection_SaveAddGuardian_BranchesOnDrawerAddMode_StaysOpen()
    {
        var source = ReadGuardianSectionSource();

        // Save dispatches to the existing-link path when the existing surface is
        // active; that path drafts with ExistingGuardianId + role and STAYS open.
        source.Should().Contain("private async Task<bool> SaveExistingGuardianLinkAsync()",
            "the existing-link commit method exists");
        source.Should().Contain("_drawerAddMode == DrawerAddMode.ExistingGuardian",
            "SaveAddGuardianAsync branches on the existing mode");
        source.Should().Contain("ExistingGuardianId: _existingGuardianId",
            "the link drafts the picked existing guardian");
        source.Should().Contain("_pickedIsCC ? GuardianRole.CC : GuardianRole.Primary",
            "the link records role (CC) alongside relationship");
        source.Should().Contain("ClearDrawerExistingSelectionState()",
            "after linking, only the per-link selection clears");
        source.Should().Contain("return false;",
            "the existing branch keeps the drawer open (post-link stay)");
    }

    [TestMethod]
    public void GuardianSection_DrawerAddMode_ResetsOnFreshAdd()
    {
        var source = ReadGuardianSectionSource();

        // Reset the mode in InitializeEditViewAsync's IsAdd branch so a fresh
        // drawer-add starts on the new-guardian surface.
        source.Should().Contain("_drawerAddMode = DrawerAddMode.NewGuardian;",
            "a fresh Add resets the drawer mode to NewGuardian");
        source.Should().Contain("ClearDrawerExistingSelectionState()",
            "toggling clears the per-link selection state");
    }

    [TestMethod]
    public void GuardianEditFields_VerticalFormRowsWithCombinedName()
    {
        var source = ReadGuardianEditFieldsSource();

        source.Should().Contain("<FormRow Label=\"Title\" Orientation=\"RowOrientation.Vertical\"",
            "the Title row stacks vertically for the narrow drawer (plan §3.1.2)");
        source.Should().Contain("Label=\"Name\" Required Orientation=\"RowOrientation.Vertical\"",
            "the Name row stacks vertically");
        // The combined Name row still carries First + Last side-by-side inputs.
        source.Should().Contain("class=\"guardian-name-field\"",
            "both Name inputs share the split-cell class");
    }
}