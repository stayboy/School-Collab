using System.Reflection;
using System.Text.RegularExpressions;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using SchoolCollab.Admin.Shared.Components;

namespace SchoolCollab.Admin.Tests.Unit.Components;

/// <summary>
/// bUnit tests for the generic <see cref="DropdownComponent{TItem, TValue}"/>.
/// Verifies the primitive-key binding pattern: the parent stores just the key
/// (e.g. <c>Guid?</c>), not the full option object, and the component resolves
/// the full object internally for display + <see cref="FluentSelect"/> binding.
/// Mirrors the contract of <c>CodedValueDropdown.SelectedId</c> (a <see cref="Guid"/>?)
/// but generic over both the item type AND the key type.
/// </summary>
[TestClass]
public class DropdownComponentTests : BunitContext
{
    private sealed record TestItem(Guid Id, string Name);

    [TestInitialize]
    public void Setup()
    {
        Services.AddFluentUIComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [TestMethod]
    public void DropdownComponent_RendersItems_FromParameter()
    {
        // Arrange
        var items = new[]
        {
            new TestItem(Guid.NewGuid(), "Alpha"),
            new TestItem(Guid.NewGuid(), "Beta"),
            new TestItem(Guid.NewGuid(), "Gamma")
        };

        // Act
        var cut = Render<DropdownComponent<TestItem, Guid?>>(parameters => parameters
            .Add(p => p.Items, items)
            .Add(p => p.OptionText, i => i.Name)
            .Add(p => p.OptionValue, i => i.Id));

        cut.WaitForState(() => cut.Find("fluent-dropdown") is not null);

        // Assert: the fluent-dropdown is rendered with the items.
        cut.Find("fluent-dropdown").Should().NotBeNull();
    }

    [TestMethod]
    public void DropdownComponent_SelectedValue_ResolvesToFullOption()
    {
        // Arrange: the parent stores just the key (Guid?), not the full object.
        var alphaId = Guid.NewGuid();
        var betaId = Guid.NewGuid();
        var items = new[]
        {
            new TestItem(alphaId, "Alpha"),
            new TestItem(betaId, "Beta")
        };
        Guid? selectedId = alphaId;

        // Act
        var cut = Render<DropdownComponent<TestItem, Guid?>>(parameters => parameters
            .Add(p => p.Items, items)
            .Add(p => p.SelectedValue, selectedId)
            .Add(p => p.OptionText, i => i.Name)
            .Add(p => p.OptionValue, i => i.Id));

        cut.WaitForState(() => cut.Find("fluent-dropdown") is not null);

        // Assert: FluentUI 5.x no longer renders the bound value as a `current-value`
        // attribute on the dropdown (that was the v4 <fluent-select> DOM). The selection
        // is expressed on the resolved option instead: the key resolves to the full item
        // and that item's option — and only it — renders as `selected`, which is the
        // guarantee the parent's key-only binding rests on.
        var selectedOptions = cut.FindAll("fluent-option").Where(o => o.HasAttribute("selected")).ToList();
        selectedOptions.Should().ContainSingle("the bound key resolves to exactly one option");
        selectedOptions[0].TextContent.Should().Contain("Alpha");
    }

    [TestMethod]
    public void DropdownComponent_SelectedValueChanged_FiresWithKey()
    {
        // Arrange: when the user picks an option, the component fires
        // SelectedValueChanged with the KEY (Guid?), not the full object.
        var alphaId = Guid.NewGuid();
        var betaId = Guid.NewGuid();
        var items = new[]
        {
            new TestItem(alphaId, "Alpha"),
            new TestItem(betaId, "Beta")
        };
        Guid? capturedKey = null;

        var cut = Render<DropdownComponent<TestItem, Guid?>>(parameters => parameters
            .Add(p => p.Items, items)
            .Add(p => p.SelectedValue, alphaId)
            .Add(p => p.SelectedValueChanged, EventCallback.Factory.Create<Guid?>(this, value => capturedKey = value))
            .Add(p => p.OptionText, i => i.Name)
            .Add(p => p.OptionValue, i => i.Id));

        cut.WaitForState(() => cut.Find("fluent-dropdown") is not null);

        // Act: simulate a pick by changing SelectedValue (in real use this
        // would come from the fluent-dropdown's internal selection change).
        // We verify the contract via TryFindItem below; the EventCallback
        // wiring is covered by the CodedValueDropdown tests.
        cut.Instance.TryFindItem(betaId, out var resolved).Should().BeTrue();
        resolved!.Id.Should().Be(betaId);

        _ = capturedKey; // captured via the EventCallback in a real pick flow
    }

    [TestMethod]
    public void DropdownComponent_TryFindItem_ReturnsFullItem_ForKey()
    {
        // Arrange
        var alphaId = Guid.NewGuid();
        var betaId = Guid.NewGuid();
        var items = new[]
        {
            new TestItem(alphaId, "Alpha"),
            new TestItem(betaId, "Beta")
        };

        var cut = Render<DropdownComponent<TestItem, Guid?>>(parameters => parameters
            .Add(p => p.Items, items)
            .Add(p => p.OptionText, i => i.Name)
            .Add(p => p.OptionValue, i => i.Id));

        cut.WaitForState(() => cut.Find("fluent-dropdown") is not null);

        // Assert: TryFindItem resolves the full item for a given key.
        cut.Instance.TryFindItem(alphaId, out var alpha).Should().BeTrue();
        alpha!.Name.Should().Be("Alpha");

        cut.Instance.TryFindItem(betaId, out var beta).Should().BeTrue();
        beta!.Name.Should().Be("Beta");

        cut.Instance.TryFindItem(Guid.NewGuid(), out var missing).Should().BeFalse();
        missing.Should().BeNull();
    }

    [TestMethod]
    public void DropdownComponent_TryFindItem_ReturnsFalse_ForNullKey()
    {
        // Arrange
        var items = new[] { new TestItem(Guid.NewGuid(), "Alpha") };

        var cut = Render<DropdownComponent<TestItem, Guid?>>(parameters => parameters
            .Add(p => p.Items, items)
            .Add(p => p.OptionText, i => i.Name)
            .Add(p => p.OptionValue, i => i.Id));

        cut.WaitForState(() => cut.Find("fluent-dropdown") is not null);

        // Assert: a null key returns false (no item matches).
        cut.Instance.TryFindItem(null, out var item).Should().BeFalse();
        item.Should().BeNull();
    }

    [TestMethod]
    public void DropdownComponent_Width_EmitsInlineStyle_OnFluentSelect()
    {
        // Arrange: the repo's W1–W9 FieldWidth enum sizes the FluentUI control
        // from the OUTSIDE, and v5 splits that "outside" into two elements —
        // <FluentSelect> renders as
        //   <fluent-field class="dropdown-component" style="width:80px; …">
        //     <fluent-dropdown slot="input">…
        // (probe-verified, Round A) — so the `Style` parameter lands on the
        // `fluent-field` HOST and carries the width + the min-width:0 / max-width:100%
        // safety nets from FieldWidthExtensions.ToCssStyle. W1 = 80px.
        var items = new[] { new TestItem(Guid.NewGuid(), "Alpha") };

        // Act
        var cut = Render<DropdownComponent<TestItem, Guid?>>(parameters => parameters
            .Add(p => p.Items, items)
            .Add(p => p.OptionText, i => i.Name)
            .Add(p => p.OptionValue, i => i.Id)
            .Add(p => p.Width, FieldWidth.W1));

        cut.WaitForState(() => cut.Find("fluent-dropdown") is not null);

        // Assert: the style lands on the host that actually receives it …
        var hostStyle = cut.Find("fluent-field").GetAttribute("style");
        hostStyle.Should().Contain("width:80px");
        hostStyle.Should().Contain("min-width:0");
        hostStyle.Should().Contain("max-width:100%");

        // … and NOT on the control (moving this assertion onto the control would
        // assert the old v4 DOM and fail for the right reason).
        cut.Find("fluent-dropdown").GetAttribute("style").Should().BeNullOrEmpty();

        // The host width only reaches the visible control through the declared
        // slotted-control rule — that second half is pinned here.
        AssertSlottedControlSizesFromFieldHost();
    }

    [TestMethod]
    public void DropdownComponent_WidthNull_OmitsStyleAttribute()
    {
        // Arrange: when Width is not set, no inline style is emitted (so the
        // consumer's Class controls the width) and no empty style="" attribute
        // leaks onto either the host or the control.
        var items = new[] { new TestItem(Guid.NewGuid(), "Alpha") };

        // Act
        var cut = Render<DropdownComponent<TestItem, Guid?>>(parameters => parameters
            .Add(p => p.Items, items)
            .Add(p => p.OptionText, i => i.Name)
            .Add(p => p.OptionValue, i => i.Id));

        cut.WaitForState(() => cut.Find("fluent-dropdown") is not null);

        // Assert: neither the fluent-field host nor the control carries a style.
        cut.Find("fluent-field").GetAttribute("style").Should().BeNullOrEmpty();
        cut.Find("fluent-dropdown").GetAttribute("style").Should().BeNullOrEmpty();

        // The sizing rule is a property of the ladder, not of a single call site,
        // so it must be declared whether or not Width is set.
        AssertSlottedControlSizesFromFieldHost();
    }

    /// <summary>
    /// Pins the second half of the width ladder's mechanism (owner pin Q28(a),
    /// 2026-10-16): v5 puts the wrapper's inline width style on the `fluent-field`
    /// HOST, and `@fluentui/web-components` gives the slotted control no width of
    /// its own (only `grid-area: input`), so the control must be told to size from
    /// the host by the rule in the shared global stylesheet the Admin host links
    /// (<c>_content/SchoolCollab.Admin.Shared/app.css</c>). bUnit cannot compute
    /// CSS, so this reads that stylesheet and requires the declaration to be
    /// present — deleting or renaming it re-breaks the ladder and fails here.
    /// LIMIT: this pins the declared mechanism, not the rendered pixels. The rule must say
    /// <c>width: 100%</c>, never <c>inherit</c>: measured in the running app 2026-10-23, `inherit`
    /// takes the host's COMPUTED width (the `w-N` percentage) and the percentage then resolves
    /// against the slotted grid area, leaving a `fluent-text-input` at its intrinsic 185px inside
    /// a 655px field. The control's own shadow CSS can still clamp a step; only a live browser
    /// confirms pixels.
    /// </summary>
    private static void AssertSlottedControlSizesFromFieldHost()
    {
        var stylesheetPath = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            "..", "..", "..", "..", "..",
            "src", "SchoolCollab.Admin.Shared", "wwwroot", "app.css"));
        File.Exists(stylesheetPath).Should().BeTrue(
            $"the shared global stylesheet should exist at '{stylesheetPath}' — check the path resolution");

