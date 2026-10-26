using Bunit;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for <see cref="RailBand"/>, <see cref="RailChips"/> and <see cref="RailChip"/> — the component
/// extracted under <c>documents/solution/component-first-policy.md</c> (Q22a), whose contract was approved from
/// the states mock <c>.pi/mock/rail-band-states.png</c> (Q24a).
///
/// What is pinned here is exactly what the mock promised, because those are the promises a future edit breaks:
/// a band always has a text label (colour never carries meaning alone), a blocked chip always states its reason
/// in visible text AND in its accessible description, a chip's ✕ only exists when a handler is supplied, and the
/// chip set is a real list rather than a run of spans.
/// </summary>
[TestClass]
public class RailBandComponentTests : BunitContext
{
    [TestMethod]
    public void RailBand_RendersItsLabelAsTextAndAsTheGroupAccessibleName()
    {
        var cut = Render<RailBand>(p => p
            .Add(b => b.Label, "Placement")
            .AddChildContent("<span>x</span>"));

        var band = cut.Find(".rail-band");
        band.GetAttribute("role").Should().Be("group",
            "a set of chips needs a name from the group, not from its first chip");
        band.GetAttribute("aria-label").Should().Be("Placement");

        cut.Find(".rail-band__label").TextContent.Trim().Should().StartWith("Placement",
            "the label is ALWAYS visible text — colour alone must never carry the meaning, so the band must "
            + "still read in greyscale and to a screen reader");
    }

    [TestMethod]
    public void RailBand_RendersTheHintWhenPresent_AndNothingWhenAbsent()
    {
        var withHint = Render<RailBand>(p => p
            .Add(b => b.Label, "Activity groups")
            .Add(b => b.Hint, "0 joined")
            .AddChildContent("<span>x</span>"));
        withHint.Find(".rail-band__hint").TextContent.Trim().Should().Be("0 joined",
            "per-band counts and markers like 'read-only' live in Hint — bands never carry a count chip (Q28a)");

        var withoutHint = Render<RailBand>(p => p
            .Add(b => b.Label, "Placement")
            .AddChildContent("<span>x</span>"));
        withoutHint.FindAll(".rail-band__hint").Should().BeEmpty("an empty hint span would still take the gap");
    }

    [TestMethod]
    public void RailBand_Variant_MapsToTheTintClass_AndNeutralAddsNone()
    {
        foreach (var (variant, expected) in new[]
        {
            (RailBandVariant.Neutral, (string?)null),
            (RailBandVariant.Groups, "rail-band--groups"),
            (RailBandVariant.Required, "rail-band--required"),
            (RailBandVariant.Assumed, "rail-band--assumed"),
        })
        {
            var cut = Render<RailBand>(p => p
                .Add(b => b.Label, "L")
                .Add(b => b.Variant, variant)
                .AddChildContent("<span>x</span>"));

            var cls = cut.Find(".rail-band").ClassName ?? string.Empty;
            if (expected is null)
            {
                cls.Should().NotContain("rail-band--",
                    "Neutral is the default and must not emit a variant class, or every band would need "
                    + "a matching CSS rule to look like the base case");
            }
            else
            {
                cls.Should().Contain(expected, "round B adds Required/Assumed as a VARIANT, not as new markup");
            }
        }
    }

    [TestMethod]
    public void RailBand_RendersTheSeamId_SoAProbeOrTestCanAddressTheBand()
    {
        var cut = Render<RailBand>(p => p
            .Add(b => b.Label, "Activity groups")
            .Add(b => b.Id, "student-edit-activity-groups")
            .AddChildContent("<span>x</span>"));

        cut.Find("#student-edit-activity-groups").Should().NotBeNull(
            "the band's internal structure is otherwise unaddressable from a Playwright probe");
    }

    [TestMethod]
    public void RailChips_IsAList_SoAssistiveTechCountsTheChips()
    {
        var cut = Render<RailChips>(p => p.AddChildContent(
            "<span class=\"rail-chip\">a</span><span class=\"rail-chip\">b</span>"));

        cut.Find(".rail-chips").GetAttribute("role").Should().Be("list",
            "four unrelated spans announce as four things; a list announces as a set");
    }

