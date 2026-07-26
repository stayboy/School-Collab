using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// Source-level regression tests for the reusable
/// <c>GuardianGrid.razor</c> component (spec §4.7 / goal #5). The grid
/// pre-binds the 3-column picker and 6-column linked column structures
/// for guardian rows so the picker (search results) and the per-student
/// list share the same column ordering, header titles, and cell formatting.
///
/// The component is "dumb" about data: the parent supplies rows (already
/// projected) and the accessors that pull name / relationship / contact /
/// role / emergency from each row. This keeps the grid reusable for
/// both <c>GuardianDto</c> and <c>StudentGuardianViewDto</c>.
/// </summary>
[TestClass]
public class GuardianGridTests
{
    private const string ComponentPath = "GuardianGrid.razor";
    private const string CssPath = "GuardianGrid.razor.css";

    [TestMethod]
    public void Component_Generic_Over_Row_Type()
    {
        var razor = ReadSource(ComponentPath);

        // The grid is generic over TItem so both GuardianDto and
        // StudentGuardianViewDto (or any future row shape) can be passed
        // in without duplicating the markup.
        razor.Should().Contain("@typeparam TItem where TItem : class",
            "the grid must be generic over the row type");
    }

    [TestMethod]
    public void Component_Supports_Picker_And_Linked_Modes()
    {
        var razor = ReadSource(ComponentPath);

        razor.Should().Contain("public enum GridMode { Picker, Linked }",
            "the grid exposes a Picker / Linked mode enum");
        razor.Should().MatchRegex(@"GridMode\s*\.\s*Picker",
            "the default mode is Picker (3 columns, no per-row actions)");
    }

    [TestMethod]
    public void Component_Picker_Mode_Renders_3_Columns()
    {
        var razor = ReadSource(ComponentPath);

        // Picker: Name + Preferred contact + Contact value. No actions.
        razor.Should().Contain("Title=\"Name\"");
        razor.Should().Contain("Title=\"Preferred contact\"");
        razor.Should().Contain("Title=\"Contact value\"");
    }

    [TestMethod]
    public void Component_Linked_Mode_Renders_6_Columns_Including_Primary_Tick_And_Actions()
    {
        var razor = ReadSource(ComponentPath);

        // Linked: Name + Relationship + Preferred contact + Contact value +
        // Primary tick + Actions. The Primary tick uses CheckmarkCircle
        // (already the icon used on the live StudentGuardiansList).
        razor.Should().Contain("Title=\"Relationship\"");
        razor.Should().Contain("Title=\"Primary\"");
        razor.Should().Contain("Icons.Regular.Size20.CheckmarkCircle",
            "Primary tick uses the CheckmarkCircle icon (consistent with the live grid)");
    }

    [TestMethod]
    public void Component_Accepts_Row_Accessors()
    {
        var razor = ReadSource(ComponentPath);

        // The grid doesn't know the TItem shape; the parent binds the
        // accessors. Each accessor is a Func<TItem, TField> that the
        // component calls per cell.
        razor.Should().Contain("GetName");
        razor.Should().Contain("GetRelationshipName");
        razor.Should().Contain("GetContactChannel");
        razor.Should().Contain("GetContactValue");
        razor.Should().Contain("GetContactCountryCode");
        razor.Should().Contain("GetIsPrimaryLink");
        razor.Should().Contain("GetIsEmergencyContact");
    }

    [TestMethod]
    public void Component_Has_Empty_State_And_Name_Cell_Styles()
    {
        var css = ReadSource(CssPath);

        // The .guardian-name-cell class is the visual anchor of the
        // linked-mode Name column (name + Emergency badge on one line).
        css.Should().Contain(".guardian-name-cell",
            "the name cell class is defined in the CSS");
        // The .muted class renders em-dash placeholders (Primary column
        // for CC guardians; Contact value for guardians with no contact).
        css.Should().Contain(".muted",
            "the muted placeholder class is defined in the CSS");
    }

    [TestMethod]
    public void Component_Forwards_Selection_And_Loading_To_EntityGrid()
    {
        var razor = ReadSource(ComponentPath);

        // The grid forwards selection changes from EntityGrid back to the
        // parent using the same Dictionary<object, TItem> shape. It also
        // exposes Loading and SearchPlaceholder so the picker can forward
        // its _loading state and custom search placeholder.
        razor.Should().Contain("SelectedChanged=\"SelectedChanged\"",
            "selection changes are forwarded to EntityGrid");
        razor.Should().Contain("Loading=\"Loading\"",
            "Loading state is forwarded to EntityGrid");
        razor.Should().Contain("SearchPlaceholder=\"@SearchPlaceholder\"",
            "SearchPlaceholder is forwarded to EntityGrid");
        razor.Should().Contain("public bool Loading", "Loading parameter exists");
        razor.Should().Contain("public string? SearchPlaceholder", "SearchPlaceholder parameter exists");
    }

    [TestMethod]
    public void Component_Picker_Mode_Renders_Name_Plus_Three_Contact_Columns()
    {
        var razor = ReadSource(ComponentPath);

        // Goal #3 final layout: the picker now renders Name + up to three
        // contacts. Contact 1 is titled "Preferred contact" and gets a star
        // icon; contacts 2 and 3 are titled via string interpolation.
        razor.Should().Contain("\"Preferred contact\"",
            "first contact column is the preferred one");
        razor.Should().Contain("$\"Contact {index + 1}\"",
            "subsequent contact columns are titled Contact 2 / Contact 3 at runtime");
        razor.Should().Contain("contact-preferred-star");
        razor.Should().Contain("GetContacts");
        razor.Should().MatchRegex(@"for\s*\(\s*var\s+i\s*=\s*0\s*;\s*i\s*<\s*3",
            "picker mode loops 3 times to emit contact columns");
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