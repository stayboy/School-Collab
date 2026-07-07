using System.ClientModel;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SchoolCollab.AI.Abstractions;

namespace SchoolCollab.AI.Services;

/// <summary>
/// Generic, domain-agnostic AI chat engine. Replaces the CodedValues-specific
/// <c>CodedValueAIService</c>: it owns the multi-round streaming + tool-call
/// loop, while every domain concern (which tools exist, which to ship per
/// prompt, how to dispatch a call, how to format its SSE summary) is delegated
/// to pluggable <see cref="IToolProvider"/>s and a single
/// <see cref="ISystemPromptProvider"/>. The CodedValues behaviour is restored
/// byte-for-byte by registering <c>CodedValuesToolProvider</c> +
/// <c>CodedValuesSystemPromptProvider</c> (see <c>AddCodedValuesAiTools</c>).
///
/// The streaming loop, error handling, text cleaning, model resolution, and
/// SSE <c>ChatUpdate</c> shapes are carried over verbatim from the former
/// <c>CodedValueAIService.ChatAsync</c> so the <c>/api/ai/chat</c> payload is
/// unchanged (NFR-3).
/// </summary>
public sealed class AIChatEngine
{
    private readonly IToolProvider[] _toolProviders;
    private readonly ISystemPromptProvider _systemPromptProvider;
    private readonly IChatClientFactory _chatClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<AIChatEngine> _logger;

    // toolName → owning provider. First-registered provider wins on a name
    // collision (a warning is logged at construction — see EC-8).
    private readonly Dictionary<string, IToolProvider> _toolByName;

    public AIChatEngine(
        IEnumerable<IToolProvider> toolProviders,
        ISystemPromptProvider systemPromptProvider,
        IChatClientFactory chatClientFactory,
        IConfiguration config,
        ILogger<AIChatEngine> logger)
    {
        _systemPromptProvider = systemPromptProvider;
        _chatClientFactory = chatClientFactory;
        _config = config;
        _logger = logger;

        _toolProviders = toolProviders.ToArray();
        _toolByName = new Dictionary<string, IToolProvider>(StringComparer.Ordinal);
        foreach (var provider in _toolProviders)
        {
            foreach (var name in provider.ToolNames)
            {
                if (_toolByName.ContainsKey(name))
                {
                    logger.LogWarning("Duplicate tool name '{Name}' registered by multiple IToolProviders; the first-registered provider wins at dispatch time.", name);
                    continue;
                }
                _toolByName[name] = provider;
            }
        }
    }

