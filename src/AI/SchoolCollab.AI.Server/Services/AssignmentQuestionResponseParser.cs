using System.Text.Json;
using SchoolCollab.AI.Abstractions;

namespace SchoolCollab.AI.Services;

/// <summary>
/// Parses the single JSON document the model returns for the assignment
/// question-generation endpoint (spec §4.3). Tolerant of common model
/// output quirks: a wrapping ```json code fence is stripped if present;
/// otherwise the substring from the first <c>{</c> to the last <c>}</c>
/// is extracted. Unknown extra JSON properties are tolerated (the model
/// frequently appends commentary fields the parser does not care about).
/// On any schema violation the parser throws
/// <see cref="AssignmentQuestionGenerationException"/> with a friendly
/// reason — the endpoint surfaces this as HTTP 502 so the wizard can
/// offer a retry (EC-1, EC-8).
/// </summary>
internal static class AssignmentQuestionResponseParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Parses the model's raw text into a validated
    /// <see cref="QuestionGenerationResponse"/>.
    /// </summary>
    public static QuestionGenerationResponse Parse(string modelText)
    {
        if (string.IsNullOrWhiteSpace(modelText))
            throw new AssignmentQuestionGenerationException(
                "The AI returned an empty response. Please try again.", 502);

        var trimmed = modelText.Trim();

        // Strip a wrapping ```json ... ``` fence if present.
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline > 0)
            {
                trimmed = trimmed[(firstNewline + 1)..];
            }

            var closingFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (closingFence >= 0)
            {
                trimmed = trimmed[..closingFence];
            }

            trimmed = trimmed.Trim();
        }

        // Fall back to extracting the first balanced JSON object when the
        // model emits prose around the document.
        var firstBrace = trimmed.IndexOf('{');
        var lastBrace = trimmed.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            trimmed = trimmed[firstBrace..(lastBrace + 1)];
        }

        QuestionGenerationResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<QuestionGenerationResponse>(trimmed, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new AssignmentQuestionGenerationException(
                $"The AI returned an invalid question set: malformed JSON ({ex.Message}).", 502);
        }

        if (parsed is null)
        {
            throw new AssignmentQuestionGenerationException(
                "The AI returned an invalid question set: the response body could not be deserialised.", 502);
        }

        Validate(parsed);
        return parsed;
    }

    private static void Validate(QuestionGenerationResponse response)
    {
        if (response.Questions is null || response.Questions.Count == 0)
            throw new AssignmentQuestionGenerationException(
                "The AI returned an invalid question set: the questions array is empty.", 502);

        for (var i = 0; i < response.Questions.Count; i++)
        {
            var q = response.Questions[i];
            ValidateQuestion(i, q);
        }
    }

    private static void ValidateQuestion(int index, GeneratedQuestionDto q)
    {
        var label = $"Question #{index + 1}";

        if (string.IsNullOrWhiteSpace(q.Text))
            throw new AssignmentQuestionGenerationException(
                $"The AI returned an invalid question set: {label} has a blank text.", 502);

        if (!Enum.IsDefined(typeof(GeneratedQuestionType), q.Type))
            throw new AssignmentQuestionGenerationException(
                $"The AI returned an invalid question set: {label} has an unknown question type.", 502);

        switch (q.Type)
        {
            case GeneratedQuestionType.MultipleChoice:
                ValidateMultipleChoice(label, q);
                break;
            case GeneratedQuestionType.TrueFalse:
                ValidateTrueFalse(label, q);
                break;
            case GeneratedQuestionType.ShortAnswer:
                ValidateShortAnswer(label, q);
                break;
        }
    }

    private static void ValidateMultipleChoice(string label, GeneratedQuestionDto q)
    {
        if (q.Options is null || q.Options.Count < 2 || q.Options.Count > 6)
            throw new AssignmentQuestionGenerationException(
                $"The AI returned an invalid question set: {label} (multipleChoice) must have between 2 and 6 options.", 502);

        var correctCount = 0;
        foreach (var opt in q.Options)
        {
            if (string.IsNullOrWhiteSpace(opt.Text))
                throw new AssignmentQuestionGenerationException(
                    $"The AI returned an invalid question set: {label} (multipleChoice) has a blank option.", 502);
            if (opt.IsCorrect) correctCount++;
        }

        if (correctCount != 1)
            throw new AssignmentQuestionGenerationException(
                $"The AI returned an invalid question set: {label} (multipleChoice) must have exactly one correct option.", 502);
    }

    private static void ValidateTrueFalse(string label, GeneratedQuestionDto q)
    {
        if (q.Options is null || q.Options.Count != 2)
            throw new AssignmentQuestionGenerationException(
                $"The AI returned an invalid question set: {label} (trueFalse) must have exactly two options.", 502);

        var labels = q.Options.Select(o => o.Text?.Trim()).ToArray();
        var labelSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "True", "False" };
        foreach (var l in labels)
        {
            if (l is null || !labelSet.Contains(l))
                throw new AssignmentQuestionGenerationException(
                    $"The AI returned an invalid question set: {label} (trueFalse) options must be labelled 'True' and 'False'.", 502);
        }

        var correctCount = q.Options.Count(o => o.IsCorrect);
        if (correctCount != 1)
            throw new AssignmentQuestionGenerationException(
                $"The AI returned an invalid question set: {label} (trueFalse) must have exactly one correct option.", 502);
    }

    private static void ValidateShortAnswer(string label, GeneratedQuestionDto q)
    {
        if (q.Options is { Count: > 0 })
            throw new AssignmentQuestionGenerationException(
                $"The AI returned an invalid question set: {label} (shortAnswer) must not include options.", 502);

        if (q.ModelAnswer is not null && string.IsNullOrWhiteSpace(q.ModelAnswer))
            throw new AssignmentQuestionGenerationException(
                $"The AI returned an invalid question set: {label} (shortAnswer) has a blank modelAnswer.", 502);
    }
}
