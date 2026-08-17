using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Playwright;
using Microsoft.Playwright.MSTest;

namespace SchoolCollab.Students.Tests.Playwright.Tests;

/// <summary>
/// Smoke tests for the contact edit/delete-with-reason + audit feature
/// (docs/plans/2026-08-17-contact-edit-delete-with-reason-and-audit.md).
///
/// Covers the two new surfaces end-to-end in a real browser:
///   - the student Detail page renders the "Contact history" section
///     (the append-only audit log of contact edits/deletes), and
///   - the student Edit page's ContactsEditor opens the ContactChangeDialog
///     (which requires a reason) and, on confirm, the audit entry is
///     surfaced on the Detail page.
///
/// PREREQUISITES (same as GuardianAdminTests):
///   - the full AppHost must be running (Aspire service discovery) so the
///     Admin host can reach the Students API;
///   - the active tenant must have at least one seeded student;
///   - TestAuth is active in dev (FEATURE:DisableOIDCAuth), so no login is
///     required.
///
/// Contacts are NOT seeded by the MigrationService, so the round-trip test
/// creates its own contact via the UI first to be deterministic.
/// </summary>
[TestClass]
public class ContactAuditSmokeTests : PageTest
{
    private string BaseUrl => PlaywrightSettings.AdminUrl;

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        BaseURL = BaseUrl,
        IgnoreHTTPSErrors = true,
        ViewportSize = new ViewportSize { Width = 1280, Height = 720 }
    };

    /// <summary>Opens the first student's detail page and returns its id.</summary>
    private async Task<Guid> OpenFirstStudentAsync()
    {
        await Page.GotoAsync("/students");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var firstView = Page.GetByRole(AriaRole.Link, new() { Name = "View" }).First;
        await firstView.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var url = Page.Url;
        var m = Regex.Match(url, @"/students/([0-9a-fA-F-]{36})");
        m.Success.Should().BeTrue($"expected a student detail URL, got '{url}'");
        return Guid.Parse(m.Groups[1].Value);
    }

    [TestMethod]
    public async Task StudentDetail_ShowsContactHistorySection()
    {
        var studentId = await OpenFirstStudentAsync();

        await Page.GotoAsync($"/students/{studentId}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // The new append-only audit log surface (§6.3) must render.
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Contact history" })).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task ContactEdit_OpensReasonDialog_AndPersistsAudit()
    {
        var studentId = await OpenFirstStudentAsync();

        // The ContactsEditor lives on the student Edit page (Live mode).
        await Page.GotoAsync($"/students/{studentId}/edit");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Contacts are not seeded, so add one via the UI to guarantee a row.
        var valueInput = Page.Locator("fluent-text-field.contacts-value");
        await valueInput.FillAsync("smoke-" + Guid.NewGuid().ToString("N")[..8] + "@example.com");
        await Page.Locator(".contacts-editor")
            .GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true })
            .ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Open the edit dialog for a contact row.
        await Page.GetByTitle("Edit contact").First.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // The ContactChangeDialog must collect a required reason. Scope to the
        // dialog so we don't collide with the page's own Save button.
        var dialog = Page.Locator("fluent-dialog");
        var reason = dialog.GetByPlaceholder("Why are you editing this contact?");
        await Expect(reason).ToBeVisibleAsync();

        // Submitting without a reason must be blocked (the dialog stays open).
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await Expect(reason).ToBeVisibleAsync();

        // Fill the reason and confirm.
        await reason.FillAsync("Smoke test: updated contact");
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // The dialog closes after a successful save.
        await Expect(dialog).ToHaveCountAsync(0);

        // The audit entry is surfaced on the student Detail page.
        await Page.GotoAsync($"/students/{studentId}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Contact history" })).ToBeVisibleAsync();
        await Expect(Page.GetByText("Smoke test: updated contact")).ToBeVisibleAsync();
    }
}
