using System.Reflection;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// Source-level regression tests for the single-page student detail view
/// (<c>SchoolCollab.Students.Admin/Components/Pages/Students/Detail.razor</c>).
///
/// The student view was modernized to be a single scrollable page
/// (Profile, Enrollments, Guardians, Contacts) with no <c>FluentTabs</c>.
/// Adding tabs back, dropping a section, or removing the per-section
/// heading would all be silent visual regressions that don't show up in
/// the build. These checks catch that at compile/test time.
///
/// Pattern: read the .razor source from disk and assert on its content.
/// A bUnit render test would be more "true", but Detail.razor depends
/// on StudentsApiClient (a concrete class with 100+ methods, no
/// IStudentsClient interface) and TenantGate, both of which are heavy
/// to fake. Source-level assertions are the right tool for the
/// structural invariants the team actually cares about.
/// </summary>
[TestClass]
public class StudentDetailSectionsTests
{
    private static string ReadDetailSource()
    {
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var srcPath = Path.GetFullPath(Path.Combine(
            asmDir,
            "..", "..", "..", "..", "..",
            "src", "Students", "SchoolCollab.Students.Admin",
            "Components", "Pages", "Students", "Detail.razor"));
        File.Exists(srcPath).Should().BeTrue(
            $"Detail.razor should exist at '{srcPath}' — check the path resolution");
        return File.ReadAllText(srcPath);
    }

    [TestMethod]
    public void Detail_Does_Not_Use_FluentTabs()
    {
        // Regression guard for the modernization's main user constraint:
        // "Do not use tabs to view guardians. Keep all parts on one page."
        var source = ReadDetailSource();
        source.Should().NotContain("<FluentTabs", "the student view is a single-page sectioned layout; do not reintroduce tabs");
        source.Should().NotContain("<FluentTab", "the student view is a single-page sectioned layout; do not reintroduce tabs");
    }

    [TestMethod]
    public void Detail_Has_All_Four_Section_Headings()
    {
        // The four sections, in the order they should appear:
        // 1. Title row
        // 2. Profile (FluentCard with profile-grid)
        // 3. Enrollments
        // 4. Guardians
        // 5. Contacts
        var source = ReadDetailSource();
        // Use a flexible matcher: any <h3> that contains the section name.
        source.Should().MatchRegex(@"<h3>\s*Enrollments\s*</h3>", "Enrollments section heading");
        source.Should().MatchRegex(@"<h3>\s*Guardians\s*</h3>", "Guardians section heading");
        source.Should().MatchRegex(@"<h3>\s*Contacts\s*</h3>", "Contacts section heading");
        // Profile uses profile-grid, not a heading, but the section header
        // is a class="profile-grid" element.
        source.Should().Contain("class=\"profile-grid\"", "Profile section uses profile-grid layout");
    }

    [TestMethod]
    public void Detail_Enrollments_Section_Has_Three_Action_Buttons()
    {
        // The user explicitly asked for enrollment/transfer on the page.
        // The Enroll/Transfer/Withdraw buttons are the visible action.
        var source = ReadDetailSource();
        // The button labels appear on their own indented line inside
        // <FluentButton>…</FluentButton> tags. Use a regex that matches
        // the label as a stand-alone token between tag boundaries, not
        // as a prefix of "Enrollments" (the section heading).
        source.Should().MatchRegex(@">\s*Enroll\s*<", "Enroll action button label");
        source.Should().MatchRegex(@">\s*Transfer\s*<", "Transfer action button label");
        source.Should().MatchRegex(@">\s*Withdraw\s*<", "Withdraw action button label");
    }

    [TestMethod]
    public void Detail_Title_Row_Uses_Enriched_Name_Format()
    {
        // The title is the user's primary identifier on the page.
        // Format: "FirstName LastName (Gender, Age)"
        var source = ReadDetailSource();
        source.Should().Contain("TitleLine", "Detail.razor uses a TitleLine computed property");
        // Sanity check the format pattern: a property for gender, age, full name.
        source.Should().Contain("AgeFromDob", "Detail.razor computes age from DOB for the title");
    }

    [TestMethod]
    public void Detail_Embeds_Guardians_And_Contacts_Subcomponents()
    {
        // Both subcomponents must be embedded (not reimplemented). The
        // component names in the markup confirm this.
        var source = ReadDetailSource();
        source.Should().Contain("<GuardiansTab", "the Guardians section embeds the shared GuardiansTab");
        source.Should().Contain("StudentId=\"Id\"", "the embedded GuardiansTab binds to the route Id");
        source.Should().Contain("<ContactsEditor", "the Contacts section embeds the shared ContactsEditor");
        source.Should().Contain("OwnerType=\"ContactOwnerType.Student\"", "the embedded ContactsEditor uses the Student owner type");
        source.Should().Contain("OwnerId=\"@Id\"", "the embedded ContactsEditor binds to the route Id");
    }

    [TestMethod]
    public void Detail_CSS_Defines_Detail_Card_Popup_Clipping_Fix()
    {
        // The popup-clipping CSS workaround is REQUIRED because the Profile
        // FluentCard hosts FluentSelect / CodedValueDropdown descendants. If
        // someone removes it, dropdowns in any future inline control would
        // silently clip.
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var cssPath = Path.GetFullPath(Path.Combine(
            asmDir,
            "..", "..", "..", "..", "..",
            "src", "Students", "SchoolCollab.Students.Admin",
            "Components", "Pages", "Students", "Detail.razor.css"));
        File.Exists(cssPath).Should().BeTrue();
        var css = File.ReadAllText(cssPath);
        css.Should().Contain(".detail-card", "the .detail-card rule is required for the popup-clipping fix");
        css.Should().Contain("contain: none !important", "the popup-clipping fix uses contain: none !important on the FluentCard");
        css.Should().Contain("position: fixed !important", "the popup-clipping fix pins popups to the viewport");
    }

    [TestMethod]
    public void Detail_Preserves_Legacy_Layout_CSS_Classes()
    {
        // The .page-container, .title-row, .action-bar, .spinner-container
        // rules were on the prior implementation and are still in use. The
        // dialog-ui skill (§3) calls out that scoped-CSS hazards silently
        // drop rules if someone rewrites the file.
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var cssPath = Path.GetFullPath(Path.Combine(
            asmDir,
            "..", "..", "..", "..", "..",
            "src", "Students", "SchoolCollab.Students.Admin",
            "Components", "Pages", "Students", "Detail.razor.css"));
        var css = File.ReadAllText(cssPath);
        css.Should().Contain(".page-container");
        css.Should().Contain(".title-row");
        css.Should().Contain(".action-bar");
        css.Should().Contain(".spinner-container");
    }
}
