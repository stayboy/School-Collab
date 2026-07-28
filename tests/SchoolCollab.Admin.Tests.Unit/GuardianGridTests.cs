using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// Source-level regression tests for the reusable
/// <c>GuardianGrid.razor</c> component (spec §4.7 / goal #5). The grid
/// pre-binds the 4-column picker and 7-column linked column structures
/// for guardian rows so the picker (search results) and the per-student
/// list share the same column ordering, header titles, and cell formatting
/// — both render Name + up to 3 contact columns titled C1, C2, C3 (C1 is
/// the lowest-DisplayOrder contact), and Linked adds Primary tick and
/// Actions (Relationship is merged into the Guardian cell as
/// 'name (relationship)'). There is no per-contact star icon; the preferred
/// contact is conveyed by column order only.
///
/// The component is "dumb" about data: the parent supplies rows (already
/// projected) and the accessors that pull name / relationship / contacts /
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
            "the default mode is Picker (Name + 3 contacts, no per-row actions)");
    }

    [TestMethod]
    public void Component_Picker_Mode_Renders_Name_Plus_Three_Contact_Columns_Titled_C1_C2_C3()
    {
        var razor = ReadSource(ComponentPath);

        // Picker: Name + up to 3 contacts titled C1, C2, C3. No actions.
        // No per-contact star icon — the preferred contact is conveyed by
        // column order only (C1 is the lowest-DisplayOrder contact).
        // Missing contacts render an em-dash placeholder.
        razor.Should().Contain("Title=\"Name\"");
        razor.Should().Contain("$\"C{index + 1}\"",
            "contact columns are titled C1, C2, C3 at runtime");
        razor.Should().NotContain("contact-preferred-star",
            "the per-contact star icon was removed; preferred is conveyed by column order only");
        razor.Should().Contain("GetContacts");
        razor.Should().MatchRegex(@"for\s*\(\s*var\s+i\s*=\s*0\s*;\s*i\s*<\s*3",
            "picker mode loops 3 times to emit contact columns");
    }

    [TestMethod]
    public void Component_Linked_Mode_Renders_6_Columns_Guardian_Merged_Three_Contacts_Primary_Tick_And_Actions()
    {
        var razor = ReadSource(ComponentPath);

        // Linked: Guardian (name + relationship merged) + up to 3 contact
        // columns + Primary tick + Actions. The Relationship column is
        // GONE — relationship is rendered inside the Guardian cell as
        // `name (relationship)`. The Guardian column is titled "Guardian"
        // (was "Name"). The Primary tick uses CheckmarkCircle (consistent
        // with the live StudentGuardiansList). The contact columns are the
        // SAME loop the picker uses — titled C1, C2, C3 (C1 is the
        // lowest-DisplayOrder contact), no per-contact star icon.
        razor.Should().Contain("Title=\"Guardian\"",
            "the merged Name+Relationship column is titled 'Guardian' (was 'Name')");
        razor.Should().NotContain("Title=\"Relationship\"",
            "the standalone Relationship column is removed — relationship is merged into the Guardian cell");
        razor.Should().Contain("Title=\"Primary\"");
        razor.Should().Contain("Icons.Regular.Size20.CheckmarkCircle",
            "Primary tick uses the CheckmarkCircle icon (consistent with the live grid)");
        // The Guardian cell renders `name (relationship)` — the
        // relationship is appended in parentheses when present.
        razor.Should().Contain("$\"{name} ({rel})\"",
            "the Guardian cell renders 'name (relationship)' (relationship omitted when unknown)");
        // Picker mode still titles its Name column "Name" (unchanged).
        razor.Should().Contain("Title=\"Name\"");
        // Both Picker and Linked modes loop 3 times to emit contact columns
        // and call GetContact(row, index). Count the shared structural marker
        // to confirm both branches are present.
        var needle = "GetContact(row, index)";
        var count = razor.Split(needle, StringSplitOptions.None).Length - 1;
        count.Should().BeGreaterThanOrEqualTo(2,
            "both Picker and Linked modes loop 3 times to emit contact columns");

        // Contact column titles are C1, C2, C3 in BOTH modes (the column
        // title strings no longer differ between modes). The legacy
        // "Preferred contact" / "Contact N" titles must not come back.
        razor.Should().NotContain("\"Preferred contact\"",
            "Linked mode no longer titles C1 \"Preferred contact\" — C1/C2/C3 everywhere");
        razor.Should().NotContain("$\"Contact {index + 1}\"",
            "Linked mode no longer titles columns \"Contact N\" — C1/C2/C3 everywhere");
        // Actions column has a real title (matches the repo convention —
        // Teachers/Subjects/Index/Periods/Guardians all use Title="Actions").
        razor.Should().Contain("Title=\"Actions\"",
            "the per-row Actions column has a title (not the empty string)");
    }

    [TestMethod]
    public void Component_Linked_Name_Cell_Shows_View_All_Anchor_Only_When_More_Than_Three_Contacts()
    {
        var razor = ReadSource(ComponentPath);
        var (pickerPart, linkedPart) = SplitByModeBranch(razor);

        // The anchor lives ONLY in the Linked Name cell (requirement: it must
        // never appear in the picker). The Linked branch carries the
        // accessor, the callback, the strict > 3 gate, the CSS class, and
        // the "View all (N) contacts" label.
        linkedPart.Should().Contain("GetTotalContactCount",
            "the Linked Name cell reads GetTotalContactCount to decide whether to show the anchor");
        linkedPart.Should().Contain("OnViewAllContacts",
            "the Linked Name cell raises OnViewAllContacts when the anchor is clicked");
        // Strict greater-than-3 — the anchor is shown ONLY when the guardian
        // has MORE than 3 contacts (not at 3, not at 2). This is the core
        // invariant the user asked to lock in.
        linkedPart.Should().Contain("totalContacts > 3",
            "the anchor is gated by a strict > 3 check (only shown when more than 3 contacts)");
        linkedPart.Should().NotContain("totalContacts > 0",
            "the gate is > 3, not a permissive > 0 (which would show the anchor at 1-3 contacts)");
        linkedPart.Should().Contain("guardian-view-all-contacts",
            "the anchor uses the guardian-view-all-contacts CSS class");
        linkedPart.Should().Contain("View all (",
            "the anchor renders the 'View all (N) contacts' label");
        // The accessor + callback are declared once in @code (outside both
        // branches); assert they exist at file scope too.
        razor.Should().Contain("public Func<TItem, int>? GetTotalContactCount",
            "GetTotalContactCount is a declared parameter");
        razor.Should().Contain("public EventCallback<TItem> OnViewAllContacts",
            "OnViewAllContacts is a declared parameter");
    }

    [TestMethod]
    public void Component_Picker_Name_Cell_Does_Not_Render_View_All_Anchor()
    {
        var razor = ReadSource(ComponentPath);
        var (pickerPart, linkedPart) = SplitByModeBranch(razor);

        // Requirement: the "View all" anchor must NEVER appear in the
        // guardian picker dialog. The Picker branch is the substring between
        // `@if (Mode == GridMode.Picker)` and the mode-level `else`; assert
        // it contains none of the anchor markers.
        pickerPart.Should().NotContain("guardian-view-all-contacts",
            "the picker Name cell does not render the view-all anchor");
        pickerPart.Should().NotContain("OnViewAllContacts",
            "the picker branch never references the OnViewAllContacts callback");
        pickerPart.Should().NotContain("View all (",
            "the picker branch does not render the 'View all (N) contacts' label");
        pickerPart.Should().NotContain("GetTotalContactCount",
            "the picker branch never reads GetTotalContactCount");
        // Positive guard: the picker Name cell is the bare single-span form
        // (distinct from the Linked Name cell's stacked .guardian-name-cell
        // div), so the anchor markup cannot have leaked into it.
        pickerPart.Should().Contain("<span>@(GetName?.Invoke(row) ?? \"\")</span>",
            "the picker Name cell is a bare span — the anchor lives in the Linked Name cell only");
    }

    [TestMethod]
    public void Component_Accepts_Row_Accessors()
    {
        var razor = ReadSource(ComponentPath);

        // The grid doesn't know the TItem shape; the parent binds the
        // accessors. Each accessor is a Func<TItem, TField> that the
        // component calls per cell. Contacts (up to 3) come via GetContacts;
        // the old single preferred-contact scalar accessors were removed.
        razor.Should().Contain("GetName");
        razor.Should().Contain("GetRelationshipName");
        razor.Should().Contain("GetContacts");
        razor.Should().Contain("GetIsPrimaryLink");
        razor.Should().Contain("GetIsEmergencyContact");
        razor.Should().NotContain("GetContactChannel",
            "the legacy single-contact scalar accessors were removed in favour of GetContacts");
        razor.Should().NotContain("FormatContactValue",
            "the legacy single-contact formatter was removed");
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
        // for CC guardians; contact columns for guardians with fewer than
        // 3 contacts).
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

    /// <summary>
    /// Splits the <c>GuardianGrid.razor</c> source into its two render branches so a
    /// test can assert that a marker (e.g. the "View all" anchor) appears in
    /// ONE branch only. The grid's column body is:
    /// <ccode>
    ///     <Columns>
    ///         @if (Mode == GridMode.Picker) { …picker… }
    ///         else                      { …linked… }
    ///     </Columns>
    /// </code>
    /// The mode-level <c>else</c> is at 8-space indentation; every nested
    /// <c>else</c> (contact/primary columns) is at 16+ spaces, so the needle
    /// <c>"\n        else\n"</c> uniquely identifies the mode split. Returns
    /// <c>(placeholderpicker, linked)</c> where <c>picker</c> spans the <c>@if</c> up to the
    /// mode <c>else</c>, and <c>linked</c> spans the <c>else</c> through end-of-file
    /// (so <c>linked</c> also includes the trailing <c></Columns></c> + <c>@code</c> —
    /// that's fine; the assertions are about markers that live in the markup
    /// branch, and the <c>@code</c> declarations are asserted separately at
    /// file scope).
    /// </summary>
    private static (string picker, string linked) SplitByModeBranch(string razor)
    {
        var ifIdx = razor.IndexOf("@if (Mode == GridMode.Picker)", StringComparison.Ordinal);
        ifIdx.Should().BeGreaterThanOrEqualTo(0,
            "the grid has a Picker/Linked mode @if branch");
        var elseNeedle = "\n        else\n";
        var elseIdx = razor.IndexOf(elseNeedle, ifIdx, StringComparison.Ordinal);
        elseIdx.Should().BeGreaterThan(ifIdx,
            "the mode branch has an else (Linked) arm at 8-space indentation");
        return (razor.Substring(ifIdx, elseIdx - ifIdx), razor.Substring(elseIdx));
    }
}