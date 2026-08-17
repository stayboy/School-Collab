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
    public void ContactsEdit_HidesSiblingSectionAndDisablesProfileAndSubmit()
    {
        var source = ReadFormFieldsSource();
        var css = ReadFormFieldsCssSource();

        // The contacts section is restyled via a CSS class on its FormRow.
        source.Should().Contain("SectionRowClass(StudentEditSection.Contacts)",
            "the contacts section uses a CSS class for the section switch");

        // Active state hides the FormRow label and shows a dedicated title.
        source.Should().Contain("Update contact",
            "the contacts edit-view section has a title");
        css.Should().Contain("student-form-fields__section-row--active ::deep .form-row-label",
            "the active section hides the FormRow label via ::deep");

        // Profile fields are disabled while a section edit is active.
        source.Should().Contain("Disabled=\"@AreProfileFieldsDisabled\"",
            "profile fields are disabled during a section edit");

        // The dialog action row stays visible but its buttons are disabled.
        source.Should().Contain("Disabled=\"@(Submitting || AreProfileFieldsDisabled)\"",
            "the dialog action buttons are disabled (not removed) during a section edit");
    }

    [TestMethod]
    public void GuardiansEdit_HidesSiblingSectionAndDisablesProfileAndSubmit()
    {
        var source = ReadFormFieldsSource();
        var css = ReadFormFieldsCssSource();
        var guardians = ReadGuardianSectionSource();

        source.Should().Contain("SectionRowClass(StudentEditSection.Guardians)",
            "the guardians section uses a CSS class for the section switch");

        // The "Update guardian" heading is rendered by the GuardianSection edit
        // panel, not by StudentFormFields (so it appears exactly once).
        source.Should().NotContain("student-form-fields__edit-title\">Update guardian</h4>",
            "StudentFormFields no longer renders a duplicate 'Update guardian' title");
        guardians.Should().Contain("guardian-add-title\">Update guardian</h4>",
            "the GuardianSection inline edit panel owns the 'Update guardian' title");

        css.Should().Contain("student-form-fields__section-row--hidden",
            "the CSS hides the inactive sibling section");
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

        // The section-edit behaviour is gated on EnableSectionEdit (default
        // false), so existing callers (Create.razor, Edit.razor, the wizard)
        // keep the always-editable form.
        source.Should().Contain("public bool EnableSectionEdit { get; set; }",
            "EnableSectionEdit defaults to false so existing callers are unchanged");
        source.Should().Contain("if (!EnableSectionEdit || ActiveEditSection == StudentEditSection.None)",
            "SectionRowClass guards the switch with EnableSectionEdit");
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
    public void ContactsEditor_UsesInlineEditWithSaveCancelInBufferedMode()
    {
        var source = ReadContactsEditorSource();

        // Buffered mode edits the contact inline; Live mode keeps the dialog
        // because per-edit audit requires a reason.
        source.Should().Contain("if (Mode == EditorMode.Live)",
            "ContactsEditor branches Live mode to the dialog");
        source.Should().Contain("await StartInlineEditAsync(row);",
            "Buffered mode starts inline editing");
        source.Should().Contain("SaveEditAsync",
            "inline edit has a Save handler");
        source.Should().Contain("CancelEditAsync",
            "inline edit has a Cancel handler");
        source.Should().Contain("contact-item--editing",
            "inline edit is rendered inside the editing list item");
    }

    [TestMethod]
    public void InlineEditActions_UseConsistentCancelSaveOrder()
    {
        var contactsSource = ReadContactsEditorSource();
        var guardiansSource = ReadGuardianSectionSource();

        // Dialog-level actions and both inline editors use the same order:
        // Cancel (Outline/Neutral) first, then Save (Accent) second.
        var contactActionIdx = contactsSource.IndexOf("class=\"contact-edit-actions\"");
        contactActionIdx.Should().BeGreaterThan(0);
        var contactSlice = contactsSource.Substring(contactActionIdx, 500);
        contactSlice.IndexOf("Cancel").Should().BeLessThan(contactSlice.IndexOf("Save"),
            "ContactsEditor inline edit must render Cancel before Save");

        var guardianActionIdx = guardiansSource.IndexOf("class=\"guardian-add-actions\"");
        guardianActionIdx.Should().BeGreaterThan(0);
        var guardianSlice = guardiansSource.Substring(guardianActionIdx, 300);
        guardianSlice.IndexOf("Cancel").Should().BeLessThan(guardianSlice.IndexOf("Save"),
            "GuardianSection inline edit must render Cancel before Save");
    }

    // ── Source-level: StudentEditDialog orchestration ───────────────────────

    [TestMethod]
    public void GuardianEdit_HidesDisplayViewOfEditedGuardian()
    {
        var source = ReadGuardianSectionSource();

        // When the inline edit panel opens, no guardian cards should remain
        // visible — the edit panel is the only view of the guardian being
        // edited (matches the focused section-edit UX used for contacts).
        source.Should().Contain("if (_panelMode == GuardianPanelMode.Edit)",
            "the guardian card loop must skip all cards when the edit panel is open");
        source.Should().NotContain("(int)g.Key != _editingIndex",
            "do not keep the edited guardian card visible above the edit panel");
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
