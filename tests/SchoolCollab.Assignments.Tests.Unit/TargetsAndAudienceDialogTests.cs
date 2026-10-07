using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components;
using SchoolCollab.Admin.Shared.Components.Dialogs;
using SchoolCollab.Assignments.Application.Components.Pages.Assignments;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// Round <c>drop-primary-grade</c> — the "Add target &amp; audience" dialog's chip flow (AC-8),
/// mounted through the real <see cref="FluentDialogProvider"/> + <c>ShowShellDialogAsync</c>
/// pipeline. The multi-select listboxes are gone: every entity category renders ONE single-value
/// control that APPENDS its pick as a dismissible chip in the always-rendered picked-targets zone
/// above it, so a submission returns every chip — and a category change clears the zone first.
///
/// <para>Discriminating: the retired dialog bound <c>SelectedOptions</c>/<c>SelectedOptionsChanged</c>
/// with <c>Multiple="true"</c> and held the whole selection in the control, so every assertion below
/// (append-and-reset, chip dismiss, the picked-targets zone) fails against it.</para>
/// </summary>
[TestClass]
public class TargetsAndAudienceDialogTests : BunitContext
{
    private static readonly Guid GradeA = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid GradeB = Guid.Parse("00000000-0000-0000-0000-0000000000a2");
    private static readonly Guid StreamA = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
    private static readonly Guid StudentA = Guid.Parse("00000000-0000-0000-0000-0000000000c1");
    private static readonly Guid GroupA = Guid.Parse("00000000-0000-0000-0000-0000000000d1");

    private IDialogService DialogService => Services.GetRequiredService<IDialogService>();

    public TargetsAndAudienceDialogTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private sealed record OpenDialog(
        IRenderedComponent<FluentDialogProvider> Cut,
        TargetsAndAudienceDialogModel Model,
        Task<TargetsAndAudienceDialogResult?> Task);

    private OpenDialog Open(TargetsAndAudienceDialogModel? model = null)
    {
        model ??= new TargetsAndAudienceDialogModel
        {
            GradeLevelOptions = [new TargetsAndAudienceOption(GradeA.ToString(), "Grade 5"),
                                 new TargetsAndAudienceOption(GradeB.ToString(), "Grade 6")],
            StreamOptions = [new TargetsAndAudienceOption(StreamA.ToString(), "Grade 5 · Blue")],
            ActivityGroupOptions = [new TargetsAndAudienceOption(GroupA.ToString(), "Chess Club")],
            StudentSearch = (query, _) => Task.FromResult<IReadOnlyList<TargetsAndAudienceOption>>(
                [new TargetsAndAudienceOption(StudentA.ToString(), "Ada Lovelace (S1001)")])
        };

        var cut = Render<FluentDialogProvider>();
        var task = DialogService.ShowShellDialogAsync<
            TargetsAndAudienceDialog, TargetsAndAudienceDialogModel, TargetsAndAudienceDialogResult>(
            model, "Add target & audience", DialogSize.Large);

        cut.WaitForAssertion(() => cut.Find("#ta-category").Should().NotBeNull(), TimeSpan.FromSeconds(5));
        return new OpenDialog(cut, model, task);
    }

    // ── the controls themselves ────────────────────────────────────────────────

