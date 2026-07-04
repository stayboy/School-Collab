using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Playwright;
using Microsoft.Playwright.MSTest;

namespace SchoolCollab.Settings.Tests.Playwright.Tests;

/// <summary>
/// Regression tests for the "The renderer does not have a component with ID N"
/// error reported on the coded-values landing page.
///
/// History:
///   The landing page previously opted into [StreamRendering(true)] for fast
///   first-paint. The streaming renderer is torn down as soon as the response
///   body finishes flushing, so a late OnInitializedAsync continuation (or an
///   optimistic-rollback StateHasChanged()) that ran after the user navigated
///   away mutated a disposed component and threw
///   "The renderer does not have a component with ID {N}" from
///   Renderer.GetRequiredComponentState.
///
///   The fix is to disable prerendering globally in App.razor — keeping the
///   circuit attached to the component for its full lifetime — and to guard
///   post-await state writes with a _disposed flag as defense in depth. These
///   tests exercise the race by routing the API call with a delay and then
///   navigating away before the load completes.
/// </summary>
[TestClass]
public class CodedValuesRendererRaceTests : PageTest
{
    private string BaseUrl => PlaywrightSettings.BaseUrl;

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        BaseURL = BaseUrl,
        IgnoreHTTPSErrors = true,
        ViewportSize = new ViewportSize { Width = 1280, Height = 720 }
    };

    private static readonly Regex RendererError = new(
        @"renderer does not have a component with ID \d+",
        RegexOptions.IgnoreCase);

    [TestMethod]
    public async Task NavigatingAwayDuringStreamRender_DoesNotThrowRendererError()
    {
        var consoleErrors = new ConcurrentQueue<string>();
        var pageErrors = new ConcurrentQueue<string>();

        Page.Console += (_, msg) =>
        {
            if (msg.Type == "error") consoleErrors.Enqueue(msg.Text);
        };
        Page.PageError += (_, message) => pageErrors.Enqueue(message);

        // Slow the /api/coded-values response so the streaming renderer is still
        // awaiting it when we navigate away — that's what makes the bug
        // reproducible.
        await Page.RouteAsync("**/api/coded-values**", async route =>
        {
            await Task.Delay(2_000);
            await route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = "[]"
            });
        });

        // Start the streaming page (don't await NetworkIdle — we want to
        // navigate while it's still streaming).
        var firstNavigation = Page.GotoAsync("/");

        // Give Blazor a beat to attach the renderer and stream the
        // <FluentProgressRing /> placeholder, but NOT enough time for the
        // 2-second-delayed API call to resolve.
        await Task.Delay(300);

        // Navigate away to a different page. The streaming renderer for "/"
        // is disposed while the OnInitializedAsync continuation is still
        // pending.
        var destination = "/coded-values/new";
        await Page.GotoAsync(destination);

        // Let the first navigation unwind so any deferred continuation has
        // had a chance to throw.
        try { await firstNavigation; } catch { /* expected: navigation aborted */ }

        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // 1) The destination page is the final URL.
        Page.Url.Should().EndWith(destination,
            "the second navigation should win and leave us on the destination page");

        // 2) The destination page actually rendered.
        var heading = Page.GetByRole(AriaRole.Heading, new() { Level = 2 });
        await Expect(heading).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // 3) No console / pageerror of the form the bug produces.
        var matchedConsole = consoleErrors.Where(m => RendererError.IsMatch(m)).ToList();
        var matchedPage    = pageErrors.Where(m => RendererError.IsMatch(m)).ToList();

        matchedConsole.Should().BeEmpty(
            "no 'renderer does not have a component with ID' errors should be logged to the console");
        matchedPage.Should().BeEmpty(
            "no uncaught 'renderer does not have a component with ID' exceptions should reach the browser");
    }

    [TestMethod]
    public async Task NavigatingBetweenAliasRoutesDuringStreamRender_DoesNotThrow()
    {
        // Same race, but navigating between the two @page routes that point
        // at the same Index.razor component. Stresses the dual-page-directive
        // path.
        var pageErrors = new ConcurrentQueue<string>();
        Page.PageError += (_, message) => pageErrors.Enqueue(message);

        await Page.RouteAsync("**/api/coded-values**", async route =>
        {
            await Task.Delay(2_000);
            await route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = "[]"
            });
        });

        var firstNavigation = Page.GotoAsync("/");
        await Task.Delay(300);
        await Page.GotoAsync("/coded-values");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        try { await firstNavigation; } catch { /* expected */ }

        pageErrors
            .Where(m => RendererError.IsMatch(m))
            .Should()
            .BeEmpty("navigating between two routes that map to the same streaming page must not throw");
    }

    [TestMethod]
    public async Task TogglingCodedValue_AfterNavigateAway_DoesNotThrowRendererError()
    {
        // Hit the landing page, click the first Disable button (which fires
        // OnToggleAsync and a background API call), then immediately navigate
        // away. The optimistic-rollback StateHasChanged() must not fire on a
        // disposed component.
        await Page.GotoAsync("/coded-values");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var pageErrors = new ConcurrentQueue<string>();
        Page.PageError += (_, message) => pageErrors.Enqueue(message);

        // Slow the Enable/Disable API call so the rollback path is the one
        // racing the navigation.
        await Page.RouteAsync("**/api/coded-values/*/disable", async route =>
        {
            await Task.Delay(1_500);
            await route.FulfillAsync(new() { Status = 204 });
        });
        await Page.RouteAsync("**/api/coded-values/*/enable", async route =>
        {
            await Task.Delay(1_500);
            await route.FulfillAsync(new() { Status = 204 });
        });

        var disableButton = Page.GetByRole(AriaRole.Button, new() { Name = "Disable" }).First;
        await disableButton.ClickAsync();

        // Navigate away before the (delayed) API call returns.
        await Task.Delay(200);
        await Page.GotoAsync("/coded-values/new");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        pageErrors
            .Where(m => RendererError.IsMatch(m))
            .Should()
            .BeEmpty("an in-flight optimistic-toggle rollback must not fire on a disposed component");
    }
}
