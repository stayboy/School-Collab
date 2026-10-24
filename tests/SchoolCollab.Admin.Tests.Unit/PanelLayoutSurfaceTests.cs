using Bunit;
using FluentAssertions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Settings.Application.Components.Pages.NotificationTemplates;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// The notification-template editor on the shared create / edit skeleton
/// (documents/solution/panel-layout-pattern.md).
/// <para>
/// Asserted through the rendered page, external behaviour only: which region each block belongs to,
/// which blocks are collapsible, where the page action renders, and that <c>Cancel</c> is present on
/// both template surfaces and leaves for the owning list.
/// </para>
/// </summary>
[TestClass]
public class PanelLayoutSurfaceTests : BunitContext
{
    public PanelLayoutSurfaceTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private TemplateApiHandler Register() => TemplateTestServices.Register<TemplateEditor>(this);

    private IRenderedComponent<TemplateEditor> RenderCreate() =>
        Render<TemplateEditor>(p => p.Add(x => x.Mode, TemplateEditorMode.Create));

    private IRenderedComponent<TemplateEditor> RenderEdit(TemplateRow row) =>
        Render<TemplateEditor>(p => p
            .Add(x => x.Mode, TemplateEditorMode.Edit)
            .Add(x => x.Id, row.Id));

    private TemplateRow EditableRow() => new()
    {
        Kind = 0, Channel = 1, Title = "Grade 8 Science reminder", Body = "BODY",
    };

    [TestMethod]
    public void ThePageActionHeaderCarriesThePair_AndTheMessagePanelIsNotSticky()
    {
        // Arrange
        Register();

        // Act
        var cut = RenderCreate();
        cut.WaitForAssertion(() => cut.FindAll("#template-save").Should().ContainSingle());

        // Assert — owner, 2026-10-24 (#404): the page action header owns the pair, and the block
        // header is no longer sticky — it is the only pinned row.
        var bar = cut.Find(".authoring-actionbar");
        bar.QuerySelectorAll("#template-save").Should().ContainSingle(
            "the page action renders in the page action header");
        bar.QuerySelectorAll("#template-cancel").Should().ContainSingle(
            "Cancel sits beside it, left of the primary");
        var message = cut.Find("#template-editor-message");
        message.ClassList.Should().NotContain("panel-section--sticky",
            "the block header is a label now — pinning it too is the overlap #404 reported");
        message.QuerySelectorAll(".panel-section__actions #template-save").Should().BeEmpty(
            "nothing duplicates the page action inside the block");
        message.QuerySelectorAll(".panel-section__chevron").Should().BeEmpty(
            "the main region is never collapsible (§6)");
    }

    [TestMethod]
    public void Cancel_RendersOnBothTemplateSurfaces_LeftOfThePrimary_AndLeavesForTheTemplateList()
    {
        // Arrange
        var handler = Register();
        var row = EditableRow();
        handler.Rows.Add(row);

        foreach (var cut in new[] { RenderCreate(), RenderEdit(row) })
        {
            cut.WaitForAssertion(() => cut.FindAll("#template-cancel").Should().ContainSingle(
                "§16: Cancel is on all four surfaces — template create and template edit included"));

            var header = cut.Find(".authoring-actionbar-controls").InnerHtml;
            header.IndexOf("template-cancel", StringComparison.Ordinal)
                .Should().BeLessThan(header.IndexOf("template-save", StringComparison.Ordinal),
                    "Cancel sits LEFT of the primary action, both in the page action header");
        }

        var create = RenderCreate();
        create.WaitForAssertion(() => create.FindAll("#template-cancel").Should().ContainSingle());

        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();

        // Act
        create.Find("#template-cancel").Click();

        // Assert
        navigation.Uri.Should().EndWith("/notification-templates",
            "Cancel navigates to the owning list page (the template surfaces carry no dirty guard today)");
    }

