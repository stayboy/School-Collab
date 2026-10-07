using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SchoolCollab.ArchitectureTests.Unit;

/// <summary>
/// Round <c>drop-primary-grade</c> — the source-shape guards for the two UI halves the bUnit suite
/// cannot pin by itself:
///
/// <list type="number">
/// <item><b>The Add dialog is single-value + chips.</b> Every category renders ONE control whose pick
/// APPENDS to the dismissible chip row (<c>TargetsAndAudienceDialog.razor</c> + the new scoped
/// <c>.razor.css</c>). The retired markup bound <c>SelectedOptions</c> with <c>Multiple="true"</c> and an
/// inline <c>min-height: 160px</c> listbox — both are explicitly forbidden here, because a
/// multi-select is invisible to an assertion that only counts chips.</item>
/// <item><b>No assignment grade seam is left in the markup.</b> The authoring page carries no
/// "Primary grade" control, and the two question-generation sections carry no
/// <c>GradeLevelId</c> parameter (AC-9/AC-10): the round removed the grade from the AI request
/// contract, so a section that still declared the parameter would be a dead seam waiting to be
/// re-wired.</item>
/// <item><b>The dialog-result doc allows a MIXED-KIND batch.</b> The old wording promised an "always
/// homogeneous" submission; the chips carry their own category, so that promise is now wrong and the
/// page-merge comment must not repeat it.</item>
/// </list>
/// </summary>
[TestClass]
public class AssignmentGradeUiMarkupArchitectureTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static readonly string AssignmentsPages = Path.Combine(
        "src", "Assignments", "SchoolCollab.Assignments.Application", "Components", "Pages", "Assignments");

    private static string Dialog => Read(AssignmentsPages, "TargetsAndAudienceDialog.razor");
    private static string DialogCss => Read(AssignmentsPages, "TargetsAndAudienceDialog.razor.css");
    private static string Authoring => Read(AssignmentsPages, "AssignmentAuthoring.razor");

    [TestMethod]
    public void AddDialog_RendersSingleValuePickersThatAppendChips()
    {
        Dialog.Should().NotContain("Multiple=\"true\"",
            "round drop-primary-grade replaced the multi-select listboxes with single-value pickers");
        Dialog.Should().NotContain("SelectedOptions",
            "the selection is no longer HELD by the control — every pick is appended to the chip row");
        Dialog.Should().NotContain("160px",
            "the retired listbox's inline min-height is gone; the control fills its cell via CSS");
        Dialog.Should().NotContain("Style=",
            "no inline style on a control (the blazor-components rule) — the scoped class owns it");

        Dialog.Should().Contain("SelectedOptionChanged",
            "each single-value picker reports its pick through the changed callback the dialog appends on");
        Dialog.Should().Contain("AppendPicked(",
            "the pick is appended to the model's chip list — the one write point for a new chip");
        Dialog.Should().Contain("RemovePicked(entry)",
            "each chip dismisses by IDENTITY (the rendered chip's own entry), never by value");
        Dialog.Should().Contain("<Chip ",
            "the chip row renders the SHARED Chip component, not a bespoke pill");
        Dialog.Should().Contain("entity-grid-chips",
            "the chip row sits above the value control (the GuardianPickerDialog precedent)");

        DialogCss.Should().Contain(".entity-grid-chips",
            "the chip row's container styling is the dialog's own scoped CSS (CSS isolation rule)");
        DialogCss.Should().Contain(".form-select--dialog",
            "…and the single-value picker's width is a class, because an inline style is forbidden");
    }

    [TestMethod]
    public void AuthoringPageAndItsSections_CarryNoAssignmentGrade()
    {
        Authoring.Should().NotContain("GradeLevelId",
            "round drop-primary-grade removed the page's grade plumbing (AC-9)");
        Authoring.Should().NotContain("Primary grade",
            "…and its field (the retired authoring-basics-grade row)");
        Authoring.Should().NotContain("authoring-basics-grade",
            "no element id may survive for a control that no longer exists");
        Authoring.Should().NotContain("gradeLevelId:",
            "the form-model projections no longer take a grade");

        foreach (var section in new[] { "QuestionGenerationSection.razor", "QuestionsDraftSection.razor" })
        {
            Read(AssignmentsPages, section).Should().NotContain("GradeLevelId",
                $"{section} must not declare the removed parameter (AC-10) — the AI host never read it, and the "
                + "request record no longer carries the field");
        }
    }

    [TestMethod]
    public void TheDialogResultAndThePageMergeComment_AllowAMixedKindBatch()
    {
        var entry = Read(AssignmentsPages, "TargetsAndAudienceEntry.cs");

        entry.Should().Contain("mixed-kind",
            "the chips carry their own category, so a submission may mix kinds now");
        entry.Should().NotContain("always homogeneous",
            "the retired promise is wrong: entity chips survive a category switch");

        Authoring.Should().NotContain("a batch is homogeneous",
            "the page-merge comment must not repeat the retired wording");
        Authoring.Should().Contain("MIXED-KIND",
            "…and must state the rule the merge enforces: Everyone still never mixes with entity chips");
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.Combine(RepoRoot, Path.Combine(segments)));

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
