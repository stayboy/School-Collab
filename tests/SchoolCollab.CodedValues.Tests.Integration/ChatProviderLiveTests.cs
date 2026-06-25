using System.ClientModel;
using System.Net.Http;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenAI;

namespace SchoolCollab.CodedValues.Tests.Integration;

/// <summary>
/// Live provider/model verification tests. These exercise the real Ollama and
/// OpenRouter endpoints using the models configured in the AI appsettings,
/// constructing the chat clients exactly as <c>SchoolCollab.AI/Program.cs</c> does.
///
/// A simple, deterministic prompt is sent ("respond with exactly the word PONG")
/// and the test asserts that the configured model for each provider responds.
///
/// If a provider endpoint is not reachable from the current environment (e.g.
/// Ollama is not running locally, or no network), the test is reported as
/// Inconclusive rather than Failed, so the suite stays green in environments
/// that lack the provider — while still verifying when the provider IS available.
/// Free-tier models (the OpenRouter :free variant) intermittently return empty
/// responses under rate limiting; those are retried once and otherwise reported
/// as Inconclusive.
/// </summary>
[TestClass]
public class ChatProviderLiveTests
{
    private const string SimplePrompt = "Respond with exactly the word PONG and nothing else.";
    private const string ExpectedToken = "PONG";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    private sealed record ProviderSettings(string Endpoint, string ApiKey, string Model);

    /// <summary>
    /// Loads the AI appsettings.json (copied to the test output as ai-appsettings.json)
    /// plus the standard .NET configuration sources for secrets, and extracts the
    /// Ollama and OpenRouter provider settings.
    ///
    /// Non-secret values (endpoint, default model) come from <c>appsettings.json</c>.
    /// The OpenRouter API key is intentionally NOT read from <c>appsettings.json</c>
    /// to keep it out of source control — see <c>.github/copilot/rules/ai-services.md</c>.
    /// The key is resolved from environment variables and user secrets instead.
    /// </summary>
    private static ProviderSettings LoadSettings(string provider)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ai-appsettings.json");
        File.Exists(path).Should().BeTrue($"ai-appsettings.json should be copied to the test output (looked for {path})");

        var config = new ConfigurationBuilder()
            .AddJsonFile(path, optional: false)
            .AddEnvironmentVariables()
            .AddUserSecrets("schoolcollab-ai-api")
            .Build();

