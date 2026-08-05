using System.Reflection;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// Source-level regression tests for the student edit form
/// (<c>src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Edit.razor</c>).
///
/// The student's direct contact editor (email / SMS / WhatsApp)
/// used to live INSIDE the Profile card on the view page
/// (<c>Detail.razor</c>), but the 404 in the contacts API client
/// (<c>StudentsApiClient.ListContactsAsync</c> calling
/// <c>/students/contacts</c>) made the editor useless there.
/// Per the new information architecture, the write surface (Add /
/// Verify / Set primary / Remove) belongs on the edit form, NOT on
/// the read-only view.
///
/// These tests guard the move:
///   - the Edit form MUST embed <c>&lt;ContactsEditor
///     OwnerType="ContactOwnerType.Student" OwnerId="@Id"
///     ShowSubscription="false" /&gt;</c>
///   - the Edit form MUST sit OUTSIDE the <c>&lt;StudentFormFields&gt;</c>
///     (i.e. outside the <c>&lt;EditForm&gt;</c> / DataAnnotationsValidator
///     scope) because contact mutations are independent of the
///     <c>UpdateStudentRequest</c> model
///   - the edit form MUST have a "Direct contact" sub-section
///     heading so the editor is visually grouped
/// </summary>
[TestClass]
public class EditContactEditorTests
{
    private static string ReadEditSource()
    {
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var srcPath = Path.GetFullPath(Path.Combine(
            asmDir,
            "..", "..", "..", "..", "..",
            "src", "Students", "SchoolCollab.Students.Application",
            "Components", "Pages", "Students", "Edit.razor"));
        File.Exists(srcPath).Should().BeTrue(
            $"Edit.razor should exist at '{srcPath}'");
        return File.ReadAllText(srcPath);
    }

    [TestMethod]
    public void Edit_Embeds_Student_Contacts_Editor_With_Student_OwnerType()
    {
        // The student's direct contact editor moved from Detail.razor
        // to Edit.razor. The component must be wired with
        // OwnerType=ContactOwnerType.Student, OwnerId=@Id (the route
        // parameter), and ShowSubscription=false (students don't
        // subscribe to their own contact info).
        var source = ReadEditSource();
        source.Should().Contain("<ContactsEditor",
            "the Edit form must embed the shared ContactsEditor for the student's direct contact");
        source.Should().Contain("OwnerType=\"ContactOwnerType.Student\"",
            "the embedded ContactsEditor must use the Student owner type");
        source.Should().Contain("OwnerId=\"@Id\"",
            "the embedded ContactsEditor must bind OwnerId to the route parameter @Id");
        source.Should().Contain("ShowSubscription=\"false\"",
            "the student's contact editor must NOT show the subscription toggle (that's a guardian/teacher feature)");
    }

    [TestMethod]
    public void Edit_Contacts_Editor_Sits_Outside_StudentFormFields()
    {
        // The contact editor is NOT a property of the validated
        // StudentFormModel (Add/Remove/Verify/SetPrimary are independent
        // API calls). It must sit OUTSIDE the <StudentFormFields>
        // (which is the <EditForm> host) so the DataAnnotationsValidator
        // does not try to validate it as a form field.
        // <StudentFormFields> is rendered as a self-closing tag in
        // Edit.razor (multi-line attributes ending in "/>"). We look
        // for the last "/>" after the <StudentFormFields> opening tag.
        var source = ReadEditSource();
        var formStart = source.IndexOf("<StudentFormFields", StringComparison.Ordinal);
        formStart.Should().BeGreaterThan(-1, "the Edit form renders the shared StudentFormFields");
        var formSelfClose = source.IndexOf("/>", formStart, StringComparison.Ordinal);
        formSelfClose.Should().BeGreaterThan(-1, "the StudentFormFields tag is self-closed");
        var editorStart = source.IndexOf("<ContactsEditor", formSelfClose, StringComparison.Ordinal);
        editorStart.Should().BeGreaterThan(-1,
            "the <ContactsEditor> tag must come AFTER the </StudentFormFields .../> self-close (outside the validated form scope)");
    }

    [TestMethod]
    public void Edit_Has_Direct_Contact_Section_Heading()
    {
        // Visual grouping: a "Direct contact" heading under the form
        // so the user understands the editor is part of the student
        // record, not a separate concern. Heading sits on its own line
        // (indented) so it's easy to find and hard to accidentally
        // remove when refactoring the surrounding markup.
        var source = ReadEditSource();
        source.Should().Contain("Direct contact",
            "the Edit form has a 'Direct contact' section heading so the contact editor is visually grouped");
    }

    [TestMethod]
    public void Edit_References_Core_Domain_Namespace_For_ContactOwnerType()
    {
        // The Edit.razor file references ContactOwnerType in markup
        // (OwnerType=\"ContactOwnerType.Student\") — it must import
        // the SchoolCollab.Students.Core.Domain namespace or the
        // Razor compiler will fail. Catch the missing @using in a
        // test so the next reader knows to keep the using.
        var source = ReadEditSource();
        source.Should().Contain("@using SchoolCollab.Students.Core.Domain",
            "Edit.razor references ContactOwnerType.Student in markup and must import SchoolCollab.Students.Core.Domain");
    }
}
