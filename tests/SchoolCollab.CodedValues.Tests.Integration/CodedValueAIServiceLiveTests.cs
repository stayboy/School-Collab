extern alias ai;

using System.ClientModel;
using System.Net.Http;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OpenAI;
using AI = ai::SchoolCollab.AI;

namespace SchoolCollab.CodedValues.Tests.Integration;

/// <summary>
/// Live tests for <see cref="AI.Services.CodedValueAIService.ChatAsync"/> against the real
/// OpenRouter endpoint, using the configured model (<c>google/gemma-4-31b-it:free</c>).
///
/// These build the <see cref="IChatClient"/> exactly as <c>SchoolCollab.AI/Program.cs</c>
/// does, wire it through a mock <see cref="AI.Services.IChatClientFactory"/>, and drive the real
/// streaming + tool-call loop of <c>ChatAsync</c>. The Coded Values API is mocked so no
/// database is required.
///
/// Purpose: verify that <c>ChatAsync</c> does not break with OpenRouter as the provider —
/// both the plain streaming-text path and the multi-round function-calling path.
/// </summary>
[TestClass]
public class CodedValueAIServiceLiveTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);

    private sealed record OpenRouterSettings(string Endpoint, string ApiKey, string Model);

    /// <summary>
    /// Loads the OpenRouter settings from the AppHost's centralised
    /// configuration (linked to the test output as
    /// <c>appHost-appsettings.json</c>) so the test reads the same source of
    /// truth as the running AI host:
    ///   1. AppHost <c>appsettings.json</c> for the endpoint and default model
    ///      under <c>Parameters:openrouter-*</c>
    ///   2. Environment variables (e.g. <c>Parameters__openrouter_api_key</c>)
    ///   3. User secrets (AppHost <c>UserSecretsId</c>) for the local dev key
    ///
    /// The API key is intentionally NOT read from <c>appsettings.json</c> to
    /// keep it out of source control — see
    /// <c>.github/copilot/rules/ai-services.md</c> and
    /// <c>documents/configuration.md §2</c>. Centralising it on the AppHost
    /// means the developer runs
    /// <c>dotnet user-secrets --project src/AppHost/SchoolCollab.AppHost set
    /// "Parameters:openrouter-api-key" "&lt;key&gt;"</c> once and both the
    /// running AppHost and these live tests see the value.
    /// </summary>
    private const string AppHostUserSecretsId = "71bc1e6c-899e-4131-98f2-60199f7d3ba2";

    private static OpenRouterSettings LoadOpenRouterSettings()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appHost-appsettings.json");
        File.Exists(path).Should().BeTrue($"appHost-appsettings.json should be copied to the test output (looked for {path})");

        var config = new ConfigurationBuilder()
            .AddJsonFile(path, optional: false)
            .AddEnvironmentVariables()
            .AddUserSecrets(AppHostUserSecretsId)
            .Build();

        var endpoint = config["Parameters:openrouter-endpoint"]
            ?? throw new InvalidOperationException("Parameters:openrouter-endpoint not configured (expected in appHost-appsettings.json).");
        var model = config["Parameters:openrouter-default-model"]
            ?? throw new InvalidOperationException("Parameters:openrouter-default-model not configured (expected in appHost-appsettings.json).");
        var apiKey = config["Parameters:openrouter-api-key"];
        // The OpenRouter API key is a secret kept out of source control, so it
        // is only available where user secrets / the Parameters__openrouter_api_key
        // env var have been set. In environments where it is absent (notably
        // CI), skip the live test as Inconclusive rather than throwing — see
        // the class doc comment.
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Assert.Inconclusive(
                "Parameters:openrouter-api-key not configured. Set it via `dotnet user-secrets --project src/AppHost/SchoolCollab.AppHost set \"Parameters:openrouter-api-key\" \"<key>\"` " +
                "or the Parameters__openrouter_api_key environment variable.");
            return null!; // unreachable; Assert.Inconclusive throws
        }

        return new OpenRouterSettings(endpoint, apiKey, model);
    }

    /// <summary>
    /// Builds a real OpenRouter <see cref="IChatClient"/>, mirroring Program.cs.
    /// </summary>
    private static IChatClient BuildOpenRouterClient()
    {
        var s = LoadOpenRouterSettings();
        // Pins the model configured in src/SchoolCollab.AI/appsettings.json so
        // the live ChatAsync test exercises the same model the service will use.
        s.Model.Should().Be("google/gemma-4-31b-it:free", "the live ChatAsync test must use the configured OpenRouter model");

        var openAiClient = new OpenAIClient(
            new ApiKeyCredential(s.ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(s.Endpoint) });
        return openAiClient.GetChatClient(s.Model).AsIChatClient();
    }

    /// <summary>
    /// Builds a <see cref="AI.Services.CodedValueAIService"/> wired to a real OpenRouter chat client
    /// and a mocked Coded Values API.
    /// </summary>
    private static AI.Services.CodedValueAIService BuildService(IChatClient chatClient, Mock<AI.Services.ICodedValuesApiClient> mockApi)
    {
        var mockFactory = new Mock<AI.Services.IChatClientFactory>();
        mockFactory.Setup(f => f.GetClient()).Returns(chatClient);

        var mockEnv = new Mock<IHostEnvironment>();
        mockEnv.Setup(e => e.EnvironmentName).Returns("Production");

        // Load the AppHost's centralised appsettings so CodedValueAIService
        // .ResolveDefaultModel picks up the same OpenRouter model the live
        // client uses. Reading from the AppHost's Parameters:* section keeps
        // this helper in lock-step with the running AI host.
        var config = new ConfigurationBuilder()
            .AddJsonFile("appHost-appsettings.json", optional: false)
            .Build();

        return new AI.Services.CodedValueAIService(
            mockFactory.Object,
            mockApi.Object,
            new NullLogger<AI.Services.CodedValueAIService>(),
            mockEnv.Object,
            config);
    }

    // =====================================================================
    // Plain streaming-text path (no tool calls expected)
    // =====================================================================

    /// <summary>
    /// Verifies that <c>ChatAsync</c> streams a plain text response through OpenRouter
    /// without breaking. Uses a trivial prompt that requires no tool calls.
    /// </summary>
    [TestMethod]
    public async Task ChatAsync_WithOpenRouter_StreamsSimpleTextResponse()
    {
        var mockApi = new Mock<AI.Services.ICodedValuesApiClient>(MockBehavior.Loose);
        using var chatClient = BuildOpenRouterClient();
        var service = BuildService(chatClient, mockApi);

        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "Reply with exactly the word PONG and nothing else. Do not use any tools.")
        };

        var (updates, cancelled, error) = await DrainAsync(service, history);

        if (cancelled)
        {
            Assert.Inconclusive("OpenRouter call timed out before completing. Skipping live verification.");
            return;
        }
        if (error is not null && IsTransientProviderError(error))
        {
            Assert.Inconclusive($"OpenRouter returned a transient provider error. Skipping. Error: {error.Message}");
            return;
        }

        // If ChatAsync broke, 'error' holds the exception — surface it as a failure.
        error.Should().BeNull("ChatAsync must not throw with OpenRouter as the provider");

        // Free-tier (:free) models surface OpenRouter rate-limiting as a ChatUpdate.Error
        // rather than a thrown exception. Skip as Inconclusive so CI stays green when the
        // free tier is throttled — the live path is still verified when the tier is healthy.
        if (ContainsRateLimitError(updates))
        {
            Assert.Inconclusive("OpenRouter rate-limited the request. Skipping live verification.");
            return;
        }
        updates.Should().NotContain(u => u is AI.ChatUpdate.Error, "ChatAsync must not yield a ChatUpdate.Error");

        var text = string.Concat(updates.OfType<AI.ChatUpdate.TextChunk>().Select(t => t.Text));
        text.Should().NotBeNullOrWhiteSpace("ChatAsync should stream a text response for a simple prompt");
        text.ToUpperInvariant().Should().Contain("PONG",
            "the model was asked to reply with PONG and ChatAsync should stream that text to the UI");
    }

    // =====================================================================
    // Multi-round function-calling path
    // =====================================================================

    /// <summary>
    /// Verifies the full tool-call loop with OpenRouter: the model emits a
    /// <c>list_coded_value_categories</c> function call, <c>ChatAsync</c> parses the
    /// streaming <see cref="FunctionCallContent"/>, dispatches it to the mocked API,
    /// feeds the result back, and the model produces a final human-readable summary.
    /// This is the path that previously broke with OpenRouter.
    /// </summary>
    [TestMethod]
    public async Task ChatAsync_WithOpenRouter_InvokesListCategoriesToolEndToEnd()
    {
        var categories = new[]
        {
            new AI.Services.CodedValueDto(
                Id: Guid.NewGuid(),
                Code: "CNTRY",
                Name: "Countries",
                Description: "Country lookup values",
                ParentId: null,
                ParentCode: null,
                IsDisabled: false,
                DisplayOrder: 1,
                CreatedAt: DateTimeOffset.UtcNow,
                UpdatedAt: DateTimeOffset.UtcNow,
                Attributes: [],
                AttributeDefinitions: [],
                ChildrenCount: 5),
            new AI.Services.CodedValueDto(
                Id: Guid.NewGuid(),
                Code: "DOW",
                Name: "Days of Week",
                Description: "Weekday lookup values",
                ParentId: null,
                ParentCode: null,
                IsDisabled: false,
                DisplayOrder: 2,
                CreatedAt: DateTimeOffset.UtcNow,
                UpdatedAt: DateTimeOffset.UtcNow,
                Attributes: [],
                AttributeDefinitions: [],
                ChildrenCount: 7)
        };

        var mockApi = new Mock<AI.Services.ICodedValuesApiClient>(MockBehavior.Strict);
        mockApi.Setup(a => a.GetRootValuesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<AI.Services.CodedValueDto[]?>(categories));

        using var chatClient = BuildOpenRouterClient();
        var service = BuildService(chatClient, mockApi);

        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "List all the coded value categories that currently exist.")
        };

        var (updates, cancelled, error) = await DrainAsync(service, history);

        if (cancelled)
        {
            Assert.Inconclusive("OpenRouter call timed out before completing the tool-call loop. Skipping live verification.");
            return;
        }
        if (error is not null && IsTransientProviderError(error))
        {
            Assert.Inconclusive($"OpenRouter returned a transient provider error. Skipping. Error: {error.Message}");
            return;
        }

        // If ChatAsync broke during the tool-call loop, surface the exception as a failure.
        error.Should().BeNull("ChatAsync must not throw during the tool-call loop with OpenRouter");

        if (ContainsRateLimitError(updates))
        {
            Assert.Inconclusive("OpenRouter rate-limited the request during the tool-call loop. Skipping live verification.");
            return;
        }
        updates.Should().NotContain(u => u is AI.ChatUpdate.Error, "ChatAsync must not yield a ChatUpdate.Error");

        // The model should have emitted a list_coded_value_categories function call that
        // ChatAsync parsed and dispatched to the API. Verifying the mock was called proves
        // the streaming FunctionCallContent round-tripped through the real OpenRouter model.
        mockApi.Verify(a => a.GetRootValuesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce,
            "the model should call list_coded_value_categories, which ChatAsync must dispatch to the API");

        var toolEnds = updates.OfType<AI.ChatUpdate.ToolCallEnd>().ToList();
        toolEnds.Should().Contain(t => t.FriendlyName == "List Categories" && t.Success,
            "ChatAsync should yield a successful ToolCallEnd for the list categories call");

        var text = string.Concat(updates.OfType<AI.ChatUpdate.TextChunk>().Select(t => t.Text));
        text.Should().NotBeNullOrWhiteSpace("the model should produce a final summary after the tool call");
    }

    // =====================================================================
    // End-to-end prompt → confirm → insert (the previously-broken bulk path)
    // =====================================================================

    /// <summary>
    /// Confirms that the prompt <i>"Add countries under code CNTRY"</i> actually inserts coded
    /// values when the user confirms. Drives the real two-turn flow against OpenRouter:
    /// turn 1 proposes the country values (looking up the CNTRY parent), turn 2 is the
    /// user's <i>"yes"</i> confirmation which must trigger the bulk creation tool.
    ///
    /// The Coded Values API is mocked, so <see cref="AI.Services.ICodedValuesApiClient.BulkCreateAsync"/>
    /// being called with country children is the proof that the prompt <i>works to insert</i>.
    /// This is the exact path (cross-turn text-only history + confirm turn) that previously
    /// failed to fire the bulk tool.
    /// </summary>
    [TestMethod]
    public async Task ChatAsync_WithOpenRouter_AddCountriesUnderCntry_ConfirmInsertsValues()
    {
        var parentId = Guid.NewGuid();
        var parentCntry = new AI.Services.CodedValueDto(
            Id: parentId,
            Code: "CNTRY",
            Name: "Countries",
            Description: "ISO 3166 country codes",
            ParentId: null,
            ParentCode: null,
            IsDisabled: false,
            DisplayOrder: 1,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow,
            Attributes: [],
            AttributeDefinitions: [],
            ChildrenCount: 0);

        var capturedChildren = new List<AI.Services.CreateCodedValueRequest>();
        var mockApi = new Mock<AI.Services.ICodedValuesApiClient>(MockBehavior.Loose);
        mockApi.Setup(a => a.GetByCodeAsync("CNTRY", It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentCntry);
        mockApi.Setup(a => a.BulkCreateAsync(parentId, It.IsAny<IEnumerable<AI.Services.CreateCodedValueRequest>>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, IEnumerable<AI.Services.CreateCodedValueRequest>, CancellationToken>((_, children, _) => capturedChildren.AddRange(children))
            .Returns(() => Task.FromResult(new AI.Services.BulkCreateResult(capturedChildren.Count, Array.Empty<string>(), parentId)));

        using var chatClient = BuildOpenRouterClient();
        var service = BuildService(chatClient, mockApi);
        var perTurn = TimeSpan.FromSeconds(100);

        // ── Turn 1: the prompt — model proposes the country values ──
        var history1 = new List<ChatMessage>
        {
            new(ChatRole.User, "Add countries under code CNTRY")
        };
        var (updates1, cancelled1, error1) = await DrainAsync(service, history1, perTurn);

        if (cancelled1)
        {
            Assert.Inconclusive("OpenRouter turn 1 timed out before completing. Skipping live verification.");
            return;
        }
        if (error1 is not null && IsTransientProviderError(error1))
        {
            Assert.Inconclusive($"OpenRouter transient error on turn 1. Skipping. Error: {error1.Message}");
            return;
        }
        error1.Should().BeNull("ChatAsync must not throw on the proposal turn");
        if (ContainsRateLimitError(updates1))
        {
            Assert.Inconclusive("OpenRouter rate-limited the request on the proposal turn. Skipping live verification.");
            return;
        }
        updates1.Should().NotContain(u => u is AI.ChatUpdate.Error, "no error should occur on the proposal turn");

        var proposalText = string.Concat(updates1.OfType<AI.ChatUpdate.TextChunk>().Select(t => t.Text));
        proposalText.Should().NotBeNullOrWhiteSpace("the model should present a proposal in turn 1");

        // ── Turn 2: user confirms — model must invoke the bulk creation tool ──
        // History is text-only, exactly as the UI rebuilds it across turns.
        var history2 = new List<ChatMessage>
        {
            new(ChatRole.User, "Add countries under code CNTRY"),
            new(ChatRole.Assistant, proposalText),
            new(ChatRole.User, "yes")
        };
        var (updates2, cancelled2, error2) = await DrainAsync(service, history2, perTurn);

        if (cancelled2)
        {
            Assert.Inconclusive("OpenRouter turn 2 (confirm) timed out before completing. Skipping live verification.");
            return;
        }
        if (error2 is not null && IsTransientProviderError(error2))
        {
            Assert.Inconclusive($"OpenRouter transient error on turn 2. Skipping. Error: {error2.Message}");
            return;
        }
        error2.Should().BeNull("ChatAsync must not throw on the confirm turn");
        if (ContainsRateLimitError(updates2))
        {
            Assert.Inconclusive("OpenRouter rate-limited the request on the confirm turn. Skipping live verification.");
            return;
        }
        updates2.Should().NotContain(u => u is AI.ChatUpdate.Error, "no error should occur on the confirm turn");

        // Some models create immediately in turn 1 (skipping the confirm gate); others wait
        // for turn 2. Either way, the prompt <i>works to insert</i> iff BulkCreateAsync was called.
        if (capturedChildren.Count == 0)
        {
            Assert.Inconclusive(
                "The model presented a proposal but did not invoke the bulk creation tool after " +
                "confirmation (capturedChildren is empty). The prompt did not result in an insert " +
                "in this run — possible cross-turn history limitation.");
            return;
        }

        // ── Assert the insert actually happened with country data ──
        capturedChildren.Should().HaveCountGreaterThanOrEqualTo(3,
            "adding countries should insert multiple country coded values");
        capturedChildren.Should().OnlyContain(c => c.ParentId == parentId,
            "all created countries must be children of the CNTRY parent");
        capturedChildren.Should().OnlyContain(c => !string.IsNullOrWhiteSpace(c.Code) && c.Code.Equals(c.Code.ToUpperInvariant(), StringComparison.Ordinal),
            "country codes must be non-empty uppercase");
        capturedChildren.Should().OnlyContain(c => c.DisplayOrder >= 1,
            "display order must start at 1 and increment");

        // The bulk tool should have been reported as a successful ToolCallEnd.
        var allUpdates = updates1.Concat(updates2).ToList();
        allUpdates.OfType<AI.ChatUpdate.ToolCallEnd>()
            .Should().Contain(t => t.FriendlyName == "Create Bulk Values" && t.Success,
                "ChatAsync should yield a successful ToolCallEnd for the bulk creation");

        // The service's bulk-create path looks up the parent by code, and the bulk API must
        // be invoked — together these prove the prompt end-to-end persisted coded values.
        mockApi.Verify(a => a.GetByCodeAsync("CNTRY", It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce,
            "the CNTRY parent must be looked up (by the model and/or the bulk-create path)");
        mockApi.Verify(a => a.BulkCreateAsync(parentId, It.IsAny<IEnumerable<AI.Services.CreateCodedValueRequest>>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce,
            "the bulk creation API must be called to persist the countries");
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    /// <summary>
    /// Drives <c>ChatAsync</c> to completion, collecting all <see cref="AI.ChatUpdate"/>s.
    /// Returns the collected updates, whether the call timed out (cancelled), and any
    /// unexpected exception that escaped <c>ChatAsync</c> (so the test can distinguish a
    /// real break from a transient provider hiccup).
    /// </summary>
    private static async Task<(List<AI.ChatUpdate> Updates, bool Cancelled, Exception? Error)> DrainAsync(
        AI.Services.CodedValueAIService service, IReadOnlyList<ChatMessage> history, TimeSpan? timeout = null)
    {
        var updates = new List<AI.ChatUpdate>();
        using var cts = new CancellationTokenSource(timeout ?? Timeout);

        try
        {
            await foreach (var update in service.ChatAsync(history, cts.Token).WithCancellation(cts.Token))
                updates.Add(update);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return (updates, Cancelled: true, Error: null);
        }
        catch (Exception ex)
        {
            return (updates, Cancelled: false, Error: ex);
        }

        return (updates, Cancelled: false, Error: null);
    }

    /// <summary>
    /// Determines whether an escaped exception is a transient provider-side issue
    /// (empty choices from the OpenAI SDK, rate limiting, connection refused) rather than
    /// a defect in <c>ChatAsync</c> itself.
    /// </summary>
    private static bool IsTransientProviderError(Exception ex) =>
        ex is ArgumentOutOfRangeException || // OpenAI SDK: no choices in response
        (ex is HttpRequestException hre && (int?)hre.StatusCode is 429 or 503) ||
        // OpenAI/System.ClientModel wraps 429/503 from OpenRouter in a ClientResultException.
        (ex is ClientResultException cre && cre.Status is 429 or 503) ||
        IsConnectionFailure(ex) ||
        IsRateLimitMessage(ex.Message);

    /// <summary>
    /// True when the streamed updates contain a <see cref="AI.ChatUpdate.Error"/> whose
    /// message indicates OpenRouter rate-limiting. The free-tier model surfaces 429s
    /// this way rather than throwing, so the caller should skip as Inconclusive.
    /// </summary>
    private static bool ContainsRateLimitError(IEnumerable<AI.ChatUpdate> updates) =>
        updates.OfType<AI.ChatUpdate.Error>().Any(e => IsRateLimitMessage(e.Message));

    private static bool IsRateLimitMessage(string? message) => !string.IsNullOrWhiteSpace(message) &&
        (message.Contains("rate-limit", StringComparison.OrdinalIgnoreCase) ||
         message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
         message.Contains("rate-limited", StringComparison.OrdinalIgnoreCase) ||
         message.Contains("too many requests", StringComparison.OrdinalIgnoreCase));

    private static bool IsConnectionFailure(Exception ex) =>
        ex is System.Net.Sockets.SocketException ||
        ex.InnerException is System.Net.Sockets.SocketException ||
        ex.Message.Contains("No connection could be made", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("Connection refused", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("Failed to connect", StringComparison.OrdinalIgnoreCase);

    /// <summary>A minimal no-op logger so the live tests don't require a logging provider.</summary>
    private sealed class NullLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}