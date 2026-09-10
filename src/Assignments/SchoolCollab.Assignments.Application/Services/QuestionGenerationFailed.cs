namespace SchoolCollab.Assignments.Application.Services;

/// <summary>
/// Typed exception thrown by <see cref="AssignmentQuestionGenerator"/> when
/// the AI host returns a non-success status, the network is unreachable, or
/// the success body cannot be deserialised. The spec-blessed name is
/// <c>QuestionGenerationFailed</c> (no <c>Exception</c> suffix) — the
/// repo's typed-exception rule is satisfied by <em>typedness</em>, not by
/// the suffix. Carries the friendly message the AI host returned (or a
/// status-derived fallback) so the wizard can render it directly.
/// </summary>
public sealed class QuestionGenerationFailed : Exception
{
    public QuestionGenerationFailed(string message) : base(message) { }
}