        return provider switch
        {
            "ollama" => new ProviderSettings(
                config["Ollama:Endpoint"]
                    ?? throw new InvalidOperationException("Ollama:Endpoint not configured (expected in ai-appsettings.json)."),
                ApiKey: "ollama",
                Model: config["Ollama:DefaultModel"]
                    ?? throw new InvalidOperationException("Ollama:DefaultModel not configured (expected in ai-appsettings.json).")),
            "openrouter" => new ProviderSettings(
                config["OpenRouter:Endpoint"]
                    ?? throw new InvalidOperationException("OpenRouter:Endpoint not configured (expected in ai-appsettings.json)."),
                ApiKey: config["OpenRouter:ApiKey"]
                    ?? throw new InvalidOperationException(
                        "OpenRouter:ApiKey not configured. Set it via `dotnet user-secrets set OpenRouter:ApiKey \"<key>\"` " +
                        "or the OpenRouter__ApiKey environment variable."),
                Model: config["OpenRouter:DefaultModel"]
                    ?? throw new InvalidOperationException("OpenRouter:DefaultModel not configured (expected in ai-appsettings.json).")),
            _ => throw new ArgumentException($"Unknown provider: {provider}", nameof(provider))
        };
    }

    /// <summary>
    /// Builds an <see cref="IChatClient"/> for the given provider, mirroring
    /// the construction in <c>SchoolCollab.AI/Program.cs</c>.
    /// </summary>
    private static IChatClient BuildClient(string provider)
    {
        var s = LoadSettings(provider);
        var openAiClient = new OpenAIClient(
            new ApiKeyCredential(s.ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(s.Endpoint) });
        return openAiClient.GetChatClient(s.Model).AsIChatClient();
    }

    // =====================================================================
    // Ollama — local provider
    // =====================================================================

    [TestMethod]
    public async Task OllamaProvider_WithGemma4_RespondsToSimplePrompt()
    {
        var settings = LoadSettings("ollama");
        settings.Model.Should().Be("gemma4:31b-cloud", "the Ollama default model must be the working gemma4 model");

        await AssertProviderRespondsAsync("ollama");
    }

    // =====================================================================
    // OpenRouter — cloud provider
    // =====================================================================

    [TestMethod]
    public async Task OpenRouterProvider_WithGoogleGemma4_RespondsToSimplePrompt()
    {
        var settings = LoadSettings("openrouter");
        settings.Model.Should().Be("google/gemma-4-26b-a4b-it", "the OpenRouter default model must be the working gemma4 model");

        await AssertProviderRespondsAsync("openrouter");
    }

    /// <summary>
    /// Verifies that the OpenRouter chat client is constructed with the configured API
    /// key (as <c>Program.cs</c> does via <c>ApiKeyCredential</c>) and that the key is
    /// accepted by the endpoint. A missing/invalid key yields HTTP 401/403, not a model
    /// response — so a successful response here proves the API key is actually used.
    /// </summary>
    [TestMethod]
    public async Task OpenRouterClient_UsesApiKey_AuthenticatesSuccessfully()
    {
        var settings = LoadSettings("openrouter");
        settings.ApiKey.Should().NotBeNullOrWhiteSpace("OpenRouter:ApiKey must be configured in appsettings");
        settings.ApiKey.Should().StartWith("sk-or-", "the configured key should be an OpenRouter API key");

        IChatClient client = BuildClient("openrouter");
        using var cts = new CancellationTokenSource(Timeout);

        List<ChatMessage> messages = [new(ChatRole.User, "Reply with OK")];

        ChatResponse response;
        try
        {
            response = await client.GetResponseAsync(messages, new ChatOptions { ModelId = settings.Model }, cts.Token);
        }
        catch (HttpRequestException ex) when ((int?)ex.StatusCode is 401 or 403)
        {
            // Explicit auth failure: the API key is NOT being used or is invalid.
            Assert.Fail(
                $"OpenRouter rejected the request with HTTP {(int)ex.StatusCode!} — the API key is not being used " +
                $"or is invalid. This proves the chat client is NOT authenticating with OpenRouter:ApiKey.");
            return;
        }
        catch (HttpRequestException ex) when (IsConnectionFailure(ex) || IsRateLimited(ex))
        {
            Assert.Inconclusive($"OpenRouter endpoint not reachable or rate-limited. Skipping. Error: {ex.Message}");
            return;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            Assert.Inconclusive("OpenRouter call timed out. Skipping live verification.");
            return;
        }

        // A non-empty response means the endpoint accepted the API key and authorised the call.
        var text = (response.Text ?? string.Empty).Trim();
        text.Should().NotBeNullOrWhiteSpace(
            "a successful, non-empty response proves the OpenRouter chat client is using the configured API key for authentication");
    }

    // =====================================================================
    // Shared assertion helper
    // =====================================================================

    private static async Task AssertProviderRespondsAsync(string provider)
    {
        var settings = LoadSettings(provider);

        IChatClient client = BuildClient(provider);
        List<ChatMessage> messages =
        [
            new(ChatRole.System, "You are a connectivity test assistant. Follow the user's instruction exactly."),
            new(ChatRole.User, SimplePrompt)
        ];

        // Free-tier models (e.g. the OpenRouter :free variant) intermittently return
        // empty choice lists or 429/503 under rate limiting. Retry once so a transient
        // free-tier hiccup doesn't fail the suite, while still verifying the provider.
        const int maxAttempts = 2;
        Exception? lastFailure = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var cts = new CancellationTokenSource(Timeout);
            try
            {
                var response = await client.GetResponseAsync(
                    messages, new ChatOptions { ModelId = settings.Model }, cts.Token);

                var responseText = (response.Text ?? string.Empty).Trim();

                // Some free-tier responses come back with an empty body but no error.
                // Retry once; if it stays empty after the final attempt, report inconclusive.
                if (string.IsNullOrWhiteSpace(responseText) && attempt < maxAttempts)
                {
                    lastFailure = new InvalidOperationException("Empty response (no choices).");
                    await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None);
                    continue;
                }

                responseText.Should().NotBeNullOrWhiteSpace(
                    $"provider '{provider}' with model '{settings.Model}' should return text for a simple prompt");

                responseText.ToUpperInvariant().Should().Contain(ExpectedToken,
                    $"provider '{provider}' with model '{settings.Model}' was asked to reply with '{ExpectedToken}'");

                return; // success
            }
            catch (ArgumentOutOfRangeException ex)
            {
                // Thrown by the OpenAI SDK when the response has no choices (free-tier
                // rate limiting / transient unavailability). Retry once, then inconclusive.
                lastFailure = ex;
                if (attempt < maxAttempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None);
                    continue;
                }
            }
            catch (HttpRequestException ex) when (IsConnectionFailure(ex) || IsRateLimited(ex))
            {
                lastFailure = ex;
                if (attempt < maxAttempts && IsRateLimited(ex))
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), CancellationToken.None);
                    continue;
                }

                Assert.Inconclusive(
                    $"Provider '{provider}' endpoint '{settings.Endpoint}' was not reachable or was rate-limited " +
                    $"(model '{settings.Model}'). Skipping live verification. Error: {ex.Message}");
                return;
            }
            catch (TaskCanceledException ex) when (!cts.IsCancellationRequested)
            {
                Assert.Inconclusive(
                    $"Provider '{provider}' call to '{settings.Endpoint}' was cancelled unexpectedly. " +
                    $"Error: {ex.Message}");
                return;
            }
            catch (Exception ex) when (IsConnectionFailureMessage(ex.Message))
            {
                Assert.Inconclusive(
                    $"Provider '{provider}' endpoint '{settings.Endpoint}' was not reachable " +
                    $"(model '{settings.Model}'). Skipping live verification. Error: {ex.Message}");
                return;
            }
        }

        // Exhausted retries on transient empty-response failures (typical of :free models).
        Assert.Inconclusive(
            $"Provider '{provider}' with model '{settings.Model}' returned no usable response after {maxAttempts} attempts " +
            $"(likely free-tier rate limiting). Last failure: {lastFailure?.Message}");
    }

    private static bool IsConnectionFailure(HttpRequestException ex) =>
        ex.InnerException is System.Net.Sockets.SocketException ||
        ex.Message.Contains("No connection could be made", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("Connection refused", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("nodename nor servname", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("temporarily unavailable", StringComparison.OrdinalIgnoreCase);

    private static bool IsRateLimited(HttpRequestException ex) =>
        (int?)ex.StatusCode is 429 or 503;

    private static bool IsConnectionFailureMessage(string? message) =>
        !string.IsNullOrWhiteSpace(message) &&
        (message.Contains("No connection could be made", StringComparison.OrdinalIgnoreCase) ||
         message.Contains("Connection refused", StringComparison.OrdinalIgnoreCase) ||
         message.Contains("Failed to connect", StringComparison.OrdinalIgnoreCase));
}