    /// <summary>
    /// Sends conversation history to the AI and yields structured updates (text chunks,
    /// tool-call progress, errors). Handles multi-turn tool-call loops.
    /// Text from tool-call rounds is collected for message history but NOT streamed to UI —
    /// only the final round's text is yielded, preventing function-call JSON leakage.
    /// </summary>
    public async IAsyncEnumerable<ChatUpdate> ChatAsync(
        IReadOnlyList<ChatMessage> history,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var effectiveModel = ResolveDefaultModel();
        _logger.LogInformation("Processing AI chat with {Count} history messages, model {Model}", history.Count, effectiveModel);

        var chatClient = _chatClientFactory.GetClient();

        var systemPrompt = await _systemPromptProvider.GetSystemPromptAsync(ct);
        var messages = new List<ChatMessage> { new(ChatRole.System, systemPrompt) };
        messages.AddRange(history);

        // Each provider narrows its own tool bag per turn (e.g. the CodedValues
        // provider runs its SelectToolsForPrompt intent classifier). The engine
        // concatenates every provider's narrowed list — preserving the per-turn
        // tool subset the model saw before the refactor.
        var applicableTools = _toolProviders.SelectMany(p => p.CreateTools(history, _logger)).ToList();

        var options = new ChatOptions { Tools = applicableTools, ModelId = effectiveModel };

        var totalToolCalls = 0;
        const int maxToolCallRounds = 10;

        while (totalToolCalls < maxToolCallRounds)
        {
            var roundText = new StringBuilder();
            var toolCallsByCallId = new Dictionary<string, (string Name, string? Args)>();
            var seenCallIds = new HashSet<string>();
            // Collect ToolCallStart events to yield outside the try/catch (C# forbids yield in try with catch)
            var pendingStarts = new List<ChatUpdate.ToolCallStart>();
            bool streamCancelled = false;
            Exception? streamError = null;

            try
            {
                await foreach (var chunk in chatClient.GetStreamingResponseAsync(messages, options, ct).WithCancellation(ct))
                {
                    // Collect function call content from streaming updates
                    // Accumulate: later chunks for the same call ID may have more complete arguments
                    if (chunk.Contents is not null)
                    {
                        foreach (var content in chunk.Contents)
                        {
                            if (content is FunctionCallContent fc && fc.Name is not null)
                            {
                                var callId = fc.CallId ?? Guid.NewGuid().ToString();
                                var args = fc.Arguments is not null
                                    ? JsonSerializer.Serialize(fc.Arguments)
                                    : null;

                                if (toolCallsByCallId.TryGetValue(callId, out var existing))
                                {
                                    // Preserve existing args if this chunk has none (streaming may send name-only deltas)
                                    var mergedArgs = args ?? existing.Args;
                                    toolCallsByCallId[callId] = (fc.Name, mergedArgs);
                                }
                                else
                                {
                                    toolCallsByCallId[callId] = (fc.Name, args);
                                }

                                // Collect ToolCallStart to yield outside try/catch
                                if (seenCallIds.Add(callId))
                                {
                                    var friendlyName = GetFriendlyToolName(fc.Name);
                                    var argsSummary = FormatArgsSummary(fc.Name, args ?? toolCallsByCallId[callId].Args);
                                    pendingStarts.Add(new ChatUpdate.ToolCallStart(callId, friendlyName, argsSummary));
                                }
                            }
                        }
                    }

                    // Collect text for message history (always), but do NOT stream to UI during tool-call rounds
                    if (chunk.Text is not null)
                        roundText.Append(chunk.Text);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("AI chat streaming canceled by user or system.");
                streamCancelled = true;
            }
            catch (ClientResultException ex)
            {
                // The AI provider returned an HTTP error (e.g. 401 Unauthorized, 429 rate
                // limited, 5xx). Report it gracefully as a ChatUpdate.Error rather than letting
                // it propagate and crash the /api/ai/chat endpoint as an HTTP 500.
                _logger.LogError(ex, "AI provider returned an error (Status: {Status}).", ex.Status);
                streamError = ex;
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "I/O operation aborted during AI chat streaming. This usually indicates the connection was closed unexpectedly.");
                streamCancelled = true;
            }
            catch (SocketException ex)
            {
                _logger.LogWarning(ex, "Socket error during AI chat streaming (SocketErrorCode={SocketErrorCode}). Treating as stream completion.", ex.SocketErrorCode);
                streamCancelled = true;
            }
            catch (HttpRequestException) when (ct.IsCancellationRequested)
            {
                _logger.LogInformation("AI chat streaming cancelled (HttpRequestException during cancellation)");
                streamCancelled = true;
            }
            catch (HttpRequestException ex)
            {
                // Non-cancellation HTTP failure (DNS resolution, connection refused, etc.)
                _logger.LogError(ex, "HTTP failure during AI chat streaming.");
                streamError = ex;
            }
            catch (Exception ex)
            {
                // Last-resort: never let a provider/transport error crash the chat endpoint.
                _logger.LogError(ex, "Unexpected error during AI chat streaming.");
                streamError = ex;
            }

            // Yield any pending ToolCallStart events (must be outside try/catch for yield)
            foreach (var start in pendingStarts)
                yield return start;

            if (streamCancelled)
                yield break;

            // If the provider returned an error (auth failure, rate limit, 5xx, transport),
            // surface it as a structured ChatUpdate.Error instead of throwing an HTTP 500.
            if (streamError is not null)
            {
                yield return new ChatUpdate.Error(FormatProviderError(streamError));
                yield break;
            }

            // Build assistant message with text + function call content items.
            // ALWAYS clean the round text before adding to history — even intermediate
            // round text can contain leaked tool-call syntax that the model will echo
            // back in subsequent rounds if it sees it in the conversation history.
            // Use CleanForHistory (aggressive) for the message history to prevent echo-back.
            var historyText = CleanForHistory(roundText.ToString());
            var assistantContents = new List<AIContent>();
            if (historyText.Length > 0)
                assistantContents.Add(new TextContent(historyText));
            foreach (var (callId, (name, args)) in toolCallsByCallId)
            {
                var arguments = ParseArgumentsDictionary(args);
                assistantContents.Add(new FunctionCallContent(callId, name, arguments));
            }
            if (assistantContents.Count > 0)
                messages.Add(new ChatMessage(ChatRole.Assistant, assistantContents));

            if (toolCallsByCallId.Count == 0)
            {
                // Final round — no more tool calls. Stream the clean text to UI.
                // Use CleanForDisplay (gentle) for the UI to preserve the model's
                // human-readable prose while still stripping leaked syntax.
                var displayText = CleanForDisplay(roundText.ToString());
                if (displayText.Length > 0)
                    yield return new ChatUpdate.TextChunk(displayText);

                break;
            }

            totalToolCalls += toolCallsByCallId.Count;

            // Dispatch each tool call to its owning provider and add results
            foreach (var (callId, (name, args)) in toolCallsByCallId)
            {
                var result = await DispatchToolCallAsync(name, args, ct);
                messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(callId, result)]));

