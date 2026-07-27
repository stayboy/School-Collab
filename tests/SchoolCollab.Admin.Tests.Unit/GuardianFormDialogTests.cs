using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// Source-level regression tests for the edit-guardian dialog
/// (<c>GuardianFormDialog.razor</c>) after the ContactsEditor-for-creation
/// change (spec 2026-07-27). The dialog edits a guardian's name + the
/// link's relationship; it does NOT collect contacts — contacts are
/// managed on <c>GuardianDetail.razor</c> (existing guardians) or
/// captured at create time via the picker's <c>ContactsEditor
/// Mode=Buffered</c>. The dialog carries the model's <c>Contacts</c>
/// through unchanged so the wizard's in-memory draft does not lose its
/// contacts on edit.
/// </summary>
[TestClass]
public class GuardianFormDialogTests
{
    private const string ComponentPath = "GuardianFormDialog.razor";

    [TestMethod]
    public void Component_Hides_Legacy_Single_Contact_Fields()
    {
        var razor = ReadSource(ComponentPath);

        // The legacy single-contact "Preferred contact" + "Contact value"
        // rows live in GuardianFormFields behind ShowContactFields. The
        // edit dialog opts OUT (ShowContactFields=false) — contacts are
        // edited inline via the shared <ContactsEditor> (see
        // Component_Edits_Contacts_Inline_Via_ContactsEditor), not the
        // legacy single-contact rows. "Preferred contact" is also stale
        // terminology post-DisplayOrder.
        razor.Should().Contain("<GuardianFormFields Model=\"Model\" ShowContactFields=\"false\"",
            "the edit dialog hides the legacy single-contact rows — contacts are owned by <ContactsEditor>");
    }

    [TestMethod]
    public void Component_Edits_Contacts_Inline_Via_ContactsEditor()
    {
        var razor = ReadSource(ComponentPath);

        // The edit dialog embeds the shared <ContactsEditor> so the user can
        // edit the guardian's contacts without leaving the dialog. Two
        // branches by guardian persistence:
        //   - Existing guardian (Model.ExistingGuardianId set): Live mode —
        //     OwnerId=gid, persists immediately (mirrors GuardianDetail).
        //   - Wizard draft (ExistingGuardianId null): Buffered mode — bound
        //     to Model.Contacts, carried through on submit.
        razor.Should().Contain("<ContactsEditor",
            "the edit dialog embeds the shared ContactsEditor for inline contact edits");
        razor.Should().Contain("Model.ExistingGuardianId is { } gid",
            "the dialog branches on whether the guardian already exists (Live vs Buffered)");
        // Live branch (existing guardian).
        razor.Should().Contain("OwnerType=\"ContactOwnerType.Guardian\" OwnerId=\"@gid\"",
            "existing guardians edit contacts in Live mode against their persisted id");
        // Buffered branch (wizard draft).
        razor.Should().Contain("Mode=\"ContactsEditor.EditorMode.Buffered\"",
            "wizard drafts edit contacts in Buffered mode (in-memory, carried through)");
        razor.Should().Contain("Contacts=\"Model.Contacts\"",
            "Buffered mode is bound to Model.Contacts");
        razor.Should().Contain("ContactsChanged=\"OnContactsChanged\"",
            "Buffered mode wires the re-render callback");
        razor.Should().Contain("private void OnContactsChanged()",
            "the OnContactsChanged callback triggers a re-render (mirrors the picker)");
        // Subscription toggles are hidden — this dialog is for quick
        // contact/value edits, not subscription management (that's on
        // GuardianDetail).
        razor.Should().Contain("ShowSubscription=\"false\"",
            "subscription toggles are hidden in the edit dialog");
    }

    [TestMethod]
    public void SubmitAsync_Carries_Model_Contacts_Through_And_Drops_Legacy_Contact_Fields()
    {
        var razor = ReadSource(ComponentPath);

        // The wizard's per-link edit stores the returned GuardianAssignment
        // back into the in-memory draft. Previously SubmitAsync rebuilt
        // the assignment from the legacy single-contact fields
        // (ContactChannel/ContactValue/CountryCode) and left Contacts at
        // its default null — silently DROPPING the draft's contacts on
        // edit. The fix passes Contacts: model.Contacts through.
        razor.Should().Contain("Contacts: model.Contacts",
            "SubmitAsync carries the model's Contacts through so the wizard draft keeps its contacts on edit");

        // The legacy single-contact fields are no longer collected
        // (ShowContactFields=false) and must NOT be threaded into the
        // assignment — passing them would re-introduce the stale
        // single-contact path alongside Contacts.
        razor.Should().NotContain("model.ContactChannel,",
            "SubmitAsync no longer passes the legacy ContactChannel into the assignment");
        razor.Should().NotContain("model.ContactValue",
            "SubmitAsync no longer passes the legacy ContactValue into the assignment");
        razor.Should().NotContain("model.CountryCode)",
            "SubmitAsync no longer passes the legacy CountryCode into the assignment");
    }

    [TestMethod]
    public void SubmitAsync_Passes_Name_Relationship_And_Title()
    {
        var razor = ReadSource(ComponentPath);

        // The dialog edits Title / First / Last / Relationship — those
        // are still threaded into the GuardianAssignment.
        razor.Should().Contain("model.FirstName!.Trim()");
        razor.Should().Contain("model.LastName!.Trim()");
        razor.Should().Contain("model.RelationshipCodedValueId");
        razor.Should().Contain("TitleCodedValueId: model.TitleCodedValueId");
    }

    private static string ReadSource(string relativePath)
    {
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var srcPath = Path.GetFullPath(Path.Combine(
            asmDir,
            "..", "..", "..", "..", "..",
            "src", "Students", "SchoolCollab.Students.Admin",
            "Components", "Students", relativePath));
        File.Exists(srcPath).Should().BeTrue(
            $"{relativePath} should exist at '{srcPath}' — check the path resolution");
        return File.ReadAllText(srcPath);
    }
}