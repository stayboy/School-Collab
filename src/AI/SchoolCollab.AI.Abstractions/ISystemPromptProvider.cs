namespace SchoolCollab.AI.Abstractions;

/// <summary>
/// Source of the system prompt + per-turn framing. The engine reads this
/// once per chat turn and prepends it to the message history.
/// </summary>
public interface ISystemPromptProvider
{
    Task<string> GetSystemPromptAsync(CancellationToken ct);

    /// <summary>True if the engine should include the current tool list in the
    /// framing message — useful for letting the model see the live tool bag.</summary>
    bool IncludesToolList { get; }
}