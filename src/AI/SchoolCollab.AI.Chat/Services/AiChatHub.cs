using SchoolCollab.AI.Chat.Components;

namespace SchoolCollab.AI.Chat.Services;

/// <summary>
/// Scoped bridge that lets the inline <c>AiChat</c> on the landing page
/// mirror its conversation into the <see cref="AiChat"/> instance living
/// inside the side-drawer panel. Both surfaces share the same scoped
/// instance per render circuit, so the inline chat pushes each new
/// <see cref="AiChatMessage"/> via <see cref="AddMessage"/> and the drawer
/// chat reads via <see cref="Messages"/>.
///
/// Streaming progress (<see cref="StreamingState"/>) flows the same way — the
/// inline chat calls <see cref="SetStreamingState"/> on every chunk and the
/// drawer chat's display area picks up the change via <see cref="Changed"/>.
///
/// Scoped (not singleton) so back-button navigations naturally dispose of stale
/// state. The hub is intentionally tiny — conversation list, streaming state,
/// change event, add/clear/set-streaming operations — to keep the surface area
/// obvious. Domain-agnostic: it carries no CodedValues-specific knowledge.
/// </summary>
public class AiChatHub
{
    private readonly List<AiChatMessage> _messages = [];

    /// <summary>
    /// All messages that have been mirrored from the source chat into this hub,
    /// in order. The drawer chat reads this and re-renders when it changes.
    /// </summary>
    public IReadOnlyList<AiChatMessage> Messages => _messages;

    /// <summary>
    /// Live streaming snapshot from the source chat. The drawer's display chat
    /// binds to this so it shows the same "AI is typing…" indicator as the
    /// inline chat. Reset to <see cref="AiChatStreamingState"/> defaults when
    /// streaming ends or the conversation is cleared.
    /// </summary>
    public AiChatStreamingState StreamingState { get; private set; } =
        new(IsStreaming: false, StreamingText: string.Empty, ActiveToolCalls: null);

    /// <summary>
    /// Raised after <see cref="AddMessage"/>, <see cref="SetStreamingState"/>,
    /// or <see cref="Clear"/> mutates the hub. Subscribers (typically the
    /// drawer chat) re-render in response.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Appends a message and notifies subscribers. No-op if the message is null.
    /// </summary>
    public void AddMessage(AiChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _messages.Add(message);
        Changed?.Invoke();
    }

    /// <summary>
    /// Replaces the live streaming snapshot. Pass a snapshot with
    /// <c>IsStreaming = false</c> when streaming ends.
    /// </summary>
    public void SetStreamingState(AiChatStreamingState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        StreamingState = state;
        Changed?.Invoke();
    }

    /// <summary>
    /// Empties the hub and resets the streaming snapshot to its default, then
    /// notifies subscribers. No-op (no event) when the hub is already idle and
    /// empty.
    /// </summary>
    public void Clear()
    {
        if (_messages.Count == 0 && !StreamingState.IsStreaming) return;
        _messages.Clear();
        StreamingState = new AiChatStreamingState(false, string.Empty, null);
        Changed?.Invoke();
    }
}