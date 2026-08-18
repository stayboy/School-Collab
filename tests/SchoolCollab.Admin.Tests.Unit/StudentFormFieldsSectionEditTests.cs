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
    public void ContactsEditor_UsesEmbeddedDrawerWithSaveCancelInBufferedMode()
    {
        var source = ReadContactsEditorSource();

        // Buffered mode edits the contact via an embedded SideDrawer inside
        // the dialog content; Live mode keeps the dialog because per-edit
        // audit requires a reason.
        source.Should().Contain("if (Mode == EditorMode.Live)",
            "ContactsEditor branches Live mode to the dialog");
        source.Should().Contain("Embedded=\"true\"",
            "ContactsEditor's Buffered edit drawer is Embedded (positioned inside the form content stack)");
        source.Should().Contain("SaveEditFromDrawerAsync",
            "the drawer's Save handler commits and closes");
        source.Should().Contain("SaveEditAsync",
            "the edit mutation handler exists");
        source.Should().Contain("_editDrawerOpen",
            "the edit-drawer open state is tracked");
    }

    [TestMethod]
    public void StudentFormFields_ContentStack_IsPositionedAncestorForDrawer()
    {
        var source = ReadFormFieldsSource();
        var css = ReadFormFieldsCssSource();
        var contactsCss = ReadSource(
            "SchoolCollab.Admin.Shared/Components/ContactsEditor.razor.css");
        var guardiansCss = ReadSource(
            "Students/SchoolCollab.Students.Application/Components/Students/GuardianSection.razor.css");

        // The FluentStack that wraps all form content is the containing block
        // for embedded SideDrawers, so the drawer overlays the full form content
        // in the dialog rather than just one section.
        source.Should().Contain("Class=\"student-form-fields__content-stack\"",
            "the form content FluentStack gets a class for positioning");
        css.Should().Contain(".student-form-fields__content-stack {",
            "the content-stack CSS rule exists");
        css.Should().Contain("position: relative",
            "the content stack establishes the containing block");

        // Component roots must NOT be positioned so the drawer fills the parent
        // content stack instead of the component's own bounds.
        contactsCss.Should().NotContain(".contacts-editor {\n    position: relative",
            "ContactsEditor root must not be positioned (drawer fills form content)");
        guardiansCss.Should().NotContain(".student-guardians {\n    position: relative",
            "GuardianSection root must not be positioned (drawer fills form content)");
        css.Should().NotContain(".student-form-fields__section-row {\n    position: relative",
            "section rows must not be positioned (drawer fills form content)");
    }

    [TestMethod]
    public void EditActions_UseConsistentCancelSaveOrder()
    {
        // The student edit dialog and all child edit drawers/panels share the
        // same action order: Cancel (Outline/Neutral) first, then Save
        // (Accent) second.
        var contactsSource = ReadContactsEditorSource();
        var guardiansSource = ReadGuardianSectionSource();

        // ContactsEditor: the buffered edit now lives in the embedded
        // SideDrawer. The drawer footer is rendered by the shared SideDrawer
        // component with ShowCancel=true (Cancel, Outline) then ShowSubmit=true
        // (Save, Accent) in that order. Assert the markup-side wiring rather
        // than a CSS class (the drawer footer owns the CSS).
        contactsSource.Should().Contain("ShowCancel=\"true\"",
            "ContactsEditor's edit drawer declares ShowCancel=true (Cancel first)");
        contactsSource.Should().Contain("ShowSubmit=\"true\"",
            "ContactsEditor's edit drawer declares ShowSubmit=true (Save after Cancel)");

        // GuardianSection: the edit form is also hosted in an embedded SideDrawer,
        // so the same Cancel-first/Save-second order is declared via ShowCancel
        // and ShowSubmit. The legacy inline guardian-add-actions buttons are gone.
        guardiansSource.Should().Contain("ShowCancel=\"true\"",
            "GuardianSection's edit drawer declares ShowCancel=true (Cancel first)");
        guardiansSource.Should().Contain("ShowSubmit=\"true\"",
            "GuardianSection's edit drawer declares ShowSubmit=true (Save after Cancel)");
        guardiansSource.Should().NotContain("class=\"guardian-add-actions\"",
            "GuardianSection no longer renders its own inline Cancel/Save action row");
    }

    // ── Source-level: StudentEditDialog orchestration ───────────────────────

    [TestMethod]
    public void GuardianEdit_KeepsCardsVisibleAndUsesEmbeddedSideDrawer()
    {
        var source = ReadGuardianSectionSource();

        // The embedded drawer slides over the card list; the cards themselves
        // stay rendered (the drawer's backdrop blocks interaction with them).
        source.Should().NotContain("if (_panelMode == GuardianPanelMode.Edit)",
            "the guardian card loop should not hide cards when the edit drawer is open");

        // GuardianSection uses the same embedded SideDrawer pattern as contacts.
        source.Should().Contain("<SideDrawer Embedded=\"true\"",
            "GuardianSection renders an embedded SideDrawer for edit");
        source.Should().Contain("OpenChanged=\"OnEditDrawerOpenChangedAsync\"",
            "the drawer's open state is forwarded to the section");
        source.Should().Contain("OnSubmitAsync=\"SaveEditGuardianAsync\"",
            "the drawer's Save handler commits and closes");
    }

    [TestMethod]
    public void GuardianEdit_UsesSingleRelationshipRoleRow()
    {
        var guardians = ReadGuardianSectionSource();

        // The edit panel puts Relationship and Role side-by-side on one
        // FormRow labelled "Relationship/Role" (mirrors the First/Last name
        // and DOB/Gender rows), rather than two separate stacked rows.
        guardians.Should().Contain("Label=\"Relationship/Role\"",
            "the Relationship and Role pickers share one FormRow");
        guardians.Should().Contain("<DropdownForEnum TEnum=\"GuardianRole\"",
            "the Role picker sits inside the combined Relationship/Role row");
        guardians.Should().NotContain("Label=\"Relationship\">",
            "there is no separate stacked Relationship row");
        guardians.Should().NotContain("Label=\"Role\">",
            "there is no separate stacked Role row");
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
}
