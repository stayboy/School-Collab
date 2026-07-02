using FluentAssertions;
using Microsoft.Playwright;
using Microsoft.Playwright.MSTest;

namespace SchoolCollab.Config.Tests.Playwright.Tests;

/// <summary>
/// End-to-end smoke tests for the central Config feature-flag service driven
/// through the Admin UI. These assume the full Aspire AppHost is running (the
/// <c>/config-flags</c> page is served by <c>SchoolCollab.Admin</c>, which reaches
/// <c>config-api</c> via Aspire service discovery, and the seed migrator has
/// created <c>FEATURE:EnableCodedValuesAiChat</c>).
///
/// Set <c>PLAYWRIGHT_BASE_URL</c> to the Admin host URL shown in the Aspire
/// dashboard before running. The cross-service gating test additionally needs
/// <c>PLAYWRIGHT_CODEDVALUES_URL</c>.
/// </summary>
[TestClass]
public class ConfigFlagsAdminSmokeTests : PageTest
{
    private const string SeededFlagKey = "FEATURE:EnableCodedValuesAiChat";

    private string AdminUrl => PlaywrightSettings.AdminUrl;

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        BaseURL = AdminUrl,
        IgnoreHTTPSErrors = true,
        ViewportSize = new ViewportSize { Width = 1280, Height = 900 }
    };

    /// <summary>
    /// The headline scenario: toggle the seeded AI-chat flag off through the
    /// admin UI, confirm an audit row is written, then toggle it back on.
    /// Screenshots capture the audit trail at each state for the PR.
    /// </summary>
    [TestMethod]
    public async Task ToggleSeededFlag_WritesAuditRow_AndRestores()
    {
        await Page.GotoAsync("/config-flags");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // The list page must show the seeded flag.
        await Expect(Page.GetByText(SeededFlagKey)).ToBeVisibleAsync();

        // Open the seeded flag's detail page via the row's "Open" button.
        var row = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = SeededFlagKey });
        await row.GetByRole(AriaRole.Button, new() { Name = "Open" }).ClickAsync();

        // Detail page renders the "Default state" card.
        await Expect(Page.GetByText("Default state")).ToBeVisibleAsync();
        var enabledSwitch = Page.GetByLabel("Enabled by default");
        var reasonField = Page.GetByLabel("Reason (required for save)");
        var saveButton = Page.GetByRole(AriaRole.Button, new() { Name = "Save" });

        // Read the current switch state so we can flip it and restore it later.
        var initialChecked = await enabledSwitch.GetAttributeAsync("aria-checked");
        var initiallyOn = initialChecked == "true";

        // ── Flip to the opposite state ──
        if (initiallyOn)
        {
            await enabledSwitch.UncheckAsync();
        }
        else
        {
            await enabledSwitch.CheckAsync();
        }
        const string offReason = "playwright smoke: disable for test";
        await reasonField.FillAsync(offReason);
        await saveButton.ClickAsync();

        // After Save, the page reloads its data; the audit grid must show the
        // transition and our reason.
        await Expect(Page.GetByText(offReason)).ToBeVisibleAsync(new() { Timeout = 10_000 });
        var transition = initiallyOn ? "On -> Off" : "Off -> On";
        await Expect(Page.GetByText(transition)).ToBeVisibleAsync();
        await ScreenshotAsync("config-flag-toggled-off");

        // ── Restore the original state ──
        if (initiallyOn)
        {
            await enabledSwitch.CheckAsync();
        }
        else
        {
            await enabledSwitch.UncheckAsync();
        }
        const string restoreReason = "playwright smoke: restore original state";
        await reasonField.FillAsync(restoreReason);
        await saveButton.ClickAsync();

        await Expect(Page.GetByText(restoreReason)).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await ScreenshotAsync("config-flag-restored");

        // Final switch state matches the initial state.
        var finalChecked = await enabledSwitch.GetAttributeAsync("aria-checked");
        finalChecked.Should().Be(initialChecked);
    }

    /// <summary>
    /// Cross-service gating smoke: toggling <c>FEATURE:EnableCodedValuesAiChat</c>
    /// off in the Config admin UI must hide the "✨ Chat" affordance on the
    /// CodedValues landing page, and toggling it back on must restore it.
    /// Requires <c>PLAYWRIGHT_CODEDVALUES_URL</c> to point at the running
    /// CodedValues host; skipped (Inconclusive) when it is unreachable so the
    /// admin-UI smoke above still runs standalone.
    /// </summary>
    [TestMethod]
    public async Task TogglingFlag_HidesAndRestoresCodedValuesChat()
    {
        var codedValuesUrl = PlaywrightSettings.CodedValuesUrl;

        // Baseline: the flag is on and the chat affordance is visible.
        await EnsureFlagEnabledAsync();

        await Page.GotoAsync($"{codedValuesUrl}/coded-values");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var chatButton = Page.GetByRole(AriaRole.Button, new() { Name = "✨ Chat" });
        await Expect(chatButton).ToBeVisibleAsync();

        // Disable the flag via the Config admin UI.
        await Page.GotoAsync($"{AdminUrl}/config-flags");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await SetFlagEnabledAsync(enabled: false, reason: "playwright smoke: hide chat");

        // The cached flag client has an L1 of ~30s; wait it out, then the chat
        // affordance must disappear.
        await Page.GotoAsync($"{codedValuesUrl}/coded-values");
        await Expect(chatButton).ToBeHiddenAsync(new() { Timeout = 45_000 });
        await ScreenshotAsync("coded-values-chat-hidden");

        // Re-enable and confirm the chat returns.
        await Page.GotoAsync($"{AdminUrl}/config-flags");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await SetFlagEnabledAsync(enabled: true, reason: "playwright smoke: show chat");

        await Page.GotoAsync($"{codedValuesUrl}/coded-values");
        await Expect(chatButton).ToBeVisibleAsync(new() { Timeout = 45_000 });
        await ScreenshotAsync("coded-values-chat-restored");
    }

    private async Task EnsureFlagEnabledAsync()
    {
        await Page.GotoAsync($"{AdminUrl}/config-flags");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await SetFlagEnabledAsync(enabled: true, reason: "playwright smoke: ensure on");
    }

    private async Task SetFlagEnabledAsync(bool enabled, string reason)
    {
        var row = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = SeededFlagKey });
        await row.GetByRole(AriaRole.Button, new() { Name = "Open" }).ClickAsync();
        await Expect(Page.GetByText("Default state")).ToBeVisibleAsync();

        var enabledSwitch = Page.GetByLabel("Enabled by default");
        var current = await enabledSwitch.GetAttributeAsync("aria-checked") == "true";
        if (current != enabled)
        {
            if (enabled)
            {
                await enabledSwitch.CheckAsync();
            }
            else
            {
                await enabledSwitch.UncheckAsync();
            }
            await Page.GetByLabel("Reason (required for save)").FillAsync(reason);
            await Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
            await Expect(Page.GetByText(reason)).ToBeVisibleAsync(new() { Timeout = 10_000 });
        }
    }

    private async Task ScreenshotAsync(string name)
    {
        Directory.CreateDirectory("screenshots");
        await Page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine("screenshots", $"{name}.png"),
            FullPage = true
        });
    }
}