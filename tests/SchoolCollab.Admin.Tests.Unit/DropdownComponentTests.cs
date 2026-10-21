using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// The key-value dropdown wrapper's parameter surface. Blazor validates component parameters at
/// RENDER time, not compile time, so a consumer passing a parameter the wrapper does not declare
/// throws on first render — and when that consumer sits inside a <c>SectionContent</c> (the
/// notification-template landing's toolbar), no page-level bUnit test ever executes it. Rendering the
/// wrapper directly is therefore the only test that can pin this contract.
/// </summary>
[TestClass]
public class DropdownComponentTests : BunitContext
{
    public DropdownComponentTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private sealed record Option(string Value, string Label);

    private static readonly Option[] Options = [new("a", "Alpha"), new("b", "Beta")];

    [TestMethod]
    public void AriaLabel_IsAcceptedAndForwardedToTheControl()
    {
        var cut = Render<DropdownComponent<Option, string>>(p => p
            .Add(x => x.Items, Options)
            .Add(x => x.SelectedValue, "a")
            .Add(x => x.OptionText, o => o.Label)
            .Add(x => x.OptionValue, o => o.Value)
            .Add(x => x.AriaLabel, "Filter by type"));

        cut.Markup.Should().Contain("aria-label=\"Filter by type\"",
            "the landing toolbar names each filter this way; without the parameter the whole page "
            + "throws on first render (2026-10-21)");
    }
}