    /// <summary>The entity categories render a SINGLE-value control: no <c>Multiple="true"</c>, no
    /// multi-select listbox height, and the Students picker keeps its server search but is single-pick.</summary>
    [TestMethod]
    public void EveryEntityCategory_RendersASingleValueControl_WithTheStudentsSearchStillWired()
    {
        var dialog = Open();

        SelectCategory(dialog, TargetsAndAudienceCategory.GradeLevels);
        dialog.Cut.FindComponents<FluentSelect<TargetsAndAudienceOption>>()
            .Single(s => s.Instance.Id == "ta-grade-level").Instance.Multiple
            .Should().BeFalse("the grade picker is single-value — a pick APPENDS, it is not a held selection");
        dialog.Cut.Find("#ta-grade-level").HasAttribute("multiple").Should().BeFalse();
        dialog.Cut.Find("#ta-grade-level").GetAttribute("style").Should().BeNull(
            "the retired listbox carried an inline min-height:160px; the class owns the width now");

        SelectCategory(dialog, TargetsAndAudienceCategory.Streams);
        dialog.Cut.FindComponents<FluentSelect<TargetsAndAudienceOption>>()
            .Single(s => s.Instance.Id == "ta-stream").Instance.Multiple.Should().BeFalse();

        SelectCategory(dialog, TargetsAndAudienceCategory.ActivityGroups);
        dialog.Cut.FindComponents<FluentSelect<TargetsAndAudienceOption>>()
            .Single(s => s.Instance.Id == "ta-activity-group").Instance.Multiple.Should().BeFalse();

        SelectCategory(dialog, TargetsAndAudienceCategory.Students);
        var students = dialog.Cut.FindComponents<FluentAutocomplete<TargetsAndAudienceOption>>()
            .Single(a => a.Instance.Id == "ta-student").Instance;
        students.Multiple.Should().BeFalse("TGT-6 keeps the server search but the pick is single");
        students.OnOptionsSearch.HasDelegate.Should().BeTrue(
            "the server-search route stays wired; a pick still lands in the chip row");
    }

    // ── the picked-targets zone (always rendered) ──────────────────────────────

    /// <summary>The picked-targets zone renders with ZERO picks: it is a demarcated home carrying its
    /// own caption and a muted empty hint, and it sits ABOVE the value picker — so the author sees
    /// where a pick will land instead of meeting a row that only appears after the first pick.</summary>
    [TestMethod]
    public void PickedTargetsZone_RendersWithZeroPicks_AboveTheValuePicker()
    {
        var dialog = Open();

        var zone = dialog.Cut.Find(".picked-targets");
        zone.QuerySelector(".picked-targets__caption")!.TextContent.Trim()
            .Should().Be("Picked targets", "the zone names what it holds");
        zone.QuerySelector(".picked-targets__hint")!.TextContent.Trim()
            .Should().Be("No targets picked yet.", "the empty zone states its own empty state");
        zone.QuerySelectorAll(".chip").Should().BeEmpty("nothing is picked yet");

        var markup = dialog.Cut.Markup;
        markup.IndexOf("picked-targets", StringComparison.Ordinal)
            .Should().BeLessThan(markup.IndexOf("ta-grade-level", StringComparison.Ordinal),
                "the picked-targets zone precedes the value control");
    }

    /// <summary>The zone keeps its caption once a pick lands, and reports the empty hint again after
    /// the chip is dismissed.</summary>
    [TestMethod]
    public void PickedTargetsZone_KeepsItsCaption_AndReturnsToTheEmptyHint()
    {
        var dialog = Open();
        Pick(dialog, "ta-grade-level", TargetsAndAudienceCategory.GradeLevels, GradeA, "Grade 5");

        dialog.Cut.Find(".picked-targets__caption").TextContent.Trim().Should().Be("Picked targets");
        dialog.Cut.FindAll(".picked-targets__hint").Should().BeEmpty("a picked chip replaces the hint");

        dialog.Cut.InvokeAsync(() => dialog.Cut.FindComponents<Chip>().First().Instance.OnDismiss.InvokeAsync())
            .GetAwaiter().GetResult();

        dialog.Cut.WaitForAssertion(() =>
        {
            ChipLabels(dialog).Should().BeEmpty();
            dialog.Cut.Find(".picked-targets__hint").TextContent.Trim()
                .Should().Be("No targets picked yet.");
        }, TimeSpan.FromSeconds(5));
    }

    // ── append / reset / dismiss ───────────────────────────────────────────────

