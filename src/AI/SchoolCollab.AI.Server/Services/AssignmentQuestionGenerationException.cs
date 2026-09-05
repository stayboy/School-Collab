namespace SchoolCollab.AI.Services;

/// <summary>
/// Typed exception thrown by the assignment question-generation surface
/// (endpoint + service + parser). Carries the HTTP status code the
/// endpoint should return to the caller:
/// <list type="bullet">
/// <item><c>400</c> — request validation failure (blank topic, count out of range, oversized override).</item>
/// <item><c>502</c> — upstream AI provider failure (auth, rate limit, 5xx, transport) OR malformed model output.</item>
/// </list>
/// Both cases map to a <c>{"error": "&lt;friendly message&gt;"}</c> JSON body so the
/// round-3 wizard has a single retry rule (EC-1 / EC-8).
/// </summary>
public sealed class AssignmentQuestionGenerationException : Exception
{
    public AssignmentQuestionGenerationException(string message, int statusCode)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
