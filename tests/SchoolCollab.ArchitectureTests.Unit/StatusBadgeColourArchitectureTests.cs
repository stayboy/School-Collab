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
///
/// <para><b>Known limitation, found 2026-10-27 while redesigning the student view:</b> FluentUI 5.0.1
/// ships the success rule misspelled — <c>fluent-badge[color=sucess]</c>, verified against
/// <c>microsoft.fluentui.aspnetcore.components/5.0.1</c>'s own stylesheet. So <c>BadgeColor.Success</c>
/// paints NOTHING, and every pinned "Success : Subtle" pair below renders two identical
/// <c>Filled</c> chips. This test's premise is therefore false for those sites: it proves the binding
/// is present, not that it works. The student view stopped using the pair altogether — its status is a
/// <c>.status-pill</c> whose colours are app.css tokens — which is why its entries moved to
/// <see cref="MovedToStatusPill"/>. Migrating the rest is deferred, not forgotten.</para>
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
        ("src/Settings/SchoolCollab.Settings.Application/Components/Pages/ConfigFlags/Index.razor",
            ["Color=\"@(context.IsEnabled ? BadgeColor.Success : BadgeColor.Subtle)\""]),
        ("src/Settings/SchoolCollab.Settings.Application/Components/Pages/ConfigFlagDetail.razor",
            ["Color=\"@(context.IsEnabled is true ? BadgeColor.Success : context.IsEnabled is false ? BadgeColor.Subtle : (BadgeColor?)null)\""]),

        // The six status helpers: the v4 Accent(Active)/Neutral(rest) pair, plus the two
        // single-status ones (member status, contact change kind).
        ("src/Students/SchoolCollab.Students.Application/Components/Pages/ActivityGroups/ActivityGroupDetails.razor",
            ["Color=\"@MemberStatusColor(m.Status)\"",
             "status == \"Active\" ? BadgeColor.Success : BadgeColor.Subtle"]),
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

    /// <summary>
    /// The sites that moved OFF the flattened <c>FluentBadge</c> pair and onto the shared
    /// <c>.status-pill</c> (app.css + <c>--status-*</c> tokens). They carry the same obligation the table
    /// above does — a status chip must be able to distinguish its states — through a mechanism that
    /// actually paints: the pill declares both halves of its own colour instead of asking FluentUI for a
    /// badge variant whose rule is misspelled upstream.
    /// <para>
    /// Asserted on the VARIANT CLASSES and the dot, not on the labels: the wording is free to change, the
    /// requirement that each state stays visually distinct is not.
    /// </para>
    /// </summary>
    private static readonly (string File, string[] Required)[] MovedToStatusPill =
    [
        // Student view: the enrollment status (Enrolled / Not enrolled) and the contact-change kind
        // (Added / Updated / Deleted) were the two flattened badges on this page.
        ("src/Students/SchoolCollab.Students.Application/Components/Pages/Students/Detail.razor",
            ["status-pill status-pill--success",
             "status-pill status-pill--warning",
             "status-pill status-pill--danger",
             "status-pill status-pill--neutral",
             "class=\"status-pill__dot\" aria-hidden=\"true\""]),
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

        foreach (var (file, required) in MovedToStatusPill)
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
                offenders.Add($"{file} — missing {snippet} (the .status-pill replacement for the "
                    + "flattened badge; its colours are app.css tokens and DO paint)");
            }
        }

        offenders.Should().BeEmpty(
            "the flattened Accent/Neutral status badges must carry their semantics in BadgeColor " +
            "(MigrationFluentBadge.md §Color) or, on a migrated surface, in the shared .status-pill; " +
            "dropping either restores two visually identical status chips and compiles clean");
    }

    private static string FindRepoRoot()
    {
        var asmDir = Path.GetDirectoryName(typeof(StatusBadgeColourArchitectureTests).Assembly.Location)!;
        return Path.GetFullPath(Path.Combine(asmDir, "..", "..", "..", "..", ".."));
    }
}
