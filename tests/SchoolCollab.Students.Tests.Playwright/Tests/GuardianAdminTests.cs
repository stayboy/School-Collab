using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Playwright;
using Microsoft.Playwright.MSTest;

namespace SchoolCollab.Students.Tests.Playwright.Tests;

/// <summary>
/// Smoke tests for the guardian admin UI (student-guardian-plan.md Phase 4
/// "Tests"). Drives the unified SchoolCollab.Admin host that serves the
/// Students Admin Blazor pages. Requires the full AppHost running (Aspire
/// service discovery) + a tenant with seeded students for the student-detail
/// tab tests. These are smoke-level (page loads, headings, navigation, tab
/// visibility); the full guardian create-wizard flow is a follow-up once the
/// app is runnable in CI for selector verification.
/// </summary>
[TestClass]
public class GuardianAdminTests : PageTest
{
    private string BaseUrl => PlaywrightSettings.AdminUrl;

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        BaseURL = BaseUrl,
        IgnoreHTTPSErrors = true,
        ViewportSize = new ViewportSize { Width = 1280, Height = 720 }
    };

    [TestMethod]
    public async Task GuardiansPage_LoadsIndex()
    {
        await Page.GotoAsync("/students/guardians");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Guardians" })).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task GuardiansPage_ShowsCreateGuardianButton()
    {
        await Page.GotoAsync("/students/guardians");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var create = Page.GetByRole(AriaRole.Button, new() { Name = "Create" });
        await Expect(create).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task GuardianSetupWizard_OpensFromCreate()
    {
        await Page.GotoAsync("/students/guardians");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Wizard route is /students/guardians/create.
        await Expect(Page).ToHaveURLAsync(new Regex("/students/guardians/create", RegexOptions.IgnoreCase));
    }

    [TestMethod]
    public async Task StudentDetail_ShowsGuardiansAndContactsTabs()
    {
        // Requires at least one seeded student in the active tenant.
        await Page.GotoAsync("/students");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var firstStudentLink = Page.GetByRole(AriaRole.Link, new() { Name = "View" }).First;
        await firstStudentLink.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Expect(Page.GetByRole(AriaRole.Tab, new() { Name = "Guardians" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Tab, new() { Name = "Contacts" })).ToBeVisibleAsync();
    }
}