using FluentAssertions;
using Microsoft.Playwright;
using Microsoft.Playwright.MSTest;

namespace SchoolCollab.CodedValues.Tests.Playwright.Tests;

/// <summary>
/// Regression tests for the AI assistant chat on the Coded Values landing page.
///
/// Covers two reported bugs:
///   1. Pressing Enter in the chat textarea did not submit the prompt.
///   2. The side-drawer chat panel failed to pop open on the second and
///      subsequent prompts after the user closed it between sends.
///
/// Root cause of bug 1: <c>FluentTextArea</c> does not declare an <c>OnKeyDown</c>
/// parameter, so the attribute flows through <c>AdditionalAttributes</c> and
/// the runtime handler receives <see cref="KeyboardEventArgs"/> (where
/// <c>e.Key</c> is a <see cref="string"/>), not <c>FluentKeyCodeEventArgs"/>
/// (where <c>e.Key</c> is the <c>KeyCode</c> enum). The old code compared a
/// string to an enum, so the submit branch never matched.
///
/// Root cause of bug 2: <c>SideDrawer.OnParametersSetAsync</c> only acted on
/// transitions of <c>Open</c>; any internal desync left the drawer stuck.
/// The fix unconditionally syncs the internal mirror to the parameter value.
/// </summary>
[TestClass]
public class CodedValuesChatPanelTests : PageTest
{
    private string BaseUrl => PlaywrightSettings.BaseUrl;

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        BaseURL = BaseUrl,
        IgnoreHTTPSErrors = true,
        ViewportSize = new ViewportSize { Width = 1280, Height = 720 }
    };

    private ILocator ChatInput => Page.GetByLabel("Chat input");
    private ILocator DrawerDialog => Page.GetByRole(AriaRole.Dialog, new() { Name = "✨ AI Assistant" });
    private ILocator DrawerCloseButton => DrawerDialog.GetByRole(AriaRole.Button, new() { Name = "Close drawer" });

    /// <summary>
    /// Bug 1 regression: pressing Enter on the chat textarea must submit the
    /// prompt. The deterministic, network-independent signal that the handler
    /// ran is that the input value is cleared. With the bug present, the
    /// input keeps its text and nothing is sent.
    /// </summary>
    [TestMethod]
    public async Task EnterKey_InChatInput_SubmitsPrompt()
    {
        await Page.GotoAsync("/coded-values");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Pre-condition: drawer closed, input empty.
        await Expect(DrawerDialog).ToBeHiddenAsync();
        await Expect(ChatInput).ToHaveValueAsync(string.Empty);

        const string prompt = "Test prompt via Enter key";
        await ChatInput.FillAsync(prompt);
        await Expect(ChatInput).ToHaveValueAsync(prompt);

        await ChatInput.PressAsync("Enter");

        // After Enter: input must clear (SendAsync ran) OR the drawer must
        // open (OnPromptSent fired). With the bug present, neither happens.
        // We assert the input is cleared as the strongest signal that doesn't
        // require a live AI service to respond.
        await Expect(ChatInput).ToHaveValueAsync(string.Empty, new() { Timeout = 5_000 });
    }

    /// <summary>
    /// Bug 1 follow-up: pressing Shift+Enter must insert a newline (NOT
    /// submit). With the buggy code the modifier check was also broken, so
    /// Shift+Enter silently did nothing.
    /// </summary>
    [TestMethod]
    public async Task ShiftEnter_InChatInput_InsertsNewlineWithoutSubmitting()
    {
        await Page.GotoAsync("/coded-values");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        const string firstLine = "Line one";
        await ChatInput.FillAsync(firstLine);

        await ChatInput.PressAsync("Shift+Enter");
        await ChatInput.PressSequentiallyAsync("Line two");

        // Input must contain both lines (newline preserved) and NOT be cleared.
        var value = await ChatInput.InputValueAsync();
        value.Should().Contain(firstLine);
        value.Should().Contain("Line two");
        value.Should().Contain("\n");

        // Drawer must not have opened (no submit happened).
        await Expect(DrawerDialog).ToBeHiddenAsync();
    }

    /// <summary>
    /// Bug 1 follow-up: pressing Ctrl+Enter must also insert a newline
    /// (the new behaviour added alongside the submit fix).
    /// </summary>
    [TestMethod]
    public async Task CtrlEnter_InChatInput_InsertsNewlineWithoutSubmitting()
    {
        await Page.GotoAsync("/coded-values");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await ChatInput.FillAsync("Line one");
        await ChatInput.PressAsync("Control+Enter");
        await ChatInput.PressSequentiallyAsync("Line two");

        var value = await ChatInput.InputValueAsync();
        value.Should().Contain("Line one");
        value.Should().Contain("Line two");
        value.Should().Contain("\n");

        await Expect(DrawerDialog).ToBeHiddenAsync();
    }

    /// <summary>
    /// Bug 2 regression: the side-drawer chat panel must pop open on the
    /// second prompt just as it did on the first. With the bug, the
    /// transitional state machine in <c>SideDrawer.OnParametersSetAsync</c>
    /// could miss the second Open=true transition, leaving the drawer stuck.
    /// </summary>
    [TestMethod]
    public async Task Drawer_PopsOpen_OnEverySend_AfterManualClose()
    {
        await Page.GotoAsync("/coded-values");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // --- 1st prompt ---
        await ChatInput.FillAsync("First prompt");
        await ChatInput.PressAsync("Enter");
        await Expect(ChatInput).ToHaveValueAsync(string.Empty, new() { Timeout = 5_000 });
        await Expect(DrawerDialog).ToBeVisibleAsync(new() { Timeout = 5_000 });

        // Close the drawer manually (× button).
        await DrawerCloseButton.ClickAsync();
        await Expect(DrawerDialog).ToBeHiddenAsync(new() { Timeout = 5_000 });

        // --- 2nd prompt ---
        await ChatInput.FillAsync("Second prompt after close");
        await ChatInput.PressAsync("Enter");
        await Expect(ChatInput).ToHaveValueAsync(string.Empty, new() { Timeout = 5_000 });
        await Expect(DrawerDialog).ToBeVisibleAsync(new() { Timeout = 5_000 });

        // Close again.
        await DrawerCloseButton.ClickAsync();
        await Expect(DrawerDialog).ToBeHiddenAsync(new() { Timeout = 5_000 });

        // --- 3rd prompt (extra coverage) ---
        await ChatInput.FillAsync("Third prompt after close");
        await ChatInput.PressAsync("Enter");
        await Expect(ChatInput).ToHaveValueAsync(string.Empty, new() { Timeout = 5_000 });
        await Expect(DrawerDialog).ToBeVisibleAsync(new() { Timeout = 5_000 });
    }

    /// <summary>
    /// Bug 2 follow-up: the Cancel button (Close) on the drawer footer must
    /// also leave the drawer in a state that can be reopened by a subsequent
    /// prompt. Tests the same re-opening path as above but via the footer
    /// button rather than the header × button.
    /// </summary>
    [TestMethod]
    public async Task Drawer_Reopens_AfterCancelButtonClose()
    {
        await Page.GotoAsync("/coded-values");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await ChatInput.FillAsync("First prompt via Send");
        await ChatInput.PressAsync("Enter");
        await Expect(DrawerDialog).ToBeVisibleAsync(new() { Timeout = 5_000 });

        // Footer Close (Cancel) button.
        var closeButton = DrawerDialog.GetByRole(AriaRole.Button, new() { Name = "Close" });
        await closeButton.ClickAsync();
        await Expect(DrawerDialog).ToBeHiddenAsync(new() { Timeout = 5_000 });

        // Second prompt must reopen.
        await ChatInput.FillAsync("Second prompt via Send");
        await ChatInput.PressAsync("Enter");
        await Expect(DrawerDialog).ToBeVisibleAsync(new() { Timeout = 5_000 });
    }
}