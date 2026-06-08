using System.IO;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using SchoolCollab.CodedValues.AI;

namespace SchoolCollab.CodedValues.Admin.Services;

/// <summary>
/// HTTP client that calls the AI API's SSE streaming endpoint and yields <see cref="ChatUpdate"/> objects.
/// This replaces the in-process CodedValueAIService after extraction.
/// </summary>
public sealed class AiChatClient(HttpClient http, ILogger<AiChatClient> logger)
{
    /// <summary>
    /// JSON options matching the server's camelCase SSE payloads.
    /// <see cref="System.Text.Json.JsonSerializerDefaults.Web"/> enables
    /// <see cref="JsonSerializerOptions.PropertyNameCaseInsensitive"/> so
    /// that <c>{"text":"..."}</c> matches <c>TextChunkPayload.Text</c>.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Sends conversation history to the AI API and yields structured updates.
    /// </summary>
    public async IAsyncEnumerable<ChatUpdate> ChatAsync(
        IReadOnlyList<ChatMessage> history,
        string? model = null,
        string? provider = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        logger.LogInformation("Sending chat with {Count} messages to AI API (provider: {Provider})", history.Count, provider ?? "(default)");

        var request = new ChatRequest(
            history.Select(m => new ChatMessageRequest(
                m.Role == ChatRole.User ? "user" : "assistant",
                m.Text)).ToList(),
            model,
            provider);

        HttpResponseMessage response;
        try
        {
            // Use ResponseHeadersRead for SSE streaming — don't buffer the entire response body.
            // Without this, HttpClient waits for the full response before returning,
            // which defeats the purpose of streaming and causes IOException to surface
            // at the POST call instead of during incremental stream reads.
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/ai/chat")
            {
                Content = JsonContent.Create(request)
            };
            response = await http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (IOException) when (ct.IsCancellationRequested)
        {
            logger.LogInformation("Chat request cancelled during POST (IOException)");
            yield break;
        }
        catch (HttpRequestException) when (ct.IsCancellationRequested)
        {
            logger.LogInformation("Chat request cancelled during POST (HttpRequestException)");
            yield break;
        }

        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentType?.MediaType != "text/event-stream")
        {
            // If the API returns JSON instead of SSE (e.g. error), read the body
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("Expected text/event-stream but got {ContentType}", response.Content.Headers.ContentType?.MediaType);
            yield return new ChatUpdate.Error($"Unexpected response type: {response.Content.Headers.ContentType?.MediaType}");
            yield break;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        string? currentEvent = null;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            string? line;
            try
            {
                line = await reader.ReadLineAsync(ct);
            }
            catch (IOException ex)
            {
                // StreamReader throws IOException when the connection is lost mid-stream,
                // including when CancellationToken fires (common when user navigates away).
                // Treat as graceful stream completion rather than surfacing an error.
                if (ct.IsCancellationRequested)
                    logger.LogInformation("Chat stream aborted due to cancellation (IOException)");
                else
                    logger.LogWarning(ex, "Chat stream aborted due to I/O error");
                break;
            }

            if (line is null) break;

            if (string.IsNullOrEmpty(line))
            {
                // Blank line = end of SSE event
                currentEvent = null;
                continue;
            }

            if (line.StartsWith("event: ", StringComparison.OrdinalIgnoreCase))
            {
                currentEvent = line["event: ".Length..].Trim();
                continue;
            }

            if (line.StartsWith("data: ", StringComparison.OrdinalIgnoreCase))
            {
                var data = line["data: ".Length..];

                var update = ParseSseEvent(currentEvent, data);
                if (update is not null)
                    yield return update;
            }
        }

        logger.LogInformation("Chat stream completed");
    }

    internal static ChatUpdate? ParseSseEvent(string? eventType, string data)
    {
        try
        {
            return eventType switch
            {
                "TextChunk" => JsonSerializer.Deserialize<TextChunkPayload>(data, JsonOptions) is { } tc
                    ? new ChatUpdate.TextChunk(tc.Text ?? string.Empty)
                    : null,
                "ToolCallStart" => JsonSerializer.Deserialize<ToolCallStartPayload>(data, JsonOptions) is { } tcs
                    ? new ChatUpdate.ToolCallStart(tcs.CallId ?? "", tcs.FriendlyName ?? "", tcs.ArgsSummary ?? "")
                    : null,
                "ToolCallEnd" => JsonSerializer.Deserialize<ToolCallEndPayload>(data, JsonOptions) is { } tce
                    ? new ChatUpdate.ToolCallEnd(tce.CallId ?? "", tce.FriendlyName ?? "", tce.ResultSummary, tce.Success)
                    : null,
                "Error" => JsonSerializer.Deserialize<ErrorPayload>(data, JsonOptions) is { } err
                    ? new ChatUpdate.Error(err.Message ?? "Unknown error")
                    : null,
                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // SSE payload DTOs for deserialization
    internal record TextChunkPayload(string? Text);
    internal record ToolCallStartPayload(string? CallId, string? FriendlyName, string? ArgsSummary);
    internal record ToolCallEndPayload(string? CallId, string? FriendlyName, string? ResultSummary, bool Success);
    internal record ErrorPayload(string? Message);
}

// These are the request DTOs sent TO the AI API.
// They live here in the Admin project to avoid a circular dependency.
// The AI project defines its own matching records for deserialization.
public record ChatRequest(List<ChatMessageRequest> Messages, string? Model = null, string? Provider = null);
public record ChatMessageRequest(string Role, string? Text);