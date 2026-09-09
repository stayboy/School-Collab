using SchoolCollab.Assignments.Core.Domain;

namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// Pure scoring implementation for <see cref="IScoringEngine"/> (WS-A3 /
/// spec §3.3). Every rule lives in the
/// <c>documents/rounds/round-ar-6-scoring.md</c> decision (d) —
/// distilled input records, no entity references, no DbContext, no DI
/// dependencies. The both-null trap (a null <c>SelectedOptionId</c>
/// answering an option-match question) is guarded at the per-question
/// evaluation step; the duplicate-answer rule deduplicates by first-wins
/// (the validator upstream rejects duplicates as a 400 first).
/// </summary>
public sealed class ScoringEngine : IScoringEngine
{
    public ScoringResult Score(ScoringInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        // Internal-misuse guard (the aggregate-guard pattern): the
        // submission handlers NEVER call the engine for TeacherGraded
        // — leaving that branch defensive only.
        if (input.GradingFormat == GradingFormat.TeacherGraded)
        {
            throw new InvalidOperationException(
                "TeacherGraded never-scored — the scoring engine is for AutoGraded/InstantGraded only.");
        }

        // Deduplicate answers by QuestionId (first wins, deterministic).
        var firstAnswerByQuestion = new Dictionary<Guid, ScoringAnswer>(input.Answers.Count);
        foreach (var answer in input.Answers)
        {
            if (!firstAnswerByQuestion.ContainsKey(answer.QuestionId))
            {
                firstAnswerByQuestion.Add(answer.QuestionId, answer);
            }
        }

        var results = new List<ScoringQuestionResult>(input.Questions.Count);
        var correctCount = 0;
        var autoScorableCount = 0;

        foreach (var question in input.Questions)
        {
            var isOptionMatch = question.CorrectOptionId.HasValue;
            var hasModelAnswer = !string.IsNullOrWhiteSpace(question.ModelAnswer);

            if (isOptionMatch)
            {
                // Option-match (MC/TF). Always auto-scorable because
                // a non-null CorrectOptionId is the discriminator; the
                // both-null trap is guarded below.
                autoScorableCount++;
                firstAnswerByQuestion.TryGetValue(question.Id, out var answer);
                var selectedOptionId = answer?.SelectedOptionId;
                var isCorrect = selectedOptionId.HasValue
                    && selectedOptionId.Value == question.CorrectOptionId!.Value;
                if (isCorrect)
                {
                    correctCount++;
                }
                results.Add(new ScoringQuestionResult(question.Id, isCorrect));
            }
            else if (hasModelAnswer)
            {
                // Text-match (ShortAnswer) with a model answer —
                // auto-scorable.
                autoScorableCount++;
                firstAnswerByQuestion.TryGetValue(question.Id, out var answer);
                var textAnswer = answer?.TextAnswer;
                var isCorrect = textAnswer is not null
                    && NormalizeForEquality(textAnswer).Equals(
                        NormalizeForEquality(question.ModelAnswer!),
                        StringComparison.OrdinalIgnoreCase);
                if (isCorrect)
                {
                    correctCount++;
                }
                results.Add(new ScoringQuestionResult(question.Id, isCorrect));
            }
            else
            {
                // Not auto-scorable — both a null-CorrectOptionId option-match
                // (recorded adjustment) and a ShortAnswer without a model
                // answer. Excluded from the auto-scorable count; per-question
                // IsCorrect is null regardless of any answer row.
                results.Add(new ScoringQuestionResult(question.Id, null));
            }
        }

        decimal score;
        if (autoScorableCount == 0)
        {
            // Division-by-zero guard (recorded adjustment): an
            // AutoGraded assignment whose questions are all model-answer-less
            // ShortAnswer scores 0 — there is nothing to score.
            score = 0m;
        }
        else if (input.MaxScore.HasValue)
        {
            // 2dp rounding pinned by test (66.666… → 66.67) and by spec §3.3
            // pass/fail semantics. MidpointRounding.AwayFromZero is the
            // documented convention.
            var raw = (decimal)correctCount * input.MaxScore.Value / autoScorableCount;
            score = Math.Round(raw, 2, MidpointRounding.AwayFromZero);
        }
        else
        {
            // Raw point count (1 point per correct auto-scorable question).
            score = correctCount;
        }

        bool? passed = null;
        if (input.PassScore.HasValue)
        {
            // The >= boundary is binding — score == threshold passes.
            passed = score >= input.PassScore.Value;
        }

        return new ScoringResult(results, score, passed);
    }

    /// <summary>Normalize a ShortAnswer text for equality comparison
    /// (spec §3.3 / decision (d) text rule): trim, case-insensitive
    /// (OrdinalIgnoreCase), collapse internal whitespace runs to single
    /// spaces. Used for the literal-exact-match scoring path.</summary>
    private static string NormalizeForEquality(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }
        var trimmed = value.Trim();
        var collapsed = new System.Text.StringBuilder(trimmed.Length);
        var previousWasSpace = false;
        foreach (var ch in trimmed)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!previousWasSpace)
                {
                    collapsed.Append(' ');
                }
                previousWasSpace = true;
            }
            else
            {
                collapsed.Append(ch);
                previousWasSpace = false;
            }
        }
        return collapsed.ToString();
    }
}