    /// <summary>Picking appends ONE chip, leaves the dialog open, resets the control to its
    /// placeholder, and a repeated pick of the same value does not mint a second chip.</summary>
    [TestMethod]
    public void Picking_AppendsOneChip_ResetsTheControl_AndADoublePickStaysOne()
    {
        var dialog = Open();
        PickingDoesNotCloseTheDialog(dialog);

        Pick(dialog, "ta-grade-level", TargetsAndAudienceCategory.GradeLevels, GradeA, "Grade 5");

        ChipLabels(dialog).Should().Equal(new[] { "Grade 5" },
            "one pick appends exactly one chip");
        dialog.Cut.FindComponents<FluentSelect<TargetsAndAudienceOption>>()
            .Single(s => s.Instance.Id == "ta-grade-level").Instance.SelectedOption
            .Should().BeNull("the picker resets to its placeholder so the next pick APPENDS");

        // The same value again: deduped by (Category, RefId) — one chip, never two.
        Pick(dialog, "ta-grade-level", TargetsAndAudienceCategory.GradeLevels, GradeA, "Grade 5");

        ChipLabels(dialog).Should().Equal(new[] { "Grade 5" },
            "a double pick must not mint a duplicate chip");

        // A different value appends a second chip.
        Pick(dialog, "ta-grade-level", TargetsAndAudienceCategory.GradeLevels, GradeB, "Grade 6");

        ChipLabels(dialog).Should().Equal(new[] { "Grade 5", "Grade 6" }, "picks accumulate in pick order");
    }

    /// <summary>Dismissing a chip removes exactly that chip — identity dismiss, so two chips whose
    /// VALUE is equal cannot remove each other (the builder's remove-action precedent).</summary>
    [TestMethod]
    public void DismissingAChip_RemovesExactlyThatChip()
    {
        var dialog = Open();
        Pick(dialog, "ta-grade-level", TargetsAndAudienceCategory.GradeLevels, GradeA, "Grade 5");
        Pick(dialog, "ta-grade-level", TargetsAndAudienceCategory.GradeLevels, GradeB, "Grade 6");

        dialog.Cut.WaitForAssertion(() => dialog.Cut.FindAll(".entity-grid-chips .chip").Should().HaveCount(2));

        // Dismiss the FIRST chip through the wiring the dialog gave it (the shared Chip's own
        // OnDismiss callback, which the dialog binds to Model.RemovePicked(entry) — the identity
        // dismiss the renderer closed over).
        dialog.Cut.InvokeAsync(() => dialog.Cut.FindComponents<Chip>()
                .First().Instance.OnDismiss.InvokeAsync())
            .GetAwaiter().GetResult();

        dialog.Cut.WaitForAssertion(() =>
            ChipLabels(dialog).Should().Equal(new[] { "Grade 6" },
                "exactly the dismissed chip goes — the other one stays"),
            TimeSpan.FromSeconds(5));
    }

    // ── the submission ─────────────────────────────────────────────────────────

    /// <summary>Submit returns EVERY chip in the zone as an entry — two picks in one category
    /// accumulate, and the submission carries both in pick order.</summary>
    [TestMethod]
    public async Task Submit_ReturnsEveryChipAsAnEntry()
    {
        var dialog = Open();

        Pick(dialog, "ta-grade-level", TargetsAndAudienceCategory.GradeLevels, GradeA, "Grade 5");
        Pick(dialog, "ta-grade-level", TargetsAndAudienceCategory.GradeLevels, GradeB, "Grade 6");

        dialog.Cut.Find("form").Submit();

        var result = await dialog.Task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().NotBeNull();
        result!.Entries.Should().HaveCount(2, "a submission carries one entry per chip — the zone holds the batch");
        result.Entries.Should().Contain(e =>
            e.Category == TargetsAndAudienceCategory.GradeLevels && e.RefId == GradeA && e.Label == "Grade 5");
        result.Entries.Should().Contain(e =>
            e.Category == TargetsAndAudienceCategory.GradeLevels && e.RefId == GradeB && e.Label == "Grade 6");
    }

