namespace SchoolCollab.CodedValues.AI;

/// <summary>
/// Represents a streaming update from the AI chat service.
/// </summary>
public abstract record ChatUpdate
{
    /// <summary>A chunk of text to display to the user.</summary>
    public sealed record TextChunk(string Text) : ChatUpdate;

    /// <summary>Signals that a tool call has started.</summary>
    public sealed record ToolCallStart(string CallId, string FriendlyName, string ArgsSummary) : ChatUpdate;

    /// <summary>Signals that a tool call has completed.</summary>
    public sealed record ToolCallEnd(string CallId, string FriendlyName, string? ResultSummary, bool Success) : ChatUpdate;

    /// <summary>An error occurred during processing.</summary>
    public sealed record Error(string Message) : ChatUpdate;
}