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
    /// Sends conversation history to the AI API and yields structured updates.
    /// </summary>
    public async IAsyncEnumerable<ChatUpdate> ChatAsync(
        IReadOnlyList<ChatMessage> history,
        string? model = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        logger.LogInformation("Sending chat with {Count} messages to AI API", history.Count);

        var request = new ChatRequest(
            history.Select(m => new ChatMessageRequest(
                m.Role == ChatRole.User ? "user" : "assistant",
                m.Text)).ToList(),
            model);

        var response = await http.PostAsJsonAsync("/api/ai/chat", request, ct);
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

            var line = await reader.ReadLineAsync(ct);
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

    private static ChatUpdate? ParseSseEvent(string? eventType, string data)
    {
        try
        {
            return eventType switch
            {
                "TextChunk" => JsonSerializer.Deserialize<TextChunkPayload>(data) is { } tc
                    ? new ChatUpdate.TextChunk(tc.Text ?? string.Empty)
                    : null,
                "ToolCallStart" => JsonSerializer.Deserialize<ToolCallStartPayload>(data) is { } tcs
                    ? new ChatUpdate.ToolCallStart(tcs.CallId ?? "", tcs.FriendlyName ?? "", tcs.ArgsSummary ?? "")
                    : null,
                "ToolCallEnd" => JsonSerializer.Deserialize<ToolCallEndPayload>(data) is { } tce
                    ? new ChatUpdate.ToolCallEnd(tce.CallId ?? "", tce.FriendlyName ?? "", tce.ResultSummary, tce.Success)
                    : null,
                "Error" => JsonSerializer.Deserialize<ErrorPayload>(data) is { } err
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
    private record TextChunkPayload(string? Text);
    private record ToolCallStartPayload(string? CallId, string? FriendlyName, string? ArgsSummary);
    private record ToolCallEndPayload(string? CallId, string? FriendlyName, string? ResultSummary, bool Success);
    private record ErrorPayload(string? Message);
}

// These are the request DTOs sent TO the AI API.
// They live here in the Admin project to avoid a circular dependency.
// The AI project defines its own matching records for deserialization.
public record ChatRequest(List<ChatMessageRequest> Messages, string? Model = null);
public record ChatMessageRequest(string Role, string? Text);