    /// <summary>A category change CLEARS every picked chip: the new category is a different KIND of
    /// target, so the previous category's chips must not linger beside its picker — and its own picker
    /// still appends normally afterwards.</summary>
    [TestMethod]
    public void CategoryChange_ClearsThePickedChips()
    {
        var dialog = Open();
        Pick(dialog, "ta-grade-level", TargetsAndAudienceCategory.GradeLevels, GradeA, "Grade 5");
        ChipLabels(dialog).Should().Equal(new[] { "Grade 5" });

        SelectCategory(dialog, TargetsAndAudienceCategory.ActivityGroups);

        dialog.Model.PickedEntries.Should().BeEmpty("the category change empties the model's pick list");
        dialog.Cut.WaitForAssertion(() =>
        {
            ChipLabels(dialog).Should().BeEmpty("no chip survives the switch in the zone");
            dialog.Cut.Find(".picked-targets__hint").TextContent.Trim()
                .Should().Be("No targets picked yet.", "the zone falls back to its empty hint");
        }, TimeSpan.FromSeconds(5));

        Pick(dialog, "ta-activity-group", TargetsAndAudienceCategory.ActivityGroups, GroupA, "Chess Club");

        ChipLabels(dialog).Should().Equal(new[] { "Chess Club" },
            "the new category's picker appends into the cleared zone");
    }

    /// <summary>Re-reporting the CURRENT category is not a change: the guard in OnCategoryChanged must
    /// return early so the picked zone survives — the load-bearing guard that keeps the clear safe.</summary>
    [TestMethod]
    public void CategoryReselected_SameValue_KeepsThePickedChips()
    {
        var dialog = Open();
        Pick(dialog, "ta-grade-level", TargetsAndAudienceCategory.GradeLevels, GradeA, "Grade 5");
        ChipLabels(dialog).Should().Equal(new[] { "Grade 5" });

        SelectCategory(dialog, TargetsAndAudienceCategory.GradeLevels);

        dialog.Model.PickedEntries.Should().ContainSingle("re-selecting the current category is a no-op");
        ChipLabels(dialog).Should().Equal(new[] { "Grade 5" },
            "the same-category re-report must not clear the picked zone");
    }

    /// <summary>The Everyone category switch clears the entity chips AND still picks the checkpoint:
    /// exactly ONE picked entry remains, and it is the value-less Everyone checkpoint (TGT-2).</summary>
    [TestMethod]
    public void CategoryChange_ToEveryone_YieldsExactlyTheEveryoneCheckpoint()
    {
        var dialog = Open();
        Pick(dialog, "ta-grade-level", TargetsAndAudienceCategory.GradeLevels, GradeA, "Grade 5");
        Pick(dialog, "ta-grade-level", TargetsAndAudienceCategory.GradeLevels, GradeB, "Grade 6");

        SelectCategory(dialog, TargetsAndAudienceCategory.Everyone);

        dialog.Model.PickedEntries.Should().ContainSingle(
            "the cleared pick list holds the Everyone checkpoint and nothing else");
        dialog.Model.PickedEntries[0].Category.Should().Be(TargetsAndAudienceCategory.Everyone);
        dialog.Model.PickedEntries[0].RefId.Should().BeNull("the checkpoint is value-less");
        ChipLabels(dialog).Should().Equal(new[] { TargetsAndAudienceEntry.EveryoneLabel });
    }

    /// <summary>The model itself still accepts a MIXED-KIND batch — the chips carry their own category,
    /// which is why the dialog-result contract allows one — while the DIALOG never produces one now,
    /// because a category change clears the zone. <see cref="TargetsAndAudienceDialogModel.ClearPicked"/>
    /// empties the list.</summary>
    [TestMethod]
    public void Model_AppendPicked_AcceptsAMixedKindBatch_ClearsTheCheckpoint_AndClearPickedEmptiesIt()
    {
        var model = new TargetsAndAudienceDialogModel();
        model.PickEveryone();

        model.AppendPicked(
            TargetsAndAudienceCategory.GradeLevels, new TargetsAndAudienceOption(GradeA.ToString(), "Grade 5"));
        model.AppendPicked(
            TargetsAndAudienceCategory.ActivityGroups, new TargetsAndAudienceOption(GroupA.ToString(), "Chess Club"));

        model.PickedEntries.Should().HaveCount(2, "the model accepts chips of two kinds");
        model.PickedEntries.Should().NotContain(e => e.Category == TargetsAndAudienceCategory.Everyone,
            "an entity pick clears the Everyone checkpoint (TGT-2)");

        model.ClearPicked();

        model.PickedEntries.Should().BeEmpty("ClearPicked empties the picked set");
    }

