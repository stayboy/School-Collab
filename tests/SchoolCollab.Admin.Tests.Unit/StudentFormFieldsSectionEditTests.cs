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
        source.Should().Contain("OnAddContact=\"OpenAddContactAsync\"",
            "the Add contact anchor raises the dialog's drawer opener");
        source.Should().Contain("OnEditGuardian=\"OpenEditGuardianAsync\"",
            "per-card guardian Edit raises the dialog's drawer opener");
        source.Should().Contain("OnAddGuardian=\"OpenAddGuardianAsync\"",
            "the Add guardian anchor raises the dialog's drawer opener");

        source.Should().NotContain("OnManageContacts=\"OpenContactsEditorAsync\"",
            "the old section-level Manage contacts path is gone");
        source.Should().NotContain("OnManageGuardians=\"OpenGuardiansEditorAsync\"",
            "the old section-level Manage guardians path is gone");

        source.Should().Contain("class=\"student-edit-dialog-root\"", "the dialog content is wrapped in the positioned root");

        // Drawer title reflects which section is open and whether it's Add or Edit.
        source.Should().Contain("Title=\"@GetDrawerTitle()\"",
            "the drawer title is computed from the active editor + Add/Edit mode");
        source.Should().Contain("Edit contact", "the title is 'Edit contact' when editing an existing contact");
        source.Should().Contain("Add contact", "the title is 'Add contact' when adding a new contact");
        source.Should().Contain("Edit guardian", "the title is 'Edit guardian' when editing an existing guardian");
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

        // Channel / Country code (conditional) / Value / Label are the four
        // rows in the shared group, each a Vertical FormRow.
        var fields = ReadContactFormFieldsSource();
        fields.Should().Contain("<FormRow Label=\"Channel\" Orientation=\"RowOrientation.Vertical\">",
            "the channel field is rendered as a vertical FormRow (explicit Orientation for the narrow drawer)");
        fields.Should().Contain("<FormRow Label=\"Country code\" Orientation=\"RowOrientation.Vertical\">",
            "the country-code field is rendered as a vertical FormRow when the channel requires one");
        fields.Should().Contain("<FormRow Label=\"@ValueLabel\" Required Orientation=\"RowOrientation.Vertical\">",
            "the value field is rendered as a vertical FormRow with channel-aware label and Required");
        fields.Should().Contain("<FormRow Label=\"Label\" Orientation=\"RowOrientation.Vertical\">",
            "the optional label field is rendered as a vertical FormRow");

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

        // The action row is a small flex row that right-aligns the Add / Save
        // button. Its existence proves we render the action surface we
        // expect.
        source.Should().Contain("contacts-edit-form__actions",
            "the Add / Save button lives in a dedicated actions row");
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

    [TestMethod]
    public void GuardianSection_EditView_NestedContactsEditorIsDisabled()
    {
        var source = ReadGuardianSectionSource();

        // Slice to the Edit-view branch (everything from the View=="Edit"
        // opening to the next `else if`). The Edit branch is the only place
        // that nests a ContactsEditor inside the focused form.
        var editStart = source.IndexOf("else if (View == GuardianView.Edit)", StringComparison.Ordinal);
        editStart.Should().BeGreaterThan(-1, "the Edit view branch exists");
        var editEnd = source.IndexOf("else if (Mode == StudentFormFieldsMode.Linked)", editStart, StringComparison.Ordinal);
        editEnd.Should().BeGreaterThan(editStart, "the Edit view slice has a defined end");
        var editBody = source.Substring(editStart, editEnd - editStart);

        // Normalize whitespace in the slice so assertions are robust against
        // CRLF/LF drift and indent changes from automated edits.
        var normalized = System.Text.RegularExpressions.Regex.Replace(editBody, @"\s+", " ");

        // Both branches of the nested ContactsEditor (existing-guardian Live
        // and draft Buffered) must declare EditDisabled="true" so the per-row
        // Edit / Delete buttons are off. A bare
        //   <ContactsEditor OwnerType="ContactOwnerType.Guardian" OwnerId="@gid" />
        // would re-introduce the nested-dialog breakage.
        normalized.Should().Contain(
            "<ContactsEditor OwnerType=\"ContactOwnerType.Guardian\" OwnerId=\"@gid\" EditDisabled=\"true\" />",
            "the existing-guardian Live ContactsEditor nested in the Edit view must declare EditDisabled=\"true\" so its per-row Edit / Delete cannot open a nested FluentDialog inside the host drawer");
        normalized.Should().Contain(
            "<ContactsEditor Mode=\"ContactsEditor.EditorMode.Buffered\" OwnerType=\"ContactOwnerType.Guardian\" Contacts=\"_editContacts\" ContactsChanged=\"OnEditContactsChanged\" EditDisabled=\"true\" />",
            "the draft-guardian Buffered ContactsEditor nested in the Edit view must declare EditDisabled=\"true\" so its per-row Edit is off");

        // Defensive: the existing-guardian Live branch must NOT exist without
        // EditDisabled. A bare OwnerId="@gid" without the trailing
        // EditDisabled="true" would re-introduce the bug.
        normalized.Should().NotContain(
            "<ContactsEditor OwnerType=\"ContactOwnerType.Guardian\" OwnerId=\"@gid\" />",
            "the existing-guardian Live ContactsEditor must not be a self-closing tag without EditDisabled=\"true\"");
    }
}