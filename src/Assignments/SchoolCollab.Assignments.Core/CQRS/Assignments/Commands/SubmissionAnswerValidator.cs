using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands;

/// <summary>
/// WS-A3 (spec §3.3) — pure validator for inbound
/// <see cref="SubmissionAnswerDto"/> payloads. Throws
/// <see cref="SubmissionAnswerValidationException"/> with a single
/// clear message before any aggregate mutation so a partial state
/// can never be persisted. Shared by both submission handlers
/// (the <c>QuestionOptionDtoValidator</c> shared-validator precedent).
/// </summary>
internal static class SubmissionAnswerValidator
{
    /// <summary>Validates every inbound answer; throws on the first
    /// violation. Null/empty answers is a valid no-op (the legacy
    /// free-text flow). <paramref name="assignment"/> must carry its
    /// question + option rows (the handler loads them as part of the
    /// aggregate fetch).</summary>
    public static void Validate(Assignment assignment, IReadOnlyList<SubmissionAnswerDto>? answers)
    {
        if (answers is null || answers.Count == 0)
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(assignment);

        // Snapshot the question + option lookup once. The aggregate's
        // questions list is the single source of truth — the validator
        // never re-loads from the DbContext.
        var questionLookup = assignment.Questions.ToDictionary(q => q.Id);
        var seenQuestionIds = new HashSet<Guid>(answers.Count);

        for (var i = 0; i < answers.Count; i++)
        {
            var answer = answers[i];

            if (answer.QuestionId == Guid.Empty)
            {
                throw new SubmissionAnswerValidationException(
                    $"Answer at position {i}: QuestionId is required.");
            }

            if (!seenQuestionIds.Add(answer.QuestionId))
            {
                throw new SubmissionAnswerValidationException(
                    $"Answer at position {i}: duplicate question id {answer.QuestionId}.");
            }

            if (!questionLookup.TryGetValue(answer.QuestionId, out var question))
            {
                throw new SubmissionAnswerValidationException(
                    $"Answer at position {i}: question {answer.QuestionId} does not belong to this assignment.");
            }

            // Option-membership check: a non-null SelectedOptionId must
            // belong to that question's Options collection.
            if (answer.SelectedOptionId.HasValue)
            {
                var optionIds = question.Options.Select(o => o.Id).ToHashSet();
                if (!optionIds.Contains(answer.SelectedOptionId.Value))
                {
                    throw new SubmissionAnswerValidationException(
                        $"Answer at position {i}: selected option {answer.SelectedOptionId.Value} is not an option of question {answer.QuestionId}.");
                }
            }
        }
    }
}
