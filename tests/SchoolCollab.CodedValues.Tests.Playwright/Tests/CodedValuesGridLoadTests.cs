using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Playwright;
using Microsoft.Playwright.MSTest;

namespace SchoolCollab.CodedValues.Tests.Playwright.Tests;

/// <summary>
/// Verifies both FluentDataGrid instances in the CodedValues admin app
/// load and render their data: the root list (Index.razor) and the
/// children grid (Children.razor) reached by clicking "View children".
/// </summary>
[TestClass]
public class CodedValuesGridLoadTests : PageTest
{
    private string BaseUrl => PlaywrightSettings.BaseUrl;

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        BaseURL = BaseUrl,
        IgnoreHTTPSErrors = true,
        ViewportSize = new ViewportSize { Width = 1280, Height = 720 }
    };

    [TestMethod]
    public async Task RootGrid_LoadsAndRendersSeededRows()
    {
        // Wait for the API response that populates the grid
        var gridResponseTask = Page.WaitForResponseAsync(
            resp => resp.Url.Contains("/coded-values") && resp.Request.Method == "GET",
            new() { Timeout = 30_000 });

        await Page.GotoAsync("/coded-values");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var gridResponse = await gridResponseTask;
        gridResponse.Status.Should().Be(200);

        // The seeded root categories from seed.csv are GENDER and STATUS.
        // Both must be visible in the root grid.
        var gender = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = "GENDER" });
        var status = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = "STATUS" });

        await Expect(gender).ToBeVisibleAsync();
        await Expect(status).ToBeVisibleAsync();

        // Each seeded row carries an "Active" badge and a "View children" link
        await Expect(gender.GetByText("Active", new() { Exact = false })).ToBeVisibleAsync();
        await Expect(status.GetByText("Active", new() { Exact = false })).ToBeVisibleAsync();
        await Expect(gender.GetByRole(AriaRole.Link, new() { Name = "View children" })).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task ChildrenGrid_LoadsAfterNavigatingFromRoot()
    {
        await Page.GotoAsync("/coded-values");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Wait for the root list to render at least the GENDER row
        var genderRow = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = "GENDER" });
        await Expect(genderRow).ToBeVisibleAsync();

        // The "by-parent" call is triggered by the Children page on init.
        var childrenResponseTask = Page.WaitForResponseAsync(
            resp => resp.Url.Contains("/coded-values/by-parent") && resp.Request.Method == "GET",
            new() { Timeout = 30_000 });

        await genderRow.GetByRole(AriaRole.Link, new() { Name = "View children" }).ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var childrenResponse = await childrenResponseTask;
        childrenResponse.Status.Should().Be(200);

        // Page title and breadcrumb should reflect the parent context
        await Expect(Page).ToHaveTitleAsync(new Regex("Children of", RegexOptions.IgnoreCase));

        var breadcrumb = Page.GetByRole(AriaRole.Navigation, new() { Name = "breadcrumb" });
        await Expect(breadcrumb).ToContainTextAsync("Categories");
        await Expect(breadcrumb).ToContainTextAsync("Gender");

        // Seeded children of GENDER: MALE, FEMALE, OTHER
        var childrenGrid = Page.GetByRole(AriaRole.Grid);
        await Expect(childrenGrid).ToBeVisibleAsync();
        await Expect(childrenGrid.GetByRole(AriaRole.Row).Filter(new() { HasText = "GENDER_MALE" })).ToBeVisibleAsync();
        await Expect(childrenGrid.GetByRole(AriaRole.Row).Filter(new() { HasText = "GENDER_FEMALE" })).ToBeVisibleAsync();
        await Expect(childrenGrid.GetByRole(AriaRole.Row).Filter(new() { HasText = "GENDER_OTHER" })).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task RootGrid_ShowsLoadingThenRows()
    {
        await Page.GotoAsync("/coded-values");

        // Wait for at least one seeded row to render; the FluentProgressRing
        // is replaced by the grid as soon as the GET /coded-values resolves.
        var genderRow = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = "GENDER" });
        await Expect(genderRow).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // The data grid itself must be present and contain the header row + at least 2 data rows
        var grid = Page.GetByRole(AriaRole.Grid);
        await Expect(grid).ToBeVisibleAsync();

        var allRows = await grid.GetByRole(AriaRole.Row).CountAsync();
        allRows.Should().BeGreaterThan(2, "expected header row plus at least 2 seeded categories");
    }
}