    /// <summary>Submitting with no chip at all keeps the dialog open with the error — the same
    /// refusal the retired "select at least one value" carried, now expressed on an empty chip row.</summary>
    [TestMethod]
    public async Task Submit_WithNoChips_KeepsTheDialogOpenWithTheError()
    {
        var dialog = Open();

        dialog.Cut.Find("form").Submit();

        dialog.Task.IsCompleted.Should().BeFalse("an empty submission is refused, not closed");
        dialog.Cut.WaitForAssertion(() =>
            dialog.Cut.Markup.Should().Contain("Add at least one target.",
                "the footer's error bar names what is missing"),
            TimeSpan.FromSeconds(5));
    }

    /// <summary>Everyone is a checkpoint, not a value: switching to it clears every entity chip, and
    /// the checkpoint does not survive a further category switch either (every switch clears the zone).</summary>
    [TestMethod]
    public void EveryoneCheckpoint_ClearsTheEntityChips()
    {
        var dialog = Open();
        Pick(dialog, "ta-grade-level", TargetsAndAudienceCategory.GradeLevels, GradeA, "Grade 5");

        SelectCategory(dialog, TargetsAndAudienceCategory.Everyone);

        ChipLabels(dialog).Should().Equal(new[] { TargetsAndAudienceEntry.EveryoneLabel },
            "switching to the Everyone category IS picking the checkpoint, so the entity chips clear");

        // …and the checkpoint is cleared by the next category switch too — the zone does not carry
        // a value-less pick into a category whose picker cannot have produced it.
        SelectCategory(dialog, TargetsAndAudienceCategory.Streams);
        dialog.Cut.WaitForAssertion(() => ChipLabels(dialog).Should().BeEmpty(
            "a category change clears the picked entries — the checkpoint included"));

        Pick(dialog, "ta-stream", TargetsAndAudienceCategory.Streams, StreamA, "Grade 5 · Blue");

        ChipLabels(dialog).Should().Equal(new[] { "Grade 5 · Blue" },
            "the stream pick is the only chip the cleared zone holds");
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    /// <summary>Fails the test if the dialog closed — a pick must not submit or dismiss.</summary>
    private static void PickingDoesNotCloseTheDialog(OpenDialog dialog) =>
        dialog.Task.IsCompleted.Should().BeFalse("appending a pick never closes the dialog");

    private static void SelectCategory(OpenDialog dialog, TargetsAndAudienceCategory category)
    {
        var picker = dialog.Cut.FindComponents<FluentSelect<TargetsAndAudienceDialog.CategoryOption>>()
            .Single(s => s.Instance.Id == "ta-category");
        var option = picker.Instance.Items!.Single(o => o.Value == category);

        dialog.Cut.InvokeAsync(() => picker.Instance.SelectedOptionChanged.InvokeAsync(option))
            .GetAwaiter().GetResult();

        dialog.Cut.WaitForAssertion(() => dialog.Model.Category.Should().Be(category));
    }

    private static void Pick(
        OpenDialog dialog, string controlId, TargetsAndAudienceCategory category, Guid refId, string label)
    {
        var picker = dialog.Cut.FindComponents<FluentSelect<TargetsAndAudienceOption>>()
            .Single(s => s.Instance.Id == controlId);

        dialog.Cut.InvokeAsync(() => picker.Instance.SelectedOptionChanged.InvokeAsync(
                new TargetsAndAudienceOption(refId.ToString(), label)))
            .GetAwaiter().GetResult();

        dialog.Cut.WaitForAssertion(() => dialog.Model.PickedEntries
            .Should().Contain(e => e.Category == category && e.RefId == refId,
                $"the {label} chip must be appended"));
    }

    private static IReadOnlyList<string> ChipLabels(OpenDialog dialog) =>
        dialog.Cut.FindAll(".entity-grid-chips .chip .chip-label")
            .Select(span => span.TextContent.Trim())
            .ToList();
}
