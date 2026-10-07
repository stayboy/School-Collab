using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit coverage for the shared <see cref="Chip"/> pill (spec CH-1…CH-3). The selectable mode is
/// opt-in: with <c>OnToggle</c> set the chip is a real toggle button; without it the original
/// badge / dismissible rendering is untouched and remains the default.
///
/// <para><b>Regression pinned here.</b> The toggle rendered <c>aria-pressed</c> from a
/// <c>bool</c>, and Blazor emits a bool attribute as a <em>bare</em> attribute when true and omits
/// it when false — so the control emitted <c>aria-pressed=""</c> (invalid ⇒ announced as "not
/// pressed") and could never report <c>true</c>. Nothing asserted the value until R3, which is why
/// the defect survived the round that introduced it.</para>
/// </summary>
[TestClass]
public class ChipBunitTests : BunitContext
{
    public ChipBunitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private static EventCallback<bool> NoOpToggle(BunitContext context) =>
        EventCallback.Factory.Create<bool>(context, _ => { });

    [TestMethod]
    public void ToggleMode_Selected_RendersTheLiteralTrueAriaPressed()
    {
        var cut = Render<Chip>(p => p
            .Add(c => c.Label, "Multiple choice")
            .Add(c => c.Selected, true)
            .Add(c => c.OnToggle, NoOpToggle(this)));

        var button = cut.Find("button.chip-toggle");

        button.GetAttribute("aria-pressed").Should().Be("true",
            "a selected chip must announce pressed=true — never a bare/empty attribute");
        button.TextContent.Should().Contain("Multiple choice");
    }

    [TestMethod]
    public void ToggleMode_Unselected_ReportsFalseRatherThanOmittingTheAttribute()
    {
        var cut = Render<Chip>(p => p
            .Add(c => c.Label, "True / false")
            .Add(c => c.Selected, false)
            .Add(c => c.OnToggle, NoOpToggle(this)));

        var button = cut.Find("button.chip-toggle");

        button.HasAttribute("aria-pressed").Should().BeTrue(
            "the attribute stays present in both states so the toggle's state is always announced");
        button.GetAttribute("aria-pressed").Should().Be("false");
    }

    [TestMethod]
    public void ToggleMode_Click_InvokesOnToggleWithTheRequestedState()
    {
        bool? received = null;
        var cut = Render<Chip>(p => p
            .Add(c => c.Label, "Short answer")
            .Add(c => c.Selected, false)
            .Add(c => c.OnToggle, EventCallback.Factory.Create<bool>(this, v => received = v)));

        cut.Find("button.chip-toggle").Click();

        received.Should().BeTrue("clicking an unselected toggle requests ON");
    }

    [TestMethod]
    public void ToggleMode_SelectedState_CarriesTheTwoStateClass()
    {
        var selected = Render<Chip>(p => p
            .Add(c => c.Label, "On")
            .Add(c => c.Selected, true)
            .Add(c => c.OnToggle, NoOpToggle(this)));

        var unselected = Render<Chip>(p => p
            .Add(c => c.Label, "Off")
            .Add(c => c.Selected, false)
            .Add(c => c.OnToggle, NoOpToggle(this)));

        selected.Find("span.chip").ClassList.Should().Contain("is-selected");
        selected.Find("span.chip").ClassList.Should().Contain("chip--toggle");
        unselected.Find("span.chip").ClassList.Should().NotContain("is-selected");
    }

    [TestMethod]
    public void DefaultMode_StaysAReadOnlyBadgeWithNoToggleButton()
    {
        var cut = Render<Chip>(p => p.Add(c => c.Label, "Fractions"));

        cut.FindAll("button.chip-toggle").Should().BeEmpty("the toggle mode is opt-in (CH-1)");
        cut.Markup.Should().Contain("chip-badge",
            "without OnToggle the chip keeps the FluentBadge rendering");
        cut.Markup.Should().Contain("Fractions");
    }

    [TestMethod]
    public void DismissibleMode_StaysInBadgeMode()
    {
        var cut = Render<Chip>(p => p
            .Add(c => c.Label, "Fractions")
            .Add(c => c.OnDismiss, EventCallback.Factory.Create(this, () => { })));

        cut.FindAll("button.chip-toggle").Should().BeEmpty(
            "setting OnDismiss must not switch the chip into its toggle rendering");
        cut.Markup.Should().Contain("chip-badge");
        cut.Markup.Should().Contain("Fractions");
    }
}
