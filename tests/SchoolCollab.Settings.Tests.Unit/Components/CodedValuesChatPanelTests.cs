using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Settings.Admin.Components.Pages.CodedValues;
using SchoolCollab.Settings.Admin.Services;

namespace SchoolCollab.Settings.Tests.Unit.Components;

/// <summary>
/// bUnit tests for the AI assistant <see cref="CodedValuesChatPanel"/> —
/// the side-drawer surface that mirrors the inline chat's conversation into
/// a read-only <see cref="CodedValuesChat"/> bound to
/// <see cref="CodedValuesChatHub"/>.
///
/// Regression coverage for the bug where messages appended to the hub after
/// the user submitted a prompt failed to appear in the drawer. Root cause:
/// <see cref="CodedValuesChatPanel"/> assigned its
/// <c>_mirroredMessages</c> field directly from
/// <see cref="CodedValuesChatHub.Messages"/>, which is the hub's internal
/// <see cref="List{T}"/> reference. <see cref="CodedValuesChatHub.AddMessage"/>
/// mutates that list in place, so the reference never changed. Blazor's
/// parameter diffing uses reference equality for reference-type parameters,
/// so it saw "no change" and skipped re-rendering the child chat — leaving
/// new user/assistant messages invisible.
///
/// The fix snapshots the hub's messages into a fresh list (via
/// <see cref="Enumerable.ToList{TSource}(IEnumerable{TSource})"/>) on every
/// change so each update produces a new reference and the child chat
/// re-renders.
/// </summary>
[TestClass]
public class CodedValuesChatPanelTests : BunitContext
{
    [TestInitialize]
    public void Setup()
    {
        Services.AddFluentUIComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
        // The panel injects the hub and the inner chat injects AiChatClient.
        // Singleton scope is fine for tests — bUnit runs each test in its
        // own BunitContext so they don't share state.
        Services.AddSingleton<CodedValuesChatHub>();
        // AiChatClient backed by a stub handler that serves /api/ai/config and
        // a one-chunk SSE stream for /api/ai/chat, so tests that drive the
        // drawer's own Send button get a deterministic AI response without a
        // live AI service. Tests that never send are unaffected.
        Services.AddHttpClient<AiChatClient>(c => c.BaseAddress = new Uri("http://localhost"))
            .ConfigurePrimaryHttpMessageHandler(() => new StubAiHandler());
    }

    /// <summary>
    /// Bug regression: when a user prompt is added to the hub (mirrored
    /// from the inline chat via <c>OnMessageAdded</c>), the prompt must
    /// appear in the drawer's display chat. Previously the panel kept a
    /// direct reference to the hub's internal list, so Blazor's parameter
    /// diffing saw no change and the child chat never re-rendered.
    /// </summary>
    [TestMethod]
    public void Drawer_NewMessageInHub_RendersInChildChat()
    {
        // Arrange: open the drawer with an empty hub.
        var hub = Services.GetRequiredService<CodedValuesChatHub>();
        var cut = Render<CodedValuesChatPanel>(parameters => parameters
            .Add(p => p.Open, true));
        var drawer = cut.Find("aside.side-drawer-panel");
        drawer.Should().NotBeNull();

        // Pre-condition: hub is empty, so no message rows are rendered.
        cut.FindAll(".chat-message").Should().BeEmpty();

        // Act: simulate the inline chat pushing a user prompt to the hub.
        hub.AddMessage(new CodedValuesChat.ChatMessageItem(ChatRole.User, "Hello, AI"));

        // Assert: the prompt now appears in the drawer's display chat.
        cut.WaitForState(() => cut.FindAll(".chat-message").Count > 0);
        cut.FindAll(".chat-message").Should().HaveCount(1);
        cut.Find(".chat-message.message-user .message-text").TextContent
            .Should().Contain("Hello, AI");
    }

    /// <summary>
    /// Bug regression follow-up: the mirror must keep working as more
    /// messages arrive in sequence. With the bug, the first message could
    /// conceivably render on a fresh component instance (where the snapshot
    /// was taken at construction time) but subsequent mutations on the same
    /// reference would silently fail. This test pins down the per-update
    /// re-render path.
    /// </summary>
    [TestMethod]
    public void Drawer_SubsequentMessages_RenderInOrder()
    {
        var hub = Services.GetRequiredService<CodedValuesChatHub>();
        var cut = Render<CodedValuesChatPanel>(parameters => parameters
            .Add(p => p.Open, true));

        // Round 1 — user prompt.
        hub.AddMessage(new CodedValuesChat.ChatMessageItem(ChatRole.User, "First"));
        cut.WaitForState(() => cut.FindAll(".chat-message").Count == 1);
        cut.Find(".message-user .message-text").TextContent.Should().Contain("First");

        // Round 2 — assistant response.
        hub.AddMessage(new CodedValuesChat.ChatMessageItem(ChatRole.Assistant, "Hi there"));
        cut.WaitForState(() => cut.FindAll(".chat-message").Count == 2);

        // Both messages present and in order.
        var messages = cut.FindAll(".chat-message");
        messages.Should().HaveCount(2);
        messages[0].GetAttribute("class").Should().Contain("message-user");
        messages[0].QuerySelector(".message-text")!.TextContent.Should().Contain("First");
        messages[1].GetAttribute("class").Should().Contain("message-assistant");
        messages[1].QuerySelector(".message-text")!.TextContent.Should().Contain("Hi there");
    }

