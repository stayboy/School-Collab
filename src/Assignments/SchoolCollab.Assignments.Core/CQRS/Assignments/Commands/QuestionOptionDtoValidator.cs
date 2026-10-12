using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands;

/// <summary>
/// FR-252 / spec §3.3 / decision 10 — pure validation for inbound
/// <see cref="NewQuestionDto"/> payloads. Throws
/// <see cref="AssignmentQuestionValidationException"/> with a single clear
/// message before any child is added to the aggregate so a partial state can
/// never be persisted. Shared by Create + Update handlers.
/// </summary>
internal static class QuestionOptionDtoValidator
{
    /// <summary>Validate every inbound question; throw on the first violation.</summary>
    /// <param name="gradingFormat">The assignment's own format. It decides whether a question MUST
    /// define a response kind — mandatory exactly where kinds can be carried (Q1(ii), owner
    /// 2026-10-09; see <see cref="QuestionResponseKindRules.RequiresResponseKinds"/>).</param>
    public static void ValidateQuestions(
        IReadOnlyList<NewQuestionDto> questions, GradingFormatDto gradingFormat)
    {
        for (var i = 0; i < questions.Count; i++)
        {
            ValidateQuestion(questions[i], listIndex: i, gradingFormat);
        }
    }

    private static void ValidateQuestion(
        NewQuestionDto q, int listIndex, GradingFormatDto gradingFormat)
    {
        if (string.IsNullOrWhiteSpace(q.QuestionText))
        {
            throw new AssignmentQuestionValidationException(
                $"Question at position {listIndex}: QuestionText is required.");
        }

        // D16/QR-2 (§5.1), as amended by Q1(ii) (owner, 2026-10-09): a question states what it
        // expects — at least one kind — WHERE THE FORMAT CAN CARRY KINDS. Every kind is media and the
        // media rule permits them on Teacher Marked alone, so on Auto Scored / Instant Feedback the
        // definition is not required: the expected answer form is already fixed by QuestionType, and
        // forcing a kind there would be unsatisfiable — the media rule rejects every one of them —
        // while breaking AI generation, which those formats are the only home of.
        var responseKinds = q.ResponseKinds ?? [];
        if (responseKinds.Count == 0
            && QuestionResponseKindRules.RequiresResponseKinds(gradingFormat))
        {
            throw new AssignmentQuestionValidationException(
                $"Question at position {listIndex}: at least one response kind is required "
                + "(video, audio, document or image).");
        }

        foreach (var kind in responseKinds)
        {
            if (!Enum.IsDefined(kind))
            {
                throw new AssignmentQuestionValidationException(
                    $"Question at position {listIndex}: unsupported response kind '{(int)kind}'.");
            }
        }

        switch (q.QuestionType)
        {
            case QuestionTypeDto.MultipleChoice:
                ValidateMultipleChoice(q, listIndex);
                break;
            case QuestionTypeDto.TrueFalse:
                ValidateTrueFalse(q, listIndex);
                break;
            case QuestionTypeDto.ShortAnswer:
                // ShortAnswer: no options required, modelAnswer optional (spec §5).
                break;
            default:
                throw new AssignmentQuestionValidationException(
                    $"Question at position {listIndex}: unsupported question type '{q.QuestionType}'.");
        }
    }

    private static void ValidateMultipleChoice(NewQuestionDto q, int listIndex)
    {
        var options = q.Options ?? [];
        if (options.Count < 2)
        {
            throw new AssignmentQuestionValidationException(
                $"Question at position {listIndex}: MultipleChoice requires at least 2 options.");
        }
        var correctCount = options.Count(o => o.IsCorrect);
        if (correctCount != 1)
        {
            throw new AssignmentQuestionValidationException(
                $"Question at position {listIndex}: MultipleChoice requires exactly 1 correct option " +
                $"(found {correctCount}).");
        }
    }

    private static void ValidateTrueFalse(NewQuestionDto q, int listIndex)
    {
        var options = q.Options ?? [];
        // Canonical TrueFalse shape: exactly two options labelled "True" and "False"
        // (decision 10 / spec §5). The handler does NOT synthesize options — payloads
        // must carry them.
        if (options.Count != 2)
        {
            throw new AssignmentQuestionValidationException(
                $"Question at position {listIndex}: TrueFalse requires exactly 2 options (found {options.Count}).");
        }
        var hasTrue = options.Any(o => string.Equals(o.OptionText, "True", StringComparison.OrdinalIgnoreCase));
        var hasFalse = options.Any(o => string.Equals(o.OptionText, "False", StringComparison.OrdinalIgnoreCase));
        if (!hasTrue || !hasFalse)
        {
            throw new AssignmentQuestionValidationException(
                $"Question at position {listIndex}: TrueFalse options must be exactly 'True' and 'False'.");
        }
        var correctCount = options.Count(o => o.IsCorrect);
        if (correctCount != 1)
        {
            throw new AssignmentQuestionValidationException(
                $"Question at position {listIndex}: TrueFalse requires exactly 1 correct option " +
                $"(found {correctCount}).");
        }
    }
}
