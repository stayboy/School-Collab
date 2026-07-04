using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Playwright;
using Microsoft.Playwright.MSTest;

namespace SchoolCollab.Settings.Tests.Playwright.Tests;

[TestClass]
public class CodedValuesAdminTests : PageTest
{
    private string BaseUrl => PlaywrightSettings.BaseUrl;

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        BaseURL = BaseUrl,
        IgnoreHTTPSErrors = true,
        ViewportSize = new ViewportSize { Width = 1280, Height = 720 }
    };

    [TestMethod]
    public async Task RootPage_LoadsCodedValuesIndex()
    {
        await Page.GotoAsync("/");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Expect(Page).ToHaveTitleAsync(new Regex("Coded Values", RegexOptions.IgnoreCase));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Coded Values" })).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task CodedValuesPage_NavigationWorks()
    {
        await Page.GotoAsync("/coded-values");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Expect(Page).ToHaveTitleAsync(new Regex("Coded Values", RegexOptions.IgnoreCase));
    }

    [TestMethod]
    public async Task CodedValuesPage_ShowsNewCategoryButton()
    {
        await Page.GotoAsync("/coded-values");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var button = Page.GetByRole(AriaRole.Button, new() { Name = "New Category" });
        await Expect(button).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task CreateCodedValue_FullFlow()
    {
        var uniqueCode = $"PWTEST{Guid.NewGuid().ToString("N")[..8]}".ToUpperInvariant();
        var uniqueName = $"Playwright Test {DateTime.Now.Ticks}";

        await Page.GotoAsync("/coded-values");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Page.GetByRole(AriaRole.Button, new() { Name = "New Category" }).ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Expect(Page).ToHaveTitleAsync(new Regex("New", RegexOptions.IgnoreCase));

        await Page.GetByLabel("Code").FillAsync(uniqueCode);
        await Page.GetByLabel("Name").FillAsync(uniqueName);
        await Page.GetByLabel("Description").FillAsync("Created by Playwright");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Expect(Page).ToHaveURLAsync(new Regex("/coded-values$"));

        var codeCell = Page.GetByRole(AriaRole.Cell, new() { Name = uniqueCode });
        await Expect(codeCell).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task DisableCodedValue_ShowsDisabledBadge()
    {
        await Page.GotoAsync("/coded-values");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var disableButton = Page.GetByRole(AriaRole.Button, new() { Name = "Disable" }).First;
        await disableButton.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var badge = Page.GetByText("Disabled").First;
        await Expect(badge).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task EditCodedValue_UpdatesName()
    {
        var code = $"EDIT{Guid.NewGuid().ToString("N")[..6]}".ToUpperInvariant();
        await Page.GotoAsync("/coded-values/new");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.GetByLabel("Code").FillAsync(code);
        await Page.GetByLabel("Name").FillAsync("Original Name");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var editLink = Page.Locator($"tr:has-text('{code}')").GetByRole(AriaRole.Link, new() { Name = "Edit" });
        await editLink.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var nameInput = Page.GetByLabel("Name");
        await nameInput.ClearAsync();
        await nameInput.FillAsync("Updated by Playwright");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var updatedCell = Page.GetByRole(AriaRole.Cell, new() { Name = "Updated by Playwright" });
        await Expect(updatedCell).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task ChildrenPage_ShowsParentBreadcrumb()
    {
        var parentCode = $"BREADCRUMB{Guid.NewGuid().ToString("N")[..4]}".ToUpperInvariant();
        await Page.GotoAsync("/coded-values/new");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.GetByLabel("Code").FillAsync(parentCode);
        await Page.GetByLabel("Name").FillAsync("Breadcrumb Parent");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Page.Locator($"tr:has-text('{parentCode}')").GetByRole(AriaRole.Link, new() { Name = "View children" }).ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var breadcrumb = Page.GetByRole(AriaRole.Navigation, new() { Name = "breadcrumb" });
        await Expect(breadcrumb).ToContainTextAsync("Categories");
        await Expect(breadcrumb).ToContainTextAsync("Breadcrumb Parent");
    }
}
