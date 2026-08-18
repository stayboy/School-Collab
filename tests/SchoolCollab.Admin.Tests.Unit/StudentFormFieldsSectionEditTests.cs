using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// Tests for the focused section-edit UX on the student edit dialog
/// (plan 2026-08-17-student-edit-dialog-section-edit.md).
///
/// The structural invariants (hiding the sibling section, disabling the
/// profile fields and the dialog action row, rendering a dedicated
/// edit-view section) are asserted at the SOURCE level by reading the .razor
/// files from disk — the same pattern as <see cref="StudentDetailSectionsTests"/>.
/// Rendering <c>StudentFormFields</c> in bUnit is heavy (it needs
/// CodedValuesApiClient, IContactsClient, IDialogService, StudentsApiClient,
/// FluentUI), so source assertions are the right tool for the invariants the
/// team cares about.
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

    private static string ReadGuardianSectionSource() => ReadSource(
        "Students/SchoolCollab.Students.Application/Components/Students/GuardianSection.razor");

    private static string ReadFormFieldsCssSource() => ReadSource(
        "Students/SchoolCollab.Students.Application/Components/Students/StudentFormFields.razor.css");

    // ── Source-level: StudentFormFields section-edit structure ──────────────

    [TestMethod]
    public void ContactsEdit_DisablesProfileAndSubmitButDoesNotHideGuardians()
    {
        var source = ReadFormFieldsSource();
        var css = ReadFormFieldsCssSource();

        // The contacts section no longer uses a CSS switch that hides siblings.
        source.Should().NotContain("SectionRowClass(StudentEditSection.Contacts)",
            "SectionRowClass should not take a section argument; sections are never hidden");
        css.Should().NotContain("student-form-fields__section-row--hidden",
            "there is no hidden modifier because both sections stay visible");
        css.Should().NotContain("student-form-fields__section-row--active",
            "there is no active-section restyling; the drawer overlays the full content");

        // Profile fields are disabled while a section edit is active.
        source.Should().Contain("Disabled=\"@AreProfileFieldsDisabled\"",
            "profile fields are disabled during a section edit");

        // The dialog action row stays visible but its buttons are disabled.
        source.Should().Contain("Disabled=\"@(Submitting || AreProfileFieldsDisabled)\"",
            "the dialog action buttons are disabled (not removed) during a section edit");
    }

    [TestMethod]
    public void GuardiansEdit_DisablesProfileAndSubmitButDoesNotHideContacts()
    {
        var source = ReadFormFieldsSource();
        var css = ReadFormFieldsCssSource();
        var guardians = ReadGuardianSectionSource();

        source.Should().NotContain("SectionRowClass(StudentEditSection.Guardians)",
            "SectionRowClass should not take a section argument; sections are never hidden");
        css.Should().NotContain("student-form-fields__section-row--hidden",
            "there is no hidden modifier because both sections stay visible");
        css.Should().NotContain("student-form-fields__section-row--active",
            "there is no active-section restyling; the drawer overlays the full content");

        // The "Update guardian" title is owned by the embedded SideDrawer in
        // GuardianSection, not by StudentFormFields.
        source.Should().NotContain("student-form-fields__edit-title\">Update guardian</h4>",
            "StudentFormFields does not render a 'Update guardian' title");
        guardians.Should().Contain("Title=\"Update guardian\"",
            "the GuardianSection edit drawer owns the 'Update guardian' title");

        source.Should().Contain("Disabled=\"@AreProfileFieldsDisabled\"",
            "profile fields are disabled during a section edit");
    }

    [TestMethod]
    public void NormalState_ShowsBothSections()
    {
        var source = ReadFormFieldsSource();

        // In the normal state both sections render side-by-side using their
        // existing components. No extra Edit buttons are added to the section
        // headers.
        source.Should().Contain("Label=\"Contacts\"",
            "the contacts section renders in the normal state");
        source.Should().Contain("Label=\"Guardians\"",
            "the guardians section renders in the normal state");
        source.Should().NotContain("StartContactsEditAsync",
            "do not add a contacts section Edit button — the component has its own");
        source.Should().NotContain("StartGuardiansEditAsync",
            "do not add a guardians section Edit button — the component has its own");
    }

    [TestMethod]
    public void SectionEdit_IsOptIn_SoExistingCallersUnchanged()
    {
        var source = ReadFormFieldsSource();

        // The drawer-based edit UX is gated on EnableSectionEdit (default false),
        // so existing callers (Create.razor, Edit.razor, the wizard) keep the
        // always-editable form. When enabled, AreProfileFieldsDisabled disables
        // profile fields and the dialog action row while a drawer is open.
        source.Should().Contain("public bool EnableSectionEdit { get; set; }",
            "EnableSectionEdit defaults to false so existing callers are unchanged");
        source.Should().Contain("AreProfileFieldsDisabled",
            "profile/action disabling is still driven by the section-edit state");
    }

    [TestMethod]
    public void ChildComponents_ReportEditingStateChanges()
    {
        var contactsSource = ReadContactsEditorSource();
        var guardiansSource = ReadGuardianSectionSource();

        contactsSource.Should().Contain("EventCallback<bool> IsEditingChanged",
            "ContactsEditor must report when inline editing starts/ends");
        contactsSource.Should().Contain("IsEditingChanged.InvokeAsync(true)",
            "ContactsEditor must report entering edit mode");
        contactsSource.Should().Contain("IsEditingChanged.InvokeAsync(false)",
            "ContactsEditor must report leaving edit mode");

        guardiansSource.Should().Contain("EventCallback<bool> IsEditingChanged",
            "GuardianSection must report when its inline edit panel opens/closes");
        guardiansSource.Should().Contain("NotifyIsEditingAsync(true)",
            "GuardianSection must report entering edit mode");
        guardiansSource.Should().Contain("NotifyIsEditingAsync(false)",
            "GuardianSection must report leaving edit mode");
    }

    [TestMethod]
    public void SideDrawer_SupportsEmbeddedMode()
    {
        var source = ReadSource(
            "SchoolCollab.Admin.Shared/Components/SideDrawer.razor");
        var css = ReadSource(
            "SchoolCollab.Admin.Shared/Components/SideDrawer.razor.css");

        source.Should().Contain("public bool Embedded { get; set; }",
            "SideDrawer exposes an Embedded parameter");
        css.Should().Contain("side-drawer-panel--embedded",
            "Embedded panel uses position: absolute");
        css.Should().Contain("side-drawer-backdrop--embedded",
            "Embedded backdrop uses position: absolute");
    }

    [TestMethod]
    public void ContactsEditor_UsesPublishUpForDialogDrawer()
    {
        var source = ReadContactsEditorSource();

        // Buffered mode edits the contact by publishing a SectionEditContext
        // up to the host (StudentFormFields → StudentEditDialog), which
        // renders it inside the shared DialogDrawer. Live mode keeps the
        // ContactChangeDialog because per-edit audit requires a reason.
        source.Should().Contain("if (Mode == EditorMode.Live)",
            "ContactsEditor branches Live mode to the dialog");
        source.Should().Contain("SectionEditContextChanged",
            "the editor publishes its edit context via SectionEditContextChanged");
        source.Should().Contain("SubmitInlineEditAsync",
            "the publish-up context wraps the editor's public Submit method");
        source.Should().Contain("CancelInlineEditAsync",
            "the publish-up context wraps the editor's public Cancel method");
        source.Should().NotContain("_editDrawerOpen",
            "the editor no longer owns an embedded drawer; the dialog owns the chrome");

        source.Should().Contain("SaveEditAsync",
            "the edit mutation handler exists");
        source.Should().Contain("_editingContactKey",
            "the edit working-copy key is tracked");
    }

    [TestMethod]
    public void StudentFormFields_HasNoPositionedAncestor_ForDrawer()
    {
        var source = ReadFormFieldsSource();
        var css = ReadFormFieldsCssSource();
        var contactsCss = ReadSource(
            "SchoolCollab.Admin.Shared/Components/ContactsEditor.razor.css");
        var guardiansCss = ReadSource(
            "Students/SchoolCollab.Students.Application/Components/Students/GuardianSection.razor.css");

        // The dialog host (.student-edit-dialog-root in StudentEditDialog)
        // is the positioned ancestor for the shared DialogDrawer. The form
        // itself does NOT add position: relative to the content stack,
        // component roots, or section rows — the dialog owns positioning.
        css.Should().NotContain(".student-form-fields__content-stack {",
            "the form does not establish its own containing block; the dialog root does");
        contactsCss.Should().NotContain(".contacts-editor {\n    position: relative",
            "ContactsEditor root must not be positioned (dialog root is the anchor)");
        guardiansCss.Should().NotContain(".student-guardians {\n    position: relative",
            "GuardianSection root must not be positioned (dialog root is the anchor)");
        css.Should().NotContain(".student-form-fields__section-row {\n    position: relative",
            "section rows must not be positioned (dialog root is the anchor)");
    }

    [TestMethod]
    public void EditActions_UseConsistentCancelSaveOrder()
    {
        // The shared DialogDrawer (hosted by StudentEditDialog) owns the
        // chrome: ShowCancel=true (Cancel, Outline) then ShowSubmit=true
        // (Save, Accent) in that order. Both ContactsEditor and
        // GuardianSection now publish their edit context up to the dialog;
        // neither renders its own drawer markup.
        var dialogSource = ReadSource(
            "SchoolCollab.Admin.Shared/Components/DialogDrawer.razor");

        dialogSource.Should().Contain("public bool ShowCancel { get; set; }",
            "DialogDrawer exposes ShowCancel");
        dialogSource.Should().Contain("public bool ShowSubmit { get; set; }",
            "DialogDrawer exposes ShowSubmit");
        dialogSource.Should().Contain("dialog-drawer-btn-cancel",
            "DialogDrawer's Cancel button class is named consistently");
        dialogSource.Should().Contain("dialog-drawer-btn-submit",
            "DialogDrawer's Submit button class is named consistently");
        dialogSource.Should().Contain("ShowCancel=\"true\"",
            "the dialog wires ShowCancel=true for the Cancel button (rendered first)");
        dialogSource.Should().Contain("ShowSubmit=\"true\"",
            "the dialog wires ShowSubmit=true for the Save button (rendered second)");

        var contactsSource = ReadContactsEditorSource();
        var guardiansSource = ReadGuardianSectionSource();
        contactsSource.Should().NotContain("SideDrawer Embedded",
            "ContactsEditor no longer renders its own embedded SideDrawer");
        guardiansSource.Should().NotContain("SideDrawer Embedded",
            "GuardianSection no longer renders its own embedded SideDrawer");
        contactsSource.Should().NotContain("class=\"guardian-add-actions\"",
            "GuardianSection no longer renders an inline Cancel/Save action row");
        var editDialogSource = ReadEditDialogSource();
        editDialogSource.Should().Contain("SubmitText=\"Save\"",
            "the dialog wires SubmitText='Save' on the shared DialogDrawer");
        contactsSource.Should().Contain("Title: \"Edit contact\"",
            "the published context title is 'Edit contact'");
        guardiansSource.Should().Contain("Title: \"Update guardian\"",
            "the published context title is 'Update guardian'");
    }

    // ── Source-level: StudentEditDialog orchestration ───────────────────────

    [TestMethod]
    public void GuardianEdit_KeepsCardsVisibleAndPublishesEditContext()
    {
        var source = ReadGuardianSectionSource();

        // GuardianSection no longer renders its own embedded drawer — the
        // shared DialogDrawer (hosted by StudentEditDialog) owns the chrome.
        // Both sections stay visible; the drawer's backdrop blocks
        // interaction with the underlying form.
        source.Should().NotContain("if (_panelMode == GuardianPanelMode.Edit)",
            "the guardian card loop should not hide cards when an edit is in progress");
        source.Should().NotContain("<SideDrawer Embedded=\"true\"",
            "GuardianSection no longer renders its own SideDrawer");
        source.Should().NotContain("GuardianPanelMode",
            "the legacy internal panel switch is gone");
        source.Should().Contain("SectionEditContextChanged",
            "GuardianSection publishes its edit context up to the host");
        source.Should().Contain("SaveEditGuardianAsync",
            "the publish-up context wraps SaveEditGuardianAsync as Submit");
        source.Should().Contain("CancelPanel",
            "the publish-up context wraps CancelPanel as Cancel");
    }

    [TestMethod]
    public void GuardianEdit_BuildsRelationshipRoleRow_InEditFragment()
    {
        var source = ReadGuardianSectionSource();

        // The Relationship + Role pair lives inside the dynamic
        // BuildEditFragment RenderFragment builder — not in static
        // markup. Assert the builder wires both pickers on a single
        // "Relationship/Role" FormRow (mirrors the First/Last name and
        // DOB/Gender rows elsewhere in the form).
        source.Should().Contain("Label", "Relationship/Role",
            "the relationship + role FormRow label appears in the edit form builder");
        source.Should().Contain("<DropdownForEnum<GuardianRole>>",
            "the Role picker sits inside the combined Relationship/Role row");
    }

    [TestMethod]
    public void StudentEditDialog_WiresSectionEditParameters()
    {
        var source = ReadEditDialogSource();

        source.Should().Contain("EnableSectionEdit=\"true\"",
            "the edit dialog opts into the focused section-edit UX");
        source.Should().Contain("ActiveEditSection=\"@_activeEditSection\"",
            "the dialog binds the active section state");
        source.Should().Contain("ActiveEditSectionChanged=\"OnActiveEditSectionChanged\"",
            "the dialog handles the section-edit state-changed callback");
    }

    [TestMethod]
    public void StudentEditDialog_DoesNotSnapshotOrAddSectionButtons()
    {
        var source = ReadEditDialogSource();

        // The dialog no longer snapshots section data or renders its own
        // section-level Save/Cancel buttons — those belong to the child
        // components.
        source.Should().NotContain("_contactsSnapshot",
            "the dialog should not snapshot contacts");
        source.Should().NotContain("_guardianLinksSnapshot",
            "the dialog should not snapshot guardian links");
        source.Should().NotContain("OnSectionEditSave",
            "the dialog should not have a section-edit save callback");
        source.Should().NotContain("OnSectionEditCancel",
            "the dialog should not have a section-edit cancel callback");
    }

    [TestMethod]
    public void StudentEditDialog_HostsSharedDialogDrawer()
    {
        var source = ReadEditDialogSource();
        var css = ReadSource(
            "Students/SchoolCollab.Students.Application/Components/Students/StudentEditDialog.razor.css");

        // The dialog owns the shared DialogDrawer (one drawer, hosted by
        // the dialog, not by the sections). The dialog wraps its body in
        // .student-edit-dialog-root which is the positioned ancestor
        // (position: relative; height: 100%) for the drawer.
        source.Should().Contain("<DialogDrawer",
            "the dialog renders a DialogDrawer");
        source.Should().Contain("Side=\"DialogDrawerSide.Right\"",
            "the drawer anchors to the right edge of the dialog body by default");
        source.Should().Contain("ShowCancel=\"true\"",
            "the drawer's Cancel button is shown");
        source.Should().Contain("ShowSubmit=\"true\"",
            "the drawer's Save button is shown");
        source.Should().Contain("SectionEditContent=\"@_sectionEditContent\"",
            "the dialog reads the active section's edit context");
        source.Should().Contain("SectionEditContentChanged=\"OnSectionEditContentChanged\"",
            "the dialog forwards child edit contexts to its own field");
        source.Should().Contain("class=\"student-edit-dialog-root\"",
            "the dialog content is wrapped in the positioned root");
        css.Should().Contain(".student-edit-dialog-root {",
            "the positioned-root CSS rule exists");
        css.Should().Contain("position: relative;",
            "the positioned root is the drawer's containing block");
        css.Should().Contain("height: 100%;",
            "the positioned root fills the dialog body height");
    }

    [TestMethod]
    public void StudentEditDialog_CancelsPreviousContextOnSectionSwap()
    {
        var source = ReadEditDialogSource();

        // Rework for the section-swap data-loss bug: if a new edit context
        // arrives while a previous one is still set (e.g. clicking Edit on a
        // contact while a guardian edit drawer is open), the previous
        // context's Cancel must run FIRST so that section tears down its
        // working copy. Otherwise its pending edits are silently lost on save.
        source.Should().Contain("await previous.Cancel();",
            "swapping sections cancels the previous edit context's working copy");
        source.Should().Contain("previous.SectionKey != ctx.SectionKey",
            "a swap is detected by a differing SectionKey, so a same-section re-publish (reactive UI update) does NOT cancel the in-flight edit");
        source.Should().Contain("OnSectionEditContentChanged(SectionEditContext? ctx)",
            "the swap-cancel lives in the dialog's section-content handler");
    }
}