    /// <summary>
    /// Bug regression follow-up: <see cref="CodedValuesChatHub.Clear"/> must
    /// also reach the child chat so the drawer empties when the user
    /// presses the drawer's Clear button.
    /// </summary>
    [TestMethod]
    public void Drawer_HubClear_EmptiesChildChat()
    {
        var hub = Services.GetRequiredService<CodedValuesChatHub>();
        var cut = Render<CodedValuesChatPanel>(parameters => parameters
            .Add(p => p.Open, true));

        hub.AddMessage(new CodedValuesChat.ChatMessageItem(ChatRole.User, "Before clear"));
        cut.WaitForState(() => cut.FindAll(".chat-message").Count == 1);

        hub.Clear();
        cut.WaitForState(() => cut.FindAll(".chat-message").Count == 0);

        cut.FindAll(".chat-message").Should().BeEmpty();
    }

    /// <summary>
    /// Streaming state must also reach the drawer so the user sees the
    /// "AI is typing…" indicator while a response streams in. Mirrors
    /// what <see cref="CodedValuesChat"/> pushes to the hub via
    /// <c>OnStreamingStateChanged</c>.
    /// </summary>
    [TestMethod]
    public void Drawer_StreamingState_RendersTypingIndicator()
    {
        var hub = Services.GetRequiredService<CodedValuesChatHub>();
        var cut = Render<CodedValuesChatPanel>(parameters => parameters
            .Add(p => p.Open, true));

        hub.AddMessage(new CodedValuesChat.ChatMessageItem(ChatRole.User, "Go"));
        cut.WaitForState(() => cut.FindAll(".chat-message").Count == 1);

        // Streaming begins.
        hub.SetStreamingState(new CodedValuesChat.ChatStreamingState(
            IsStreaming: true, StreamingText: string.Empty, ActiveToolCalls: null));

        cut.WaitForState(() => cut.FindAll(".streaming-container").Count == 1);
        cut.Find(".streaming-container").Should().NotBeNull();

        // Partial text arrives.
        hub.SetStreamingState(new CodedValuesChat.ChatStreamingState(
            IsStreaming: true, StreamingText: "Partial", ActiveToolCalls: null));

        cut.WaitForState(() => cut.Find(".streaming-text") != null);
        cut.Find(".streaming-text").TextContent.Should().Contain("Partial");

        // Streaming ends.
        hub.SetStreamingState(new CodedValuesChat.ChatStreamingState(
            IsStreaming: false, StreamingText: string.Empty, ActiveToolCalls: null));

        cut.WaitForState(() => cut.FindAll(".streaming-container").Count == 0);
    }

    /// <summary>
    /// Regression for "input hidden in panel": the drawer must host a visible,
    /// WORKING input. Prompting directly from the drawer's textbox must add the
    /// user's message and stream the AI response into the drawer — without the
    /// inline page chat. The drawer chat is the Full-mode
    /// <see cref="CodedValuesChat"/> hosted by <see cref="CodedValuesChatPanel"/>,
    /// wired to mirror through the hub.
    /// </summary>
    [TestMethod]
    public async Task DrawerInput_Submit_RendersUserPromptAndAiResponse()
    {
        var cut = Render<CodedValuesChatPanel>(parameters => parameters
            .Add(p => p.Open, true));
        cut.WaitForElement(".input-area");

        // The drawer's Full-mode chat is the single CodedValuesChat here.
        var drawerChat = cut.FindComponent<CodedValuesChat>();
        var field = typeof(CodedValuesChat).GetField("_inputText",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field!.SetValue(drawerChat.Instance, "Create a Country coded value");

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
}

/// <summary>
/// Minimal handler that serves the two endpoints the drawer chat hits when
/// it sends: GET /api/ai/config (provider/model) and POST /api/ai/chat (SSE
/// stream with one text chunk). Used by <see cref="CodedValuesChatPanelTests"/>.
/// </summary>
file sealed class StubAiHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        HttpResponseMessage response;
        if (request.RequestUri?.AbsolutePath == "/api/ai/config")
        {
            response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"defaultProvider":"ollama","defaultModel":"test-model"}""",
                    System.Text.Encoding.UTF8, "application/json")
            };
        }
        else if (request.RequestUri?.AbsolutePath == "/api/ai/chat")
        {
            var sse =
                "event: TextChunk\r\n" +
                "data: {\"text\":\"Hello from the AI\"}\r\n" +
                "\r\n";
            response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(sse, System.Text.Encoding.UTF8, "text/event-stream")
            };
        }
        else
        {
            response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json")
            };
        }
        return Task.FromResult(response);
    }
}
