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
}