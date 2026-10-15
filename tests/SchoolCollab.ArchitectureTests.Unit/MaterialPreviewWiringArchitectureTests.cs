using System;
using System.IO;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.ArchitectureTests.Unit;

/// <summary>
/// Post-close rework/correction (round <c>media-pill-listing</c>, owner 2026-10-14): the metadata
/// dialog's media preview depends on ONE wiring line — the dialog-open site hands the media kinds the
/// list's lazy preview seam. Both ends are covered behaviourally (the list's seam has a bUnit pin; the
/// dialog's no-loader branch has one), but the glue itself has no page-level harness: the authoring
/// page's bUnit suite does not open the material dialog, and building one to observe three lines is
/// disproportionate. This guard therefore pins the glue as a SOURCE fact, in the same spirit as this
/// project's other source-inspection guards — it proves the wiring exists and stays inside its media
/// guard, not that it runs.
/// </summary>
[TestClass]
public class MaterialPreviewWiringArchitectureTests
{
    private static readonly string AuthoringPage = Path.Combine(
        FindRepoRoot(), "src", "Assignments", "SchoolCollab.Assignments.Application",
        "Components", "Pages", "Assignments", "AssignmentAuthoring.razor");

    /// <summary>The dialog-open site wires the preview seam, and only for the media kinds — a Text or
    /// Link row must not be handed a loader it can never use (the dialog renders no toggle outside the
    /// media branch, but the wiring should not imply otherwise).</summary>
    [TestMethod]
    public void MaterialDialogOpenSite_WiresThePreviewSeam_InsideTheMediaGuard()
    {
        var source = File.ReadAllText(AuthoringPage);

        source.Should().Contain("model.PreviewLoader = _instructionList?.GetPreviewLoader(row)",
            "the media kinds' preview toggle exists only if the host hands the dialog the list's seam");

        var guardIndex = source.IndexOf("if (row.HasMedia)", StringComparison.Ordinal);
        var wiringIndex = source.IndexOf("model.PreviewLoader =", StringComparison.Ordinal);
        guardIndex.Should().BeGreaterThan(-1, "the wiring must sit behind an explicit media guard");
        wiringIndex.Should().BeGreaterThan(guardIndex,
            "…and the wiring must be INSIDE that guard, not beside it");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "documents", "specs")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException(
                "Could not locate the repo root (documents/specs) from " + AppContext.BaseDirectory);
    }
}