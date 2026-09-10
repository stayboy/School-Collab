using SchoolCollab.AI.Abstractions;

namespace SchoolCollab.Assignments.Application.Services;

/// <summary>
/// Server-side seam between the Assignments UI / commands and the AI
/// question-generation endpoint exposed by <c>SchoolCollab.AI.Server</c>
/// (spec §3.4). The Blazor wizard and the create command depend on this
/// abstraction only — they MUST NOT call the AI host directly.
/// </summary>
public interface IAssignmentQuestionGenerator
{
    /// <summary>
    /// Sends a generation request to the AI host and returns the validated
    /// questions. Throws <see cref="QuestionGenerationFailed"/> on any
    /// provider error, transport failure, or malformed response body so
    /// the wizard can render a friendly retryable message (EC-1 / EC-8).
    /// </summary>
    Task<IReadOnlyList<GeneratedQuestionDto>> GenerateAsync(
        QuestionGenerationRequest request,
        CancellationToken ct = default);
}
