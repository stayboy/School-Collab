using System.Net;
using System.Reflection;
using System.Text;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Settings.Admin.Components.Pages.CodedValues;
using SchoolCollab.Settings.Admin.Services;
using SchoolCollab.Core.Features;

namespace SchoolCollab.Settings.Tests.Unit.Components;

/// <summary>
/// End-to-end bUnit tests that render the full Coded Values <see cref="Index"/>
/// page and drive the inline <see cref="CodedValuesChat"/> (InputOnly) the way
/// a user does, then assert the conversation is mirrored into the
/// <see cref="CodedValuesChatPanel"/> drawer.
///
/// These tests pin down the wiring reported broken — "panel does not show
/// user response, neither ai response" — by exercising the real event path:
/// inline chat SendAsync → OnMessageAdded/OnStreamingStateChanged → page
/// handlers → <see cref="CodedValuesChatHub"/> → panel subscription → child
/// chat re-render. The AI HTTP call is stubbed with an SSE stream so no live
/// AI service is required.
/// </summary>
[TestClass]
public class CodedValuesIndexChatMirrorTests : BunitContext
{
    [TestInitialize]
    public void Setup()
    {
        Services.AddFluentUIComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddScoped<CodedValuesChatHub>();
        Services.AddSingleton<IFeatureFlagService, AlwaysOnFeatureFlagService>();

        var handler = new StubHttpHandler();
        Services.AddSingleton(handler);

        Services.AddHttpClient<CodedValuesApiClient>(c => c.BaseAddress = new Uri("http://localhost"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        Services.AddHttpClient<AiChatClient>(c => c.BaseAddress = new Uri("http://localhost"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
    }

    /// <summary>
    /// Submitting a prompt from the inline page chat must (a) open the drawer
    /// and (b) render the user's prompt in the drawer's display chat. This is
    /// the minimum "I can see my own question" signal.
    /// </summary>
    [TestMethod]
    public async Task InlineSubmit_UserPromptAppearsInDrawer()
    {
        var cut = Render<CodedValuesPageHost>();
        cut.WaitForElement(".input-area");

        var inlineChat = cut.FindComponent<CodedValuesChat>();
        SetInputText(inlineChat.Instance, "Create a Country coded value");

        // Submit from the inline (page) chat — same path the Enter key takes.
        await cut.InvokeAsync(async () => await inlineChat.Instance.SubmitFromKeyAsync());
        cut.WaitForState(() => cut.FindAll("aside.side-drawer-panel").Count > 0);

        // The user's prompt must be mirrored into the drawer.
        cut.WaitForState(() => cut.FindAll(".chat-message.message-user").Count > 0);
        cut.FindAll(".chat-message.message-user").Should().HaveCount(1);
        cut.Find(".chat-message.message-user .message-text").TextContent
            .Should().Contain("Create a Country coded value");
    }

    /// <summary>
    /// The AI response (stubbed SSE text chunk) must also be mirrored into
    /// the drawer after the stream completes — the original "does not show
    /// ai response" complaint.
    /// </summary>
    [TestMethod]
    public async Task InlineSubmit_AiResponseAppearsInDrawer()
    {
        var cut = Render<CodedValuesPageHost>();
        cut.WaitForElement(".input-area");

        var inlineChat = cut.FindComponent<CodedValuesChat>();
        SetInputText(inlineChat.Instance, "Hello AI");

        await cut.InvokeAsync(async () => await inlineChat.Instance.SubmitFromKeyAsync());

        cut.WaitForState(() => cut.FindAll(".chat-message.message-assistant").Count > 0,
            TimeSpan.FromSeconds(5));
        cut.FindAll(".chat-message.message-assistant").Should().HaveCount(1);
        cut.Find(".chat-message.message-assistant .message-text").TextContent
            .Should().Contain("Hello from the AI");
    }

    /// <summary>
    /// Regression for "input hidden in panel": the drawer must host a visible,
    /// WORKING input. Prompting directly from the drawer's textbox must add the
    /// user's message and stream the AI response into the drawer — without
    /// relying on the inline page chat. The drawer chat is the Full-mode
    /// <see cref="CodedValuesChat"/> hosted by <see cref="CodedValuesChatPanel"/>.
    /// </summary>
    [TestMethod]
    public async Task DrawerInput_UserPromptAndAiResponseAppearInDrawer()
    {
        var cut = Render<CodedValuesPageHost>();
        cut.WaitForElement(".input-area");

        // Open the drawer via the page's ✨ Chat button (no prompt yet).
        // FluentButton renders as a <fluent-button> custom element, so query
        // that tag rather than "button".
        var chatButton = cut.FindAll("fluent-button").First(b => b.TextContent.Contains("Chat"));
        await cut.InvokeAsync(() => chatButton.Click());
        cut.WaitForState(() => cut.FindAll("aside.side-drawer-panel").Count > 0);

        // The drawer's Full-mode chat is the second CodedValuesChat in the tree
        // (the first is the inline InputOnly chat on the page).
        var drawerChat = cut.FindComponents<CodedValuesChat>()
            .Single(c => c.Instance.Mode == CodedValuesChat.CodedValuesChatMode.Full);
        SetInputText(drawerChat.Instance, "Create a Country coded value");

        await cut.InvokeAsync(async () => await drawerChat.Instance.SubmitFromKeyAsync());

        // The user's prompt appears in the drawer.
        cut.WaitForState(() => cut.FindAll(".chat-message.message-user").Count > 0);
        cut.Find(".chat-message.message-user .message-text").TextContent
            .Should().Contain("Create a Country coded value");

        // …and so does the streamed AI response.
        cut.WaitForState(() => cut.FindAll(".chat-message.message-assistant").Count > 0,
            TimeSpan.FromSeconds(5));
        cut.Find(".chat-message.message-assistant .message-text").TextContent
            .Should().Contain("Hello from the AI");
    }

    private static void SetInputText(CodedValuesChat chat, string text)
    {
        // FluentTextArea binds to _inputText via @bind-Value. Driving the web
        // component from bUnit is unreliable, so set the backing field
        // directly — the value is what SendAsync reads.
        var field = typeof(CodedValuesChat).GetField("_inputText",
            BindingFlags.NonPublic | BindingFlags.Instance);
        field.Should().NotBeNull();
        field!.SetValue(chat, text);
    }

    /// <summary>
    /// Minimal handler that serves the two endpoints the page/chat hit on
    /// load + send: GET /api/ai/config (provider/model) and POST /api/ai/chat
    /// (SSE stream with one text chunk). GET /coded-values returns an empty
    /// array so the grid renders without error.
    /// </summary>
    private sealed class StubHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            HttpResponseMessage response;
            if (request.RequestUri?.AbsolutePath == "/api/ai/config")
            {
                response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"defaultProvider":"ollama","defaultModel":"test-model"}""",
                        Encoding.UTF8, "application/json")
                };
            }
            else if (request.RequestUri?.AbsolutePath == "/api/ai/chat")
            {
                var sse =
                    "event: TextChunk\r\n" +
                    """data: {"text":"Hello from the AI"}""" + "\r\n" +
                    "\r\n";
                response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(sse, Encoding.UTF8, "text/event-stream")
                };
            }
            else
            {
                // /coded-values and any other GET → empty array / 404-safe body.
                response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json")
                };
            }
            return Task.FromResult(response);
        }
    }
}