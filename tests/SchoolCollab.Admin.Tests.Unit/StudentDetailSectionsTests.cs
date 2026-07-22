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
    public void Enroll_Action_Lives_In_Section_Header()
    {
        // Enroll is the only section-level action: it always applies and
        // never needs per-row context. It belongs in the section header
        // next to the <h3>Enrollments</h3> title.
        var source = ReadDetailSource();

        // Locate the Enrollments section header. The Enroll button's label
        // ("Enroll") is the content of a <FluentButton>; the text appears
        // on its own line between the opening tag's `>` and the closing
        // `</FluentButton>`. We assert by structural position rather than
        // by exact substring so the test survives whitespace tweaks.
        var headerStart = source.IndexOf("<h3>Enrollments</h3>", StringComparison.Ordinal);
        headerStart.Should().BeGreaterThan(-1, "Enrollments section header exists");

        // Slice from the header onward, then find the first <FluentDataGrid>
        // and the first <FluentMessageBar> — the section ends at the
        // earlier of the two. The Enroll button must come before that.
        var afterHeader = source.Substring(headerStart);
        var firstEnroll = afterHeader.IndexOf("Enroll", StringComparison.Ordinal);
        // Skip the <h3>Enrollments</h3> header itself: it contains "Enroll".
        var headerMatchLen = "<h3>Enrollments</h3>".Length;
        firstEnroll = afterHeader.IndexOf("Enroll", headerMatchLen, StringComparison.Ordinal);
        firstEnroll.Should().BeGreaterThan(-1, "Enroll button label appears after the section header");

        var firstGrid = afterHeader.IndexOf("<FluentDataGrid", StringComparison.Ordinal);
        var firstMsgBar = afterHeader.IndexOf("<FluentMessageBar", StringComparison.Ordinal);
        var nextSection = firstGrid >= 0 && firstMsgBar >= 0 ? Math.Min(firstGrid, firstMsgBar)
                       : firstGrid >= 0 ? firstGrid
                       : firstMsgBar;
        nextSection.Should().BeGreaterThan(-1, "Enrollments section has either a grid or an empty-state message bar");
        firstEnroll.Should().BeLessThan(nextSection,
            "Enroll button is in the section header, not in the grid");
    }

    [TestMethod]
    public void Transfer_And_Withdraw_Actions_Are_Per_Row()
    {
        // Transfer and Withdraw are inherently per-enrollment operations
        // (the row they affect). They live inside a TemplateColumn in the
        // grid, and the row only renders them when that row's enrollment
        // is the active one (IsActiveEnrollment predicate).
        var source = ReadDetailSource();

        // Both labels must appear in the markup.
        source.Should().Contain("Transfer", "Transfer action button label is present");
        source.Should().Contain("Withdraw", "Withdraw action button label is present");

        // They must come AFTER <FluentDataGrid> in source order (inside
        // the row template, not in the section header).
        var gridStart = source.IndexOf("<FluentDataGrid", StringComparison.Ordinal);
        gridStart.Should().BeGreaterThan(-1, "Enrollments data grid exists");
        var afterGrid = source.Substring(gridStart);

        // Both labels must appear inside the grid (and the per-row
        // OnClick handlers must be inside too).
        afterGrid.Should().Contain("OnTransferAsync", "Transfer OnClick is wired inside the grid row template");
        afterGrid.Should().Contain("OnWithdrawAsync", "Withdraw OnClick is wired inside the grid row template");
        afterGrid.Should().Contain("Transfer", "Transfer label is rendered inside the grid");
        afterGrid.Should().Contain("Withdraw", "Withdraw label is rendered inside the grid");

        // The Actions column with the per-row buttons uses the
        // IsActiveEnrollment helper — assert its presence so a future
        // regression (e.g. dropping the gate) would be caught.
        source.Should().Contain("IsActiveEnrollment", "Per-row actions are gated by IsActiveEnrollment");
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
    public void Profile_Demographics_Are_Stacked_Top_Down()
    {
        // The user asked for a "top-down display" of demographics:
        // label on top, value below (a stat-card / metric-card pattern).
        // Asserted via the scoped CSS: .profile-row is column-direction,
        // .profile-label is the small uppercase hint, .profile-value is
        // the prominent value.
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var cssPath = Path.GetFullPath(Path.Combine(
            asmDir,
            "..", "..", "..", "..", "..",
            "src", "Students", "SchoolCollab.Students.Admin",
            "Components", "Pages", "Students", "Detail.razor.css"));
        File.Exists(cssPath).Should().BeTrue();
        var css = File.ReadAllText(cssPath);

        // .profile-row must be a vertical flex container.
        var rowRule = ExtractRule(css, ".profile-row");
        rowRule.Should().NotBeNull(".profile-row is defined in the scoped CSS");
        rowRule!.Should().Contain("flex-direction: column",
            ".profile-row is column-direction (label stacked above value)");
        rowRule!.Should().Contain("align-items: flex-start",
            ".profile-row aligns its children to the start (left-aligned label/value stack)");

        // .profile-label should be small, muted, uppercase (the "caption" style).
        var labelRule = ExtractRule(css, ".profile-label");
        labelRule.Should().NotBeNull();
        labelRule!.Should().Contain("text-transform: uppercase",
            ".profile-label is uppercase so it reads as a caption above the value");
        labelRule!.Should().Contain("var(--neutral-foreground-hint)",
            ".profile-label uses the muted design-system color");

        // .profile-value should be prominent (heavier than the label).
        var valueRule = ExtractRule(css, ".profile-value");
        valueRule.Should().NotBeNull();
        valueRule!.Should().Contain("font-weight: 500",
            ".profile-value is heavier than the label (the primary content)");
    }

    private static string? ExtractRule(string css, string selector)
    {
        // Match the first rule for the given selector (e.g. ".profile-row")
        // including the opening { and its body up to the matching closing }.
        //
        // We track a "inComment" state so selector occurrences inside
        // /* ... */ comments are not matched. We also require the selector
        // to be a whole identifier (not a prefix of ".profile-row-extended")
        // and to NOT be inside a string literal (class="...profile-row...").
        var inComment = false;
        for (var i = 0; i < css.Length; i++)
        {
            // Toggle comment state on /* and */
            if (inComment)
            {
                if (i + 1 < css.Length && css[i] == '*' && css[i + 1] == '/')
                {
                    inComment = false;
                    i++; // skip the /
                }
                continue;
            }
            if (i + 1 < css.Length && css[i] == '/' && css[i + 1] == '*')
            {
                inComment = true;
                i++; // skip the *
                continue;
            }

            // Try to match the selector at this position
            if (i + selector.Length > css.Length) break;
            if (string.CompareOrdinal(css, i, selector, 0, selector.Length) != 0) continue;

            // Boundary: previous char must not extend an identifier
            if (i > 0)
            {
                var prev = css[i - 1];
                if (char.IsLetterOrDigit(prev) || prev == '-' || prev == '_') continue;
            }
            // Boundary: next char must not extend an identifier
            if (i + selector.Length < css.Length)
            {
                var after = css[i + selector.Length];
                if (char.IsLetterOrDigit(after) || after == '-' || after == '_') continue;
            }
            // Inside a string? Walk back to find the last non-whitespace
            // char; if it's a quote, the selector is inside a string.
            var backIdx = i - 1;
            while (backIdx >= 0 && (css[backIdx] == ' ' || css[backIdx] == '\t'))
                backIdx--;
            if (backIdx >= 0 && (css[backIdx] == '"' || css[backIdx] == '\''))
                continue;

            // Find this rule's { and matching }.
            var open = css.IndexOf('{', i);
            if (open < 0) return null;
            var depth = 1;
            for (var j = open + 1; j < css.Length; j++)
            {
                if (css[j] == '{') depth++;
                else if (css[j] == '}')
                {
                    depth--;
                    if (depth == 0) return css.Substring(i, j - i + 1);
                }
            }
            return null;
        }
        return null;
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