    [TestMethod]
    public void TheRail_IsConstraintsThenPreview_BothCollapsible_AndNothingElse()
    {
        // Arrange
        Register();

        // Act
        var cut = RenderCreate();
        cut.WaitForAssertion(() => cut.FindAll("#template-editor-rail").Should().ContainSingle());

        // Assert — rail membership is the ownership test (§5): Constraints and Preview present OTHER
        // records / a derived read, so they are the rail; the Message is the entity's own fields.
        var rail = cut.Find("#template-editor-rail");
        rail.ClassList.Should().Contain("region-grid__rail", "the rail is a region that draws nothing");

        var panels = rail.QuerySelectorAll(".panel-section").ToList();
        panels.Select(p => p.QuerySelector(".panel-section__title")!.TextContent.Trim())
            .Should().Equal(["Constraints", "Preview"], "the consent table's rail, in reading order");

        panels.Should().OnlyContain(p => p.QuerySelector("button.panel-section__chevron") != null,
            "both rail panels are collapsible (§6 consent table)");

        cut.FindAll(".region-grid__main .panel-section__chevron").Should().BeEmpty(
            "…and the main region carries no collapse control at all");

        var mainPanel = cut.Find(".region-grid__main .panel-section");
        mainPanel.ClassList.Should().NotContain("panel-section--sticky",
            "no block header is sticky any more (owner, 2026-10-24; #404)");

        // The frame is regional (owner, 2026-10-24): the main block is the page's own content and
        // carries no box — its only edge is the header rule — while every rail block keeps the box,
        // because a narrow column of unboxed blocks reads as one undifferentiated list.
        mainPanel.ClassList.Should().Contain("panel-section--borderless",
            "the main region is borderless");
        panels.Should().OnlyContain(p => !p.ClassList.Contains("panel-section--borderless"),
            "the rail keeps its boxes");
    }

    [TestMethod]
    public void Preview_RefreshSitsInThePanelHeader_NotInABodyFooter()
    {
        // Arrange
        Register();

        // Act
        var cut = RenderCreate();
        cut.WaitForAssertion(() => cut.FindAll("#template-preview-refresh").Should().ContainSingle());

        // Assert
        var refresh = cut.Find("#template-preview-refresh");
        refresh.ParentElement!.ClassList.Should().Contain("panel-section__actions",
            "Refresh is the Preview panel's own header control (§6)");
        cut.FindAll(".template-editor-preview__actions").Should().BeEmpty(
            "the body footer it used to sit in is gone — one action row per box");
    }

    [TestMethod]
    public void Reset_KeepsItsPinnedId_InTheMessageHeader_OnAWritableEditRow()
    {
        // Arrange
        var handler = Register();
        var row = EditableRow();
        handler.Rows.Add(row);

        // Act
        var cut = RenderEdit(row);

        // Assert
        cut.WaitForAssertion(() =>
        {
            cut.Find("#template-editor-message .panel-section__actions #template-reset").Should().NotBeNull(
                "Reset is a header control now, and its pinned id did not move");
            cut.FindAll("#authoring-primary-action").Should().BeEmpty(
                "the shell's own primary id is not emitted on this surface — it delegates instead");
        });
    }

    [TestMethod]
    public void TheLayout_IsTheSharedRegionGrid_WithNoViewportBreakpointOfItsOwn()
    {
        // Arrange
        Register();

        // Act
        var cut = RenderCreate();
        cut.WaitForAssertion(() => cut.FindAll(".region-grid").Should().ContainSingle());

        // Assert
        cut.Find(".region-grid").ClassList.Should().Contain("template-editor-layout",
            "the page keeps its layout hook, but the TRACKS come from the shared rule");
        cut.FindAll(".region-grid-host").Should().ContainSingle(
            "the container-type ancestor is the wrapper, because a grid cannot query itself (§2)");
        cut.FindAll(".authoring-layout--cq").Should().BeEmpty(
            "the duplicated track definition and its @media bound are retired");
    }
}
