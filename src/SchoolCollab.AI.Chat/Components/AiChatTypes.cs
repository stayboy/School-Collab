using Microsoft.Extensions.AI;

namespace SchoolCollab.AI.Chat.Components;

/// <summary>
/// Controls which parts of the AI chat UI are rendered. Used to embed the chat
/// in different layouts (inline on a page, split between display + input
/// areas inside a drawer, etc.).
/// </summary>
public enum AiChatMode
{
    /// <summary>Header + messages + input — the full standalone chat.</summary>
    Full,

    /// <summary>Header + messages only — read-only view of the conversation.</summary>
    DisplayOnly,

    /// <summary>Just the input area — no header, no message history.</summary>
    InputOnly,
}

/// <summary>
/// Compact display of a tool call. Public so it can be embedded in
/// <see cref="AiChatMessage"/> and raised via
/// <see cref="AiChat.OnToolCallCompleted"/>.
/// </summary>
public record ToolCallDisplay(string CallId, string FriendlyName, string ArgsSummary, string? ResultSummary = null, bool? Success = null);

/// <summary>
/// One turn in the conversation. Public so it can be the payload of the
/// public <see cref="AiChat.OnMessageAdded"/> event callback.
/// </summary>
public record AiChatMessage(ChatRole Role, string Text, List<ToolCallDisplay>? ToolCalls = null);

/// <summary>
/// Live snapshot of the AI streaming state. Bundles the is-streaming flag,
/// the partial text emitted so far, and the in-flight tool calls so they
/// can travel together through one event/parameter rather than three.
/// </summary>
public record AiChatStreamingState(
    bool IsStreaming,
    string StreamingText,
    IReadOnlyList<ToolCallDisplay>? ActiveToolCalls);