using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Constants;
using SchoolCollab.Students.Application.Components.Students;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for the shared <see cref="SectionCard{TItem}"/> component
/// (section-card-lessons-adoption.md §5). SectionCard is self-contained: it
/// only injects <c>NavigationManager</c> and takes all data via parameters
/// (<c>Items</c>, selectors, callbacks), so its full rendering contract is
/// verified ONCE here against fake data and applies to every usage (Subjects /
/// Teachers / Students / Streams cards on the grade-detail page).
///
/// The page-level tests (<c>GradeLevelDetailPageTests</c>) keep only the
/// wiring — which handler/selector each card binds — and delegate the rendering
/// mechanics to this file. This is the shared home for all kebab + action
/// assertions so each card's wiring test asserts only that the elements are
/// wired, not the rendering mechanics.
/// </summary>
[TestClass]
public class SectionCardTests : BunitContext
{
    private sealed record TestItem(string Name, string[]? Meta = null, string? Href = null);

    public SectionCardTests()
    {
        // bUnit needs the FluentUI services registered (JSRuntime + DI for
        // FluentCard / FluentAnchor / FluentButton / FluentProgressRing).
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private IRenderedComponent<SectionCard<TestItem>> RenderCard(
        TestItem[]? items,
        Action<ComponentParameterCollectionBuilder<SectionCard<TestItem>>>? configure = null)
    {
        return Render<SectionCard<TestItem>>(p =>
        {
            p.Add(x => x.Title, "Subjects");
            p.Add(x => x.Count, items?.Length ?? 0);
            p.Add(x => x.Items, items);
            p.Add(x => x.Icon, FluentIcons.Book);
            p.Add(x => x.ItemTextSelector, (TestItem i) => i.Name);
            p.Add(x => x.ItemMetaSelector, (TestItem i) => i.Meta);
            configure?.Invoke(p);
        });
    }

    [TestMethod]
    public void Renders_Title_And_Count()
    {
        var cut = RenderCard(new[] { new TestItem("Math") });

        cut.Markup.Should().Contain("Subjects", "the card title renders in the header");
        cut.Find(".section-card__count").TextContent.Should().Be("1",
            "the count badge shows the item count");
    }

    [TestMethod]
    public void Renders_EmptyState_When_NoItems()
    {
        var cut = RenderCard(Array.Empty<TestItem>(), p => p.Add(x => x.EmptyMessage, "No items yet."));

        cut.Find(".section-card__empty").TextContent.Should().Contain("No items yet.",
            "the empty message renders when Items is empty");
    }

    [TestMethod]
    public void Renders_LoadingRing_When_Loading()
    {
        var cut = RenderCard(new[] { new TestItem("Math") }, p => p.Add(x => x.Loading, true));

        cut.Find(".section-card__spinner").Should().NotBeNull(
            "the loading progress ring renders while Loading is true");
        cut.FindAll(".section-card__item").Count.Should().Be(0,
            "loading suppresses the item list");
    }

    [TestMethod]
    public void Renders_ErrorMessage_Over_EmptyState()
    {
        var cut = RenderCard(Array.Empty<TestItem>(), p => p
            .Add(x => x.ErrorMessage, "Failed to load")
            .Add(x => x.EmptyMessage, "No items yet."));

        cut.Find(".section-card__error").TextContent.Should().Contain("Failed to load",
            "the error message renders in the error slot");
        cut.Markup.Should().NotContain("No items yet.",
            "an error takes precedence over the empty state — a failure must not look like an empty state");
    }

    [TestMethod]
    public void Renders_TopN_Preview_Respecting_MaxPreviewItems()
    {
        var items = Enumerable.Range(1, 5).Select(i => new TestItem($"Item {i}")).ToArray();
        var cut = RenderCard(items, p => p.Add(x => x.MaxPreviewItems, 3));

        cut.FindAll(".section-card__item").Count.Should().Be(3,
            "only MaxPreviewItems items render in the preview");
        cut.Markup.Should().Contain("Item 1");
        cut.Markup.Should().Contain("Item 3");
        cut.Markup.Should().NotContain("Item 4",
            "items beyond MaxPreviewItems are not rendered");
    }

    [TestMethod]
    public void Renders_ItemTextSelector_As_Plain_Span_When_No_Href_Or_Click()
    {
        var cut = RenderCard(new[] { new TestItem("Mathematics") });

        cut.Markup.Should().Contain("Mathematics", "ItemTextSelector value renders");
        cut.Find(".item-name").TagName.Should().BeEquivalentTo("span",
            "with no href/click the item name renders as a plain span, not an anchor");
    }

    [TestMethod]
    public void Renders_ItemMetaSelector_With_Pipe_Separator()
    {
        var cut = RenderCard(new[] { new TestItem("Math", new[] { "2 strands", "3 lessons" }) });

        cut.Markup.Should().Contain("2 strands", "the first meta part renders");
        cut.Markup.Should().Contain("3 lessons", "the second meta part renders");
        cut.FindAll(".item-meta__separator").Count.Should().Be(1,
            "a pipe separator renders between the two meta parts");
    }

    [TestMethod]
    public void Renders_ItemHrefSelector_As_Anchor()
    {
        var cut = RenderCard(new[] { new TestItem("Math", Href: "/students/1") }, p => p
            .Add(x => x.ItemHrefSelector, (TestItem i) => i.Href));

        var anchor = cut.Find("fluent-anchor.item-name");
        anchor.GetAttribute("href").Should().Be("/students/1",
            "ItemHrefSelector renders the item name as a real hypertext anchor");
    }

    [TestMethod]
    public void ItemOnClick_Fires_When_Clicked()
    {
        TestItem? clicked = null;
        var cut = RenderCard(new[] { new TestItem("Math") }, p => p
            .Add(x => x.ItemOnClick, (TestItem i) => { clicked = i; return Task.CompletedTask; }));

        cut.Find("fluent-anchor.item-name").Click();

        clicked.Should().NotBeNull("ItemOnClick fires when the item name is clicked");
        clicked!.Name.Should().Be("Math");
    }

    [TestMethod]
    public void Renders_ItemNameTitle_Tooltip()
    {
        var cut = RenderCard(new[] { new TestItem("Math", Href: "/students/1") }, p => p
            .Add(x => x.ItemHrefSelector, (TestItem i) => i.Href)
            .Add(x => x.ItemNameTitle, "View student"));

        cut.Find("fluent-anchor.item-name").GetAttribute("title").Should().Be("View student",
            "ItemNameTitle sets the anchor tooltip");
    }

    [TestMethod]
    public void Renders_ItemActions_Fragment_Per_Item()
    {
        var cut = RenderCard(
            new[] { new TestItem("Math"), new TestItem("Science") },
            p => p.Add(x => x.ItemActions, (TestItem i) => (RenderFragment)(b =>
                b.AddMarkupContent(0, $"<span class=\"test-actions\">{i.Name}-actions</span>"))));

        cut.FindAll(".test-actions").Count.Should().Be(2,
            "the ItemActions fragment renders once per preview item");
        cut.Markup.Should().Contain("Math-actions");
        cut.Markup.Should().Contain("Science-actions");
    }

    [TestMethod]
    public void AddButton_Fires_OnAddClick()
    {
        var clicked = false;
        var cut = RenderCard(new[] { new TestItem("Math") }, p => p
            .Add(x => x.ShowAddButton, true)
            .Add(x => x.AddTitle, "Add subject")
            .Add(x => x.OnAddClick, () => { clicked = true; return Task.CompletedTask; }));

        cut.Find("fluent-button[title=\"Add subject\"]").Click();

        clicked.Should().BeTrue("the Add button fires OnAddClick");
    }

    [TestMethod]
    public void ViewAll_NavigationUrl_Renders_Anchor()
    {
        var cut = RenderCard(new[] { new TestItem("Math") }, p => p
            .Add(x => x.ViewAllNavigationUrl, "/students")
            .Add(x => x.ViewAllText, "View all"));

        var anchor = cut.Find(".section-card__footer fluent-anchor");
        anchor.GetAttribute("href").Should().Be("/students",
            "ViewAllNavigationUrl renders the footer as a real navigation anchor");
        anchor.TextContent.Should().Contain("View all");
    }

    [TestMethod]
    public void ViewAll_OnClick_Fires()
    {
        var clicked = false;
        var cut = RenderCard(new[] { new TestItem("Math") }, p => p
            .Add(x => x.OnViewAllClick, () => { clicked = true; return Task.CompletedTask; }));

        cut.Find(".section-card__footer fluent-anchor").Click();

        clicked.Should().BeTrue("the View-all footer fires OnViewAllClick");
    }

    [TestMethod]
    public void ItemTemplate_Renders_Custom_Content()
    {
        var cut = RenderCard(new[] { new TestItem("Math") }, p => p
            .Add(x => x.ItemTemplate, (TestItem i) => (RenderFragment)(b =>
                b.AddMarkupContent(0, $"<div class=\"custom-item\">{i.Name} custom</div>"))));

        cut.Markup.Should().Contain("custom-item", "ItemTemplate renders custom per-item content");
        cut.Markup.Should().Contain("Math custom");
    }
}