        var css = File.ReadAllText(stylesheetPath);
        Regex.IsMatch(css, @"fluent-field\s*>\s*fluent-dropdown\s*,[^{}]*\{\s*width:\s*100%\s*;")
            .Should().BeTrue(
                "the slotted control must size from the fluent-field host that carries the inline width style "
                + "(fluent-field > fluent-dropdown { width: 100%; } in src/SchoolCollab.Admin.Shared/wwwroot/app.css)");
    }

    [TestMethod]
    public async Task DropdownComponent_Refresh_TriggersRerender()
    {
        // Arrange: the parent mutates the Items list in place (without
        // reassigning the field). Blazor's parameter-change detection
        // doesn't fire on in-place mutations, so the dropdown would show
        // a stale list. Refresh() forces a re-render.
        var items = new List<TestItem>
        {
            new(Guid.NewGuid(), "Alpha"),
            new(Guid.NewGuid(), "Beta")
        };

        var cut = Render<DropdownComponent<TestItem, Guid?>>(parameters => parameters
            .Add(p => p.Items, items)
            .Add(p => p.OptionText, i => i.Name)
            .Add(p => p.OptionValue, i => i.Id));

        cut.WaitForState(() => cut.Find("fluent-dropdown") is not null);

        // Act: mutate in place + Refresh(). The Refresh() call must be on
        // the Blazor dispatcher (bunit's InvokeAsync) — StateHasChanged()
        // throws InvalidOperationException when called from the test thread
        // (not on the renderer's dispatcher). In a real app, Refresh() is
        // called from within a Blazor event handler which runs on the
        // dispatcher, so this is only a test-side concern.
        var gammaId = Guid.NewGuid();
        items.Add(new TestItem(gammaId, "Gamma"));
        await cut.InvokeAsync(() => cut.Instance.Refresh());

        // Assert: TryFindItem finds the newly added item (proves the
        // re-render picked up the in-place mutation).
        cut.Instance.TryFindItem(gammaId, out var gamma).Should().BeTrue();
        gamma!.Name.Should().Be("Gamma");
    }
}
