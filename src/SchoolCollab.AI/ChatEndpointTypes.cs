namespace SchoolCollab.AI;

/// <summary>Marker class to avoid Program type collision with Admin project.</summary>
public sealed class AiProgramMarker { }

/// <summary>
/// Request model for the chat endpoint.
/// </summary>
public record ChatRequest(List<ChatMessageRequest> Messages, string? Model = null);

/// <summary>
/// Individual message in a chat request.
/// </summary>
public record ChatMessageRequest(string Role, string? Text);