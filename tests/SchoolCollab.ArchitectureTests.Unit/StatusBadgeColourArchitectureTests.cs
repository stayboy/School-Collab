using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.ArchitectureTests.Unit;

/// <summary>
/// Pins the eleven status badges the FluentUI v5 badge mapping flattened (Round A, 2026-10-15).
///
/// <para><c>MigrationFluentBadge.md</c> maps <c>Appearance.Accent</c> and <c>Appearance.Neutral</c>
/// both to <c>BadgeAppearance.Filled</c>, so every Active/Inactive chip — and the six status helpers
/// behind the Periods/ActivityGroups/contact-history grids — rendered identically: a status chip that
/// cannot distinguish Active from Inactive. Q24 restored the distinction in <c>BadgeColor</c> (the v5
/// home for badge colour) while leaving each badge's migrated <c>BadgeAppearance</c> untouched.</para>
///
/// <para><b>Why a source pin and not more bUnit:</b> the runtime proof for this mapping is
/// <c>PeriodsLandingGridTests.Dialog_StatusBadges_DistinguishActiveFromDraftByColour</c> and
/// <c>ActivityGroupsPageTests.DetailsPage_ActiveChips_CarryTheActiveColour</c>. The remaining sites are
/// pages whose bUnit fixtures need a full API/feature-flag scaffold; for those, this table is the pin
/// that fails if a colour is dropped again — the exact regression, since dropping a <c>Color</c>
/// binding compiles.</para>
/// </summary>
[TestClass]
public class StatusBadgeColourArchitectureTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    /// <summary>Each pinned site: the file, and the colour wiring that must be present in it.</summary>
    private static readonly (string File, string[] Required)[] PinnedSites =
    [
        // The five Active/Inactive (and Enabled/Disabled) chips whose two v4 appearances collapsed.
        ("src/Students/SchoolCollab.Students.Application/Components/Pages/ActivityGroups/ActivityGroupDetails.razor",
            ["Color=\"@(_group.IsActive ? BadgeColor.Success : BadgeColor.Subtle)\""]),
        ("src/Students/SchoolCollab.Students.Application/Components/Pages/ActivityGroups/ActivityGroups.razor",
            ["Color=\"@(context.IsActive ? BadgeColor.Success : BadgeColor.Subtle)\""]),
        ("src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Detail.razor",
            ["Color=\"@(context.IsActive ? BadgeColor.Success : BadgeColor.Subtle)\""]),
        ("src/Settings/SchoolCollab.Settings.Application/Components/Pages/ConfigFlags/Index.razor",
            ["Color=\"@(context.IsEnabled ? BadgeColor.Success : BadgeColor.Subtle)\""]),
        ("src/Settings/SchoolCollab.Settings.Application/Components/Pages/ConfigFlagDetail.razor",
            ["Color=\"@(context.IsEnabled is true ? BadgeColor.Success : context.IsEnabled is false ? BadgeColor.Subtle : (BadgeColor?)null)\""]),

        // The six status helpers: the v4 Accent(Active)/Neutral(rest) pair, plus the two
        // single-status ones (member status, contact change kind).
        ("src/Students/SchoolCollab.Students.Application/Components/Pages/ActivityGroups/ActivityGroupDetails.razor",
            ["Color=\"@MemberStatusColor(m.Status)\"",
             "status == \"Active\" ? BadgeColor.Success : BadgeColor.Subtle"]),
        ("src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Detail.razor",
            ["Color=\"@ChangeKindBadgeColor(entry.ChangeKind)\"",
             "changeKind == \"Deleted\" ? BadgeColor.Danger : BadgeColor.Subtle"]),
        ("src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/Periods.razor",
            ["Color=\"@GetStatusColor(context.Status)\"",
             "\"Active\"    => BadgeColor.Success,",
             "\"Completed\" => null,"]),
        ("src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/PeriodSubPeriodsEditor.razor",
            ["Color=\"@GetStatusColor(context.Status)\"",
             "\"Active\"    => BadgeColor.Success,",
             "\"Completed\" => null,"]),
        ("src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/SubPeriods.razor",
            ["Color=\"@GetStatusColor(context.Status)\"",
             "\"Active\"    => BadgeColor.Success,",
             "\"Completed\" => null,"]),
        ("src/Students/SchoolCollab.Students.Application/Components/Pages/Periods/SubPeriodsListDialog.razor",
            ["Color=\"@GetStatusColor(context.Status)\"",
             "\"Active\"    => BadgeColor.Success,",
             "\"Completed\" => null,"]),
    ];

    [TestMethod]
    public void FlattenedStatusBadges_CarryTheirV4SemanticsInBadgeColor()
    {
        var offenders = new List<string>();

        foreach (var (file, required) in PinnedSites)
        {
            var path = Path.Combine(RepoRoot, file.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(path))
            {
                offenders.Add($"{file} — file not found; the pinned site moved.");
                continue;
            }

            var text = File.ReadAllText(path);

            foreach (var snippet in required.Where(s => !text.Contains(s, StringComparison.Ordinal)))
            {
                offenders.Add($"{file} — missing {snippet}");
            }
        }

        offenders.Should().BeEmpty(
            "the flattened Accent/Neutral status badges must carry their semantics in BadgeColor " +
            "(MigrationFluentBadge.md §Color); dropping it restores two visually identical status chips " +
            "and compiles clean");
    }

    private static string FindRepoRoot()
    {
        var asmDir = Path.GetDirectoryName(typeof(StatusBadgeColourArchitectureTests).Assembly.Location)!;
        return Path.GetFullPath(Path.Combine(asmDir, "..", "..", "..", "..", ".."));
    }
}
