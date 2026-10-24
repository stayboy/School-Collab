using Bunit;
using FluentAssertions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// The block itself (documents/solution/panel-layout-pattern.md §3, §4, §6): one box, an OPTIONAL
/// title, an OPT-IN chevron, and a header row whose actions live on the right.
/// <para>
/// The load-bearing assertion here is the negative one. "A non-collapsible panel renders no affordance
/// at all" is a hard requirement, not styling: a control that looks collapsible and is not is worse
/// than no control, so the chevron must be ABSENT from the DOM — not disabled, not CSS-hidden.
/// </para>
/// </summary>
[TestClass]
public class PanelSectionTests : BunitContext
{
    public PanelSectionTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    [TestMethod]
    public void NonCollapsiblePanel_RendersNoChevronElementAtAll()
    {
        var cut = Render<PanelSection>(p => p
            .Add(x => x.Title, "Rules")
            .AddChildContent("<p id=\"body\">rows</p>"));

        cut.FindAll("button.panel-section__chevron").Should().BeEmpty(
            "collapsibility is opt-in: the DEFAULT panel must render no affordance in the DOM at all");
        cut.FindAll("[aria-expanded]").Should().BeEmpty(
            "…and nothing that announces an expandable state either");
        cut.Markup.Should().NotContain("chevron",
            "not a disabled chevron, not a CSS-hidden one — the element does not exist");
        cut.Find("#body").Should().NotBeNull("the body is always present when nothing can collapse it");
    }

    [TestMethod]
    public void CollapsiblePanel_PutsTheChevronBeforeTheTitle_AndStartsExpanded()
    {
        var cut = Render<PanelSection>(p => p
            .Add(x => x.Id, "panel")
            .Add(x => x.Title, "Rules")
            .Add(x => x.Collapsible, true)
            .AddChildContent("<p id=\"body\">rows</p>"));

        var chevron = cut.Find("button.panel-section__chevron");
        chevron.GetAttribute("aria-expanded").Should().Be("true", "a collapsible panel starts expanded (§6)");
        chevron.GetAttribute("aria-controls").Should().Be("panel-body",
            "it names the body it hides — and the body id is derived from the panel's own id");

        var header = cut.Find(".panel-section__header");
        header.FirstElementChild!.ClassList.Should().Contain("panel-section__chevron",
            "the chevron LEADS: it is part of the title's affordance, not a trailing control (§6)");
        header.QuerySelectorAll(".panel-section__chevron")[0].NextElementSibling!.ClassList
            .Should().Contain("panel-section__title",
                "it sits immediately BEFORE the title it expands (§6)");
        cut.Find("#body").Should().NotBeNull("expanded means the body renders");

        chevron.Click();

        cut.FindAll("button.panel-section__chevron").Single()
            .GetAttribute("aria-expanded").Should().Be("false");
        cut.Find("#panel-body").HasAttribute("hidden").Should().BeTrue(
            "collapsing hides the BODY, never the panel — the body element stays in the DOM, merely hidden");
        cut.Find("#body").Should().NotBeNull(
            "…and it is `aria-controls`'s own target, so the body exists while collapsed and the children keep their state");
        cut.Find(".panel-section__title").TextContent.Trim().Should().Be("Rules",
            "the title stays visible, so a relationship's existence is never a click away");
    }

    [TestMethod]
    public void TheHeaderRow_IsNotClickable()
    {
        var cut = Render<PanelSection>(p => p
            .Add(x => x.Title, "Rules")
            .AddChildContent("<p>rows</p>"));

        var header = cut.Find(".panel-section__header");
        header.HasAttribute("blazor:onclick").Should().BeFalse(
            "a clickable header row overlaps the kebab's hit area and hides a target (§6)");
        header.GetAttribute("onclick").Should().BeNull();
        header.QuerySelectorAll("button").Should().BeEmpty(
            "with Collapsible false there is not even a chevron button on the row");
    }

    [TestMethod]
    public void Title_IsOptional_SoTheComponentCanRenderAnUntitledBox()
    {
        var cut = Render<PanelSection>(p => p
            .AddChildContent("<p id=\"body\">content</p>"));

        cut.Find("#body").Should().NotBeNull("no title is not an exception — the parameter is nullable");
        cut.Find(".panel-section__title").TextContent.Trim().Should().BeEmpty();
        cut.Find(".panel-section__rule").Should().NotBeNull("the rule travels with the header row either way");
    }

    [TestMethod]
    public void StickyHeader_PinsTheHeaderGroupAndKeepsItsRule()
    {
        var cut = Render<PanelSection>(p => p
            .Add(x => x.Title, "Details")
            .Add(x => x.StickyHeader, true)
            .AddChildContent("<p>fields</p>"));

        cut.Find(".panel-section").ClassList.Should().Contain("panel-section--sticky",
            "the first block's header row is what lets it own the page action (§6)");
        cut.Find(".panel-section__head").QuerySelector(".panel-section__rule").Should().NotBeNull(
            "the pinned row keeps its bottom rule, so it reads as the panel's header and not a toolbar");
        cut.FindAll(".panel-section__actions").Should().BeEmpty(
            "no HeaderActions supplied — the slot renders nothing rather than an empty box");
    }

    [TestMethod]
    public void HeaderActions_RenderOnTheTitleRow_AfterTheTitle()
    {
        var cut = Render<PanelSection>(p => p
            .Add(x => x.Title, "Content & Resources")
            .Add(x => x.HeaderActions, (Microsoft.AspNetCore.Components.RenderFragment)
                (b => b.AddMarkupContent(0, "<button id=\"page-action\">Save</button>")))
            .AddChildContent("<p>body</p>"));

        var header = cut.Find(".panel-section__header");
        header.QuerySelector(".panel-section__title")!.TextContent.Trim().Should().Be("Content & Resources");
        header.QuerySelector(".panel-section__actions #page-action").Should().NotBeNull(
            "the action slot sits on the title's row, right-aligned (§4)");
    }

    [TestMethod]
    public void Panel_IsABoxByDefault_AndTheBoxCanBeTurnedOff()
    {
        var boxed = Render<PanelSection>(p => p.Add(x => x.Title, "Rules"));
        boxed.Find(".panel-section").ClassList.Should().NotContain("panel-section--borderless",
            "a panel is a box unless the caller opts out — the rail relies on that default (§3)");

        var borderless = Render<PanelSection>(p => p
            .Add(x => x.Title, "Details")
            .Add(x => x.ShowBorder, false));
        borderless.Find(".panel-section").ClassList.Should().Contain("panel-section--borderless",
            "the main region turns the frame off (owner, 2026-10-24)");
    }

    [TestMethod]
    public void BorderlessPanel_KeepsTheHeaderRule_AsItsOnlySeparator()
    {
        var cut = Render<PanelSection>(p => p
            .Add(x => x.Title, "Message")
            .Add(x => x.ShowBorder, false)
            .AddChildContent("<p>body</p>"));

        cut.Find(".panel-section--borderless").QuerySelector(".panel-section__rule").Should().NotBeNull(
            "the title block and the body must still be divided — that divider is the ONE edge the "
            + "owner kept when the main region's frame was removed");
        cut.Find(".panel-section__body").Should().NotBeNull("turning the frame off must not affect the body");
    }
}