    [TestMethod]
    public void RailChip_IsAListItem_AndAFactHasNoRemoveAffordance()
    {
        var cut = Render<RailChip>(p => p.Add(c => c.Text, "Grade 4"));

        var chip = cut.Find(".rail-chip");
        chip.GetAttribute("role").Should().Be("listitem");
        chip.TextContent.Trim().Should().Be("Grade 4");
        cut.FindAll(".rail-chip__action").Should().BeEmpty(
            "a fact is not removable — the ✕ exists only when an OnRemove handler is supplied");
        chip.ClassName.Should().NotContain("rail-chip--blocked");
    }

    [TestMethod]
    public void RailChip_WithOnRemove_RendersAnXLabelledWithTheChipText()
    {
        var removed = 0;
        var cut = Render<RailChip>(p => p
            .Add(c => c.Text, "Chess Club")
            .Add(c => c.GroupStyle, true)
            .Add(c => c.OnRemove, () => removed++));

        var button = cut.Find(".rail-chip__action");
        button.GetAttribute("aria-label").Should().Be("Remove Chess Club",
            "the ✕ is icon-only, so its accessible name has to name the thing it removes");
        button.GetAttribute("title").Should().Be("Remove Chess Club");
        cut.Find(".rail-chip").ClassName.Should().Contain("rail-chip--groups",
            "the chip tints with its Groups band so the pair reads as one block");

        button.Click();
        removed.Should().Be(1);
    }

    [TestMethod]
    public void RailChip_Disabled_DisablesTheRemoveButton_ButNotTheChip()
    {
        var cut = Render<RailChip>(p => p
            .Add(c => c.Text, "Chess Club")
            .Add(c => c.Disabled, true)
            .Add(c => c.OnRemove, () => { }));

        cut.Find(".rail-chip__action").HasAttribute("disabled").Should().BeTrue(
            "a slow server must not be double-clicked into a second write");
        cut.Find(".rail-chip").HasAttribute("aria-disabled").Should().BeFalse(
            "the fact is still readable; only the action is unavailable");
    }

    [TestMethod]
    public void RailChip_Blocked_ShowsTheReasonInVisibleText_TooltipAndAccessibleDescription()
    {
        var cut = Render<RailChip>(p => p
            .Add(c => c.Text, "Choir")
            .Add(c => c.Blocked, true)
            .Add(c => c.Reason, "window closed"));

        var chip = cut.Find(".rail-chip");
        chip.ClassName.Should().Contain("rail-chip--blocked");
        chip.GetAttribute("aria-disabled").Should().Be("true");

        cut.Find(".rail-chip__reason").TextContent.Should().Contain("window closed",
            "an unavailable option must read as a RULE, not as a bug (Q18a) — a tooltip alone is invisible "
            + "to touch, to keyboard tab-through and to most screen readers");

        chip.GetAttribute("title").Should().Be("Choir — window closed");
        chip.GetAttribute("aria-description").Should().Be("window closed",
            "the reason must survive assistive tech, not only the visual tooltip");

        cut.FindAll(".rail-chip__action").Should().BeEmpty();
    }

    [TestMethod]
    public void RailChip_BlockedWithNoReason_RendersNoReasonSpan_SoTheGapIsVisibleInTheTestNotTheUi()
    {
        // The contract says Reason is required when Blocked. Blazor cannot enforce that at compile time, so the
        // component renders nothing extra rather than inventing a reason — and this test documents that the
        // enforcement lives at review time plus the visible strike-through.
        var cut = Render<RailChip>(p => p
            .Add(c => c.Text, "Choir")
            .Add(c => c.Blocked, true));

        cut.FindAll(".rail-chip__reason").Should().BeEmpty();
        cut.Find(".rail-chip").ClassName.Should().Contain("rail-chip--blocked",
            "the chip is still struck through, so the missing reason is a review defect, never a silent pass");
    }
}
