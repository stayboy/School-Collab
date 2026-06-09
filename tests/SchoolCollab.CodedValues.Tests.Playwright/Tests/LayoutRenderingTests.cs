using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Playwright;
using Microsoft.Playwright.MSTest;

namespace SchoolCollab.CodedValues.Tests.Playwright.Tests;

/// <summary>
/// Verifies that page layouts render correctly — no blank pages,
/// headers are visible, and content fills the available space.
/// </summary>
[TestClass]
public class LayoutRenderingTests : PageTest
{
    private string BaseUrl => PlaywrightSettings.BaseUrl;

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        BaseURL = BaseUrl,
        IgnoreHTTPSErrors = true,
        ViewportSize = new ViewportSize { Width = 1280, Height = 720 }
    };

    [TestMethod]
    public async Task IndexPage_RendersHeaderAndGrid()
    {
        var gridResponseTask = Page.WaitForResponseAsync(
            resp => resp.Url.Contains("/coded-values") && resp.Request.Method == "GET",
            new() { Timeout = 30_000 });

        await Page.GotoAsync("/coded-values");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var gridResponse = await gridResponseTask;
        gridResponse.Status.Should().Be(200);

        // Header must be visible (not blank)
        var heading = Page.GetByRole(AriaRole.Heading, new() { Level = 1, Name = "Coded Values" });
        await Expect(heading).ToBeVisibleAsync();

        // New Category button must be visible
        var button = Page.GetByRole(AriaRole.Button, new() { Name = "New Category" });
        await Expect(button).ToBeVisibleAsync();

        // Data grid must be visible with seeded rows
        var grid = Page.GetByRole(AriaRole.Grid);
        await Expect(grid).ToBeVisibleAsync();

        var gender = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = "GENDER" });
        await Expect(gender).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task IndexPage_GridFillsAvailableSpace()
    {
        await Page.GotoAsync("/coded-values");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Wait for grid to render
        var grid = Page.GetByRole(AriaRole.Grid);
        await Expect(grid).ToBeVisibleAsync(new() { Timeout = 30_000 });

        // The grid should have a non-zero bounding box (not collapsed to 0 height)
        var gridBox = await grid.BoundingBoxAsync();
        gridBox.Should().NotBeNull("the grid should have a bounding box");
        gridBox!.Height.Should().BeGreaterThan(50, "the grid should fill available vertical space, not collapse to 0");
    }

    [TestMethod]
    public async Task ChildrenPage_RendersHeaderAndBreadcrumb()
    {
        // Navigate from root to children page
        await Page.GotoAsync("/coded-values");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var genderRow = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = "GENDER" });
        await Expect(genderRow).ToBeVisibleAsync(new() { Timeout = 30_000 });

        var childrenResponseTask = Page.WaitForResponseAsync(
            resp => resp.Url.Contains("/coded-values/by-parent") && resp.Request.Method == "GET",
            new() { Timeout = 30_000 });

        await genderRow.GetByRole(AriaRole.Link, new() { Name = "View children" }).ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await childrenResponseTask;

        // Breadcrumb must be visible
        var breadcrumb = Page.GetByRole(AriaRole.Navigation, new() { Name = "breadcrumb" });
        await Expect(breadcrumb).ToBeVisibleAsync();

        // H2 heading must be visible
        var heading = Page.GetByRole(AriaRole.Heading, new() { Level = 2 });
        await Expect(heading).ToBeVisibleAsync();

        // "Add Child Value" button must be visible
        var button = Page.GetByRole(AriaRole.Button, new() { Name = "Add Child Value" });
        await Expect(button).ToBeVisibleAsync();

        // Children grid must be visible
        var childrenGrid = Page.GetByRole(AriaRole.Grid);
        await Expect(childrenGrid).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task ChildrenPage_GridHasNonZeroHeight()
    {
        await Page.GotoAsync("/coded-values");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var genderRow = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = "GENDER" });
        await Expect(genderRow).ToBeVisibleAsync(new() { Timeout = 30_000 });

        var childrenResponseTask = Page.WaitForResponseAsync(
            resp => resp.Url.Contains("/coded-values/by-parent") && resp.Request.Method == "GET",
            new() { Timeout = 30_000 });

        await genderRow.GetByRole(AriaRole.Link, new() { Name = "View children" }).ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await childrenResponseTask;

        var grid = Page.GetByRole(AriaRole.Grid);
        await Expect(grid).ToBeVisibleAsync();

        var gridBox = await grid.BoundingBoxAsync();
        gridBox.Should().NotBeNull();
        gridBox!.Height.Should().BeGreaterThan(50, "the children grid should fill available vertical space");
    }

    [TestMethod]
    public async Task EditPage_RendersFormFields()
    {
        // First create a value, then navigate to its edit page
        await Page.GotoAsync("/coded-values");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var genderRow = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = "GENDER" });
        await Expect(genderRow).ToBeVisibleAsync(new() { Timeout = 30_000 });

        await genderRow.GetByRole(AriaRole.Link, new() { Name = "Edit" }).ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Edit page should render the form, not be blank
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 2 })).ToBeVisibleAsync();
        await Expect(Page.GetByLabel("Name")).ToBeVisibleAsync();
    }
}