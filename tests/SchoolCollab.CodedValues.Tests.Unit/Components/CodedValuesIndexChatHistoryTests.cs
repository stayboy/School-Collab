using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.CodedValues.Admin.Components.Pages.CodedValues;
using SchoolCollab.CodedValues.Admin.Services;

namespace SchoolCollab.CodedValues.Tests.Unit.Components;

/// <summary>
/// Multi-turn regression tests: verifies that subsequent prompts forwarded from
/// the inline InputOnly chat to the drawer's Full-mode chat carry the prior
/// conversation history (prompt + assistant response) into the AI request.
/// </summary>
[TestClass]
public class CodedValuesIndexChatHistoryTests : BunitContext
{
    [TestInitialize]
    public void Setup()
    {
        Services.AddFluentUIComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddScoped<CodedValuesChatHub>();

        _handler = new CapturingHandler();
        Services.AddSingleton(_handler);

        Services.AddHttpClient<SchoolCollab.Admin.Shared.Services.CodedValuesApiClient>(
            c => c.BaseAddress = new Uri("http://localhost"))
            .ConfigurePrimaryHttpMessageHandler(() => _handler);
        Services.AddHttpClient<AiChatClient>(c => c.BaseAddress = new Uri("http://localhost"))
            .ConfigurePrimaryHttpMessageHandler(() => _handler);
    }

    private CapturingHandler _handler = default!;

    [TestMethod]
    public async Task SubsequentPrompt_IncludesPriorPromptAndAssistantResponseInHistory()
    {
        var cut = Render<CodedValuesPageHost>();
        cut.WaitForElement(".input-area");

        var inlineChat = cut.FindComponent<CodedValuesChat>();
        SetInputText(inlineChat.Instance, "Add countries under CNTRY");
        await cut.InvokeAsync(async () => await inlineChat.Instance.SubmitFromKeyAsync());

        // Wait for the first AI turn to complete (assistant response mirrored).
        cut.WaitForState(() => cut.FindAll(".chat-message.message-assistant").Count > 0,
            TimeSpan.FromSeconds(5));
        _handler.ChatBodies.Should().HaveCount(1, "first prompt should fire one AI request");

        // Second prompt (follow-up) while the drawer is still open.
        SetInputText(inlineChat.Instance, "yes");
        await cut.InvokeAsync(async () => await inlineChat.Instance.SubmitFromKeyAsync());

        cut.WaitForState(() => _handler.ChatBodies.Count >= 2, TimeSpan.FromSeconds(5));

        // Inspect the history sent on the SECOND request.
        _handler.ChatBodies.Should().HaveCountGreaterThanOrEqualTo(2);
        var secondBody = _handler.ChatBodies[1];
        var doc = JsonDocument.Parse(secondBody);
        var messages = doc.RootElement.GetProperty("messages").EnumerateArray().ToList();

        // The second request must include the first user prompt, the assistant
        // response, and the second user prompt — i.e. full conversation history.
        messages.Should().Contain(m => m.GetProperty("role").GetString() == "user"
            && (m.GetProperty("text").GetString() ?? "").Contains("Add countries under CNTRY"),
            "second request must carry the first user prompt");
        messages.Should().Contain(m => m.GetProperty("role").GetString() == "assistant",
            "second request must carry the first assistant response");
        messages.Should().Contain(m => m.GetProperty("role").GetString() == "user"
            && (m.GetProperty("text").GetString() ?? "").Contains("yes"),
            "second request must carry the second user prompt");
    }

    [TestMethod]
    public async Task ReopenDrawerViaChatButton_DoesNotRefireLastPrompt()
    {
        // Regression: the SideDrawer unmounts the inner chat on close, resetting
        // its consumed-prompt tracker. A stale _pendingPrompt would otherwise be
        // re-fired as a brand-new prompt when the drawer is reopened via the ✨
        // Chat button. Index must clear _pendingPrompt on close.
        var cut = Render<CodedValuesPageHost>();
        cut.WaitForElement(".input-area");

        var inlineChat = cut.FindComponent<CodedValuesChat>();
        SetInputText(inlineChat.Instance, "Add countries under CNTRY");
        await cut.InvokeAsync(async () => await inlineChat.Instance.SubmitFromKeyAsync());
        cut.WaitForState(() => _handler.ChatBodies.Count >= 1, TimeSpan.FromSeconds(5));
        var requestsAfterFirst = _handler.ChatBodies.Count;

        // Close the drawer via the × dismiss button.
        cut.WaitForElement(".side-drawer-dismiss");
        await cut.InvokeAsync(() => cut.Find(".side-drawer-dismiss").Click());

        // Re-open via the ✨ Chat button (no new prompt).
        var chatButton = cut.FindAll("fluent-button").First(b => b.TextContent.Contains("Chat"));
        await cut.InvokeAsync(() => chatButton.Click());
        cut.WaitForState(() => cut.FindAll("aside.side-drawer-panel").Count > 0, TimeSpan.FromSeconds(3));

        // Give the renderer a moment to process any (erroneous) parameter-driven send.
        await Task.Delay(200);

        _handler.ChatBodies.Count.Should().Be(requestsAfterFirst,
            "re-opening the drawer without a new prompt must NOT re-fire the last prompt");
    }

    private static void SetInputText(CodedValuesChat chat, string text)
    {
        var field = typeof(CodedValuesChat).GetField("_inputText",
            BindingFlags.NonPublic | BindingFlags.Instance);
        field.Should().NotBeNull();
        field!.SetValue(chat, text);
    }

    /// <summary>
    /// Handler that serves /api/ai/config and /api/ai/chat (one SSE text chunk),
    /// and captures each /api/ai/chat request body so tests can assert on the
    /// conversation history sent to the AI.
    /// </summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<string> ChatBodies { get; } = [];

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
                if (request.Content is not null)
                {
                    var body = request.Content.ReadAsStringAsync(ct).Result ?? "";
                    if (!string.IsNullOrWhiteSpace(body))
                        ChatBodies.Add(body);
                }
                var sse = "event: TextChunk\r\n" +
                          """data: {"text":"Proposal table"}""" + "\r\n" +
                          "\r\n";
                response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(sse, Encoding.UTF8, "text/event-stream")
                };
            }
            else
            {
                response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json")
                };
            }
            return Task.FromResult(response);
        }
    }
}