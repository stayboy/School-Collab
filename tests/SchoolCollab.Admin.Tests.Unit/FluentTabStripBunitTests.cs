using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// <c>FluentTabStrip</c> — the repo's traditional folder-tab chrome over FluentTabs (round
/// <c>wysiwyg-instruction-editor</c>, extracted for reuse 2026-10-11). The chrome itself is CSS,
/// verified live with screenshots pinned in the round doc; these seams pin the STRUCTURE: the
/// container that carries the styling, FluentTabs untouched beneath it, and the caller's tabs and
/// panels rendered through — including the hidden-panel behaviour editors inside tabs rely on.
/// </summary>
[TestClass]
public class FluentTabStripBunitTests : BunitContext
{
    /// <summary>FluentTabs injects <c>LibraryConfiguration</c> and renders FluentOverflow, which
    /// imports a JS module — the standard FluentUI bUnit setup (AddFluentUIComponents + Loose).</summary>
    public FluentTabStripBunitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    [TestMethod]
    public void RendersTheCallersTabsInsideFluentTabs_WithTheChromeContainer()
    {
        var cut = Render<FluentTabStrip>(p => p.AddChildContent(builder =>
        {
            builder.OpenComponent<FluentTab>(0);
            builder.AddAttribute(1, "Label", "First");
            builder.AddAttribute(2, "ChildContent", (RenderFragment)(b => b.AddContent(0, "one")));
            builder.CloseComponent();
            builder.OpenComponent<FluentTab>(3);
            builder.AddAttribute(4, "Label", "Second");
            builder.AddAttribute(5, "ChildContent", (RenderFragment)(b => b.AddContent(0, "two")));
            builder.CloseComponent();
        }));

        cut.Find(".fluent-tab-strip").Should().NotBeNull("the container is the styling seam the css binds to");
        cut.Find(".fluent-tab-strip fluent-tabs").Should().NotBeNull("the tabs are FluentUI's, untouched");

        var tabs = cut.FindAll("fluent-tab");
        tabs.Should().HaveCount(2);
        tabs[0].GetAttribute("aria-label").Should().Be("First", "the caller's labels pass through");
        tabs[1].GetAttribute("aria-label").Should().Be("Second");

        var panels = cut.FindAll("fluent-tab-panel");
        panels.Should().HaveCount(2,
            "both panels render even for the inactive tab — FluentUI hides the inactive one in the BROWSER "
            + "via the web component (verified live), so tab content is never unmounted in bUnit either");
        panels[1].GetAttribute("hidden").Should().BeNull(
            "the hidden attribute is the web component's own JS behaviour, not Blazor markup");
    }
}