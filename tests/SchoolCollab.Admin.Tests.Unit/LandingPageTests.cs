using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components;
using SchoolCollab.Admin.Shared.Constants;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for the <see cref="SchoolCollab.Admin.Shared.Components.Landing.LandingPage{TItem}"/>
/// wrapper. Covers the shell states (loading / empty / grid / error), the
/// New-button navigation, the search box wiring, and the toolbar / above-grid /
/// footer slot rendering. Markup lives in <c>TestLandingPage.razor</c> because
/// the <c>@&lt;…&gt;</c> templated syntax is only valid in <c>.razor</c> files.
/// </summary>
[TestClass]
public class LandingPageTests : BunitContext
{
    public LandingPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private IRenderedComponent<TestLandingPage> RenderWrapper(
        TestLandingPage.Widget[]? items = null,
        string? error = null,
        bool loading = false,
        bool searchEnabled = true,
        bool createEnabled = true,
        bool showFilters = false,
        bool showActions = false,
        bool showFooter = false,
        bool rowActionsUseMenuService = false,
        EventCallback<string>? searchChanged = null,
        Func<TestLandingPage.Widget, IReadOnlyList<RowAction>>? rowActions = null)
    {
        return Render<TestLandingPage>(p => p
            .Add(x => x.Items, items)
            .Add(x => x.Error, error)
            .Add(x => x.Loading, loading)
            .Add(x => x.SearchEnabled, searchEnabled)
            .Add(x => x.CreateEnabled, createEnabled)
            .Add(x => x.ShowFilters, showFilters)
            .Add(x => x.ShowActions, showActions)
            .Add(x => x.ShowFooter, showFooter)
            .Add(x => x.RowActionsUseMenuService, rowActionsUseMenuService)
            .Add(x => x.SearchTextChanged, searchChanged ?? EventCallback<string>.Empty)
            .Add(x => x.RowActions, rowActions));
    }

    [TestMethod]
    public void Renders_Title_In_PageTitle_And_Heading()
    {
        var cut = RenderWrapper(items: []);

        cut.Find("h1").TextContent.Should().Contain("Widgets");
    }

    [TestMethod]
    public void NewButton_Navigates_To_CreateRoute()
    {
        var cut = RenderWrapper(items: []);

        cut.Find("fluent-anchor").Click();

        Services.GetRequiredService<NavigationManager>().Uri
                .Should().EndWith("/widgets/create");
    }

    [TestMethod]
    public void CreateEnabled_False_Hides_NewButton()
    {
        var cut = RenderWrapper(items: [], createEnabled: false);

        cut.FindAll("fluent-anchor").Should().BeEmpty();
    }

    [TestMethod]
    public void NullItems_Shows_Spinner()
    {
        var cut = RenderWrapper(items: null);

        cut.Markup.ToLower().Should().Contain("progress");
    }

    [TestMethod]
    public void LoadingTrue_Shows_Spinner_EvenWithItems()
    {
        var cut = RenderWrapper(
            items: [new(Guid.NewGuid(), "W1")], loading: true);

        cut.Markup.ToLower().Should().Contain("progress");
    }

    [TestMethod]
    public void EmptyItems_Shows_EmptyMessage()
    {
        var cut = RenderWrapper(items: []);

        cut.Markup.Should().Contain("No widgets yet.");
    }

    [TestMethod]
    public void NonEmptyItems_Renders_Grid_WithColumns()
    {
        var cut = RenderWrapper(
            items: [new(Guid.NewGuid(), "W1"), new(Guid.NewGuid(), "W2")]);

        cut.Markup.Should().Contain("fluent-data-grid");
        cut.Markup.Should().Contain("Name");   // column header
        cut.Markup.Should().Contain("W1");     // row 1
        cut.Markup.Should().Contain("W2");     // row 2
    }

    [TestMethod]
    public void Error_Renders_RedMessageBar()
    {
        var cut = RenderWrapper(items: [], error: "kaboom");

        cut.Markup.Should().Contain("kaboom");
    }

    [TestMethod]
    public void SearchEnabled_False_Hides_SearchBox()
    {
        var cut = RenderWrapper(items: [], searchEnabled: false);

        cut.FindAll("fluent-text-field").Should().BeEmpty();
    }

    [TestMethod]
    public void SearchEnabled_True_Renders_SearchBox_WithPlaceholder()
    {
        var cut = RenderWrapper(items: []);

        cut.Find("fluent-text-field").GetAttribute("placeholder")
            .Should().Be("Search widgets…");
    }

    [TestMethod]
    public async Task SearchBox_Change_Fires_SearchTextChanged()
    {
        var captured = string.Empty;
        var cb = EventCallback.Factory.Create<string>(this, v => captured = v);

        var cut = RenderWrapper(items: [], searchChanged: cb);

        await cut.Find("fluent-text-field").InputAsync("needle");

        captured.Should().Be("needle");
    }

    [TestMethod]
    public void ToolbarFilters_Slot_Renders()
    {
        var cut = RenderWrapper(items: [], showFilters: true);

        cut.Markup.Should().Contain("Show deleted");
    }

    [TestMethod]
    public void ToolbarActions_Slot_Renders()
    {
        var cut = RenderWrapper(items: [], showActions: true);

        cut.Markup.Should().Contain("Chat");
    }

    [TestMethod]
    public void Footer_Slot_Renders()
    {
        var cut = RenderWrapper(items: [], showFooter: true);

        cut.Markup.Should().Contain("chat-launcher");
    }

    [TestMethod]
    public void RowActions_AllSingleAction_RendersLabeledButton_NotKebab()
    {
        // Every row has exactly one action → each renders a labeled button,
        // never the kebab (⋮). No row qualifies for the kebab, so ForceKebab
        // stays false.
        var cut = RenderWrapper(
            items: [new(Guid.NewGuid(), "W1"), new(Guid.NewGuid(), "W2")],
            rowActions: w => new List<RowAction> { RowAction.Callback("Edit", () => { }, FluentIcons.Edit) });

        var menus = cut.FindComponents<RowActionsMenu>();
        menus.Should().HaveCount(2, "one actions menu per row");
        menus.Should().OnlyContain(m => m.Instance.ForceKebab == false, "no row qualifies for the kebab");
        cut.Markup.Should().Contain(">Edit</fluent-button>", "single-action rows render a labeled Edit button");
    }

    [TestMethod]
    public void RowActions_AnyRowHasKebab_ForcesKebabOnEveryRow()
    {
        // W1 has 2 actions (Edit + Delete) → qualifies for the kebab; W2 has
        // only 1 action (Edit). Repo convention: because W1 qualifies, the
        // kebab is forced on EVERY row so the actions column is consistent.
        var cut = RenderWrapper(
            items: [new(Guid.NewGuid(), "W1"), new(Guid.NewGuid(), "W2")],
            rowActions: w => w.Name == "W1"
                ? new List<RowAction>
                {
                    RowAction.Callback("Edit", () => { }, FluentIcons.Edit),
                    RowAction.Callback("Delete", () => { }, FluentIcons.Delete, destructive: true),
                }
                : new List<RowAction> { RowAction.Callback("Edit", () => { }, FluentIcons.Edit) });

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("fluent-data-grid", "the grid renders"));
        cut.WaitForAssertion(() => cut.FindComponents<RowActionsMenu>().Should().HaveCount(2,
            "one actions menu per row"));
        var menus = cut.FindComponents<RowActionsMenu>();
        menus.Should().OnlyContain(m => m.Instance.ForceKebab == true,
            "any row qualifying for the kebab forces it on every row");
    }
}