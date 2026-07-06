using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace SchoolCollab.AI.Abstractions;

/// <summary>
/// A bag of tools the AI engine exposes to the model. One provider per
/// bounded context (e.g. CodedValues, Assignments, Students). The engine
/// aggregates all registered providers and dispatches tool calls by name.
/// </summary>
public interface IToolProvider
{
    /// <summary>Stable, namespaced tool names (e.g. "coded_values.create_bulk").</summary>
    IReadOnlyList<string> ToolNames { get; }

    /// <summary>
    /// Build the AITool list for the current turn. The provider may
    /// narrow the list per turn (e.g. the CodedValues provider applies its
    /// SelectToolsForPrompt intent classifier here). <paramref name="history"/>
    /// is the current chat message list so the provider can classify intent.
    /// </summary>
    IReadOnlyList<AITool> CreateTools(IReadOnlyList<ChatMessage> history, ILogger logger);

    /// <summary>Route a tool call to the right local implementation.</summary>
    Task<string> DispatchAsync(string toolName, string? args, CancellationToken ct);

    /// <summary>
    /// Human-friendly display name for a tool, emitted in the SSE
    /// <c>ToolCallStart</c>/<c>ToolCallEnd</c> events. Falls back to
    /// <paramref name="toolName"/> when no friendly name is mapped.
    /// </summary>
    string GetFriendlyName(string toolName);

    /// <summary>
    /// One-line summary of a tool call's arguments, emitted in the SSE
    /// <c>ToolCallStart</c> event (e.g. <c>"parent: CNTRY"</c>). Return
    /// <see cref="string.Empty"/> when no summary is useful.
    /// </summary>
    string FormatArgsSummary(string toolName, string? args);

    /// <summary>
    /// One-line summary of a tool call's result, emitted in the SSE
    /// <c>ToolCallEnd</c> event. <paramref name="result"/> is the raw string
    /// returned by <see cref="DispatchAsync"/>.
    /// </summary>
    string FormatResultSummary(string toolName, string result);
}