                var friendlyName = GetFriendlyToolName(name);
                var resultSummary = FormatResultSummary(name, result);
                var success = !result.StartsWith("Error", StringComparison.OrdinalIgnoreCase);
                yield return new ChatUpdate.ToolCallEnd(callId, friendlyName, resultSummary, success);
            }
        }

        if (totalToolCalls >= maxToolCallRounds)
        {
            _logger.LogWarning("Reached max tool-call rounds ({Max}), stopping", maxToolCallRounds);
            yield return new ChatUpdate.Error($"Reached maximum tool-call limit ({maxToolCallRounds}). Please continue your request.");
        }

        _logger.LogInformation("AI chat completed with {ToolCalls} tool calls", totalToolCalls);
    }

    private string ResolveDefaultModel()
    {
        // ChatModelResolver.Resolve returns the (provider, model) tuple resolved from
        // configuration. We only need the model here; ChatAsync always uses the model
        // that the active provider is configured with.
        var (_, model) = ChatModelResolver.Resolve(
            _config["codedvalue-ai-provider"],
            _config["Ollama:DefaultModel"],
            _config["OpenRouter:DefaultModel"]);
        return model;
    }

    private static IDictionary<string, object?> ParseArgumentsDictionary(string? args)
    {
        var arguments = new Dictionary<string, object?>();
        if (string.IsNullOrEmpty(args)) return arguments;
        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(args);
            if (parsed is not null)
            {
                foreach (var kvp in parsed)
                {
                    arguments[kvp.Key] = kvp.Value.ValueKind switch
                    {
                        JsonValueKind.String => kvp.Value.GetString(),
                        JsonValueKind.Number => kvp.Value.GetDouble(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.Null => null,
                        _ => kvp.Value.GetRawText()
                    };
                }
            }
        }
        catch { /* ignore parse errors, partial args ok */ }
        return arguments;
    }

    private async Task<string> DispatchToolCallAsync(string toolName, string? arguments, CancellationToken ct)
    {
        _logger.LogDebug("Dispatching tool call: {ToolName}", toolName);
        if (_toolByName.TryGetValue(toolName, out var provider))
        {
            try
            {
                return await provider.DispatchAsync(toolName, arguments, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tool call {ToolName} failed", toolName);
                return $"Error: {ex.Message}";
            }
        }

        // No provider owns this tool name — defensive (the model should only
        // call tools we advertised). Matches the former "Unknown tool" reply.
        return $"Unknown tool: {toolName}";
    }

    // --- Tool formatting (delegated to the owning provider) ---

    private string GetFriendlyToolName(string toolName) =>
        _toolByName.TryGetValue(toolName, out var provider)
            ? provider.GetFriendlyName(toolName)
            : toolName;

    private string FormatArgsSummary(string toolName, string? args) =>
        _toolByName.TryGetValue(toolName, out var provider)
            ? provider.FormatArgsSummary(toolName, args)
            : string.Empty;

    private string FormatResultSummary(string toolName, string result) =>
        _toolByName.TryGetValue(toolName, out var provider)
            ? provider.FormatResultSummary(toolName, result)
            : Truncate(result, 150);

    private static string Truncate(string result, int maxLength)
    {
        if (result.Length <= maxLength) return result;
        var firstLine = result.Split('\n')[0];
        return firstLine.Length <= maxLength
            ? firstLine + "…"
            : firstLine[..maxLength] + "…";
    }

    private static string CleanForHistory(string text) => AiTextCleaner.CleanForHistory(text);
    private static string CleanForDisplay(string text) => AiTextCleaner.CleanForDisplay(text);

    /// <summary>
    /// Formats a provider/transport exception into a concise, user-facing message,
    /// mapping common HTTP statuses (401/403/429/5xx) to actionable guidance.
    /// </summary>
    private static string FormatProviderError(Exception ex)
    {
        var status = ex switch
        {
            ClientResultException cre => (int?)cre.Status,
            HttpRequestException hre => (int?)hre.StatusCode,
            _ => null
        };

        return status switch
        {
            401 or 403 => "The AI provider rejected the request as unauthorised. Please check that a valid OpenRouter API key is configured (OpenRouter:ApiKey).",
            429 => "The AI provider rate-limited the request. Please wait a moment and try again.",
            >= 500 => $"The AI provider returned a server error (HTTP {status}). Please try again in a moment.",
            _ => $"The AI chat could not be completed: {ex.Message}"
        };
    }
}