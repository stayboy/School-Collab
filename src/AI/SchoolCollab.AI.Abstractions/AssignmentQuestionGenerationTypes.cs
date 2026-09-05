using System.Text.Json.Serialization;

namespace SchoolCollab.AI.Abstractions;

/// <summary>
/// Discriminator for the wire-format question type used by the assignment
/// question-generation endpoint (spec §4.3). Values mirror
/// <c>QuestionTypeDto</c> in <c>SchoolCollab.Assignments.Contracts</c> so
/// the wizard can map between them at the UI boundary. AI.Abstractions
/// cannot reference Assignments.Contracts (cross-context), so the enum is
/// duplicated at the wire seam by int value.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<GeneratedQuestionType>))]
public enum GeneratedQuestionType
{
    /// <summary>2–6 options, exactly one <c>isCorrect</c>.</summary>
    MultipleChoice = 0,

    /// <summary>Exactly two canonical <c>True</c>/<c>False</c> options, exactly one <c>isCorrect</c>.</summary>
    TrueFalse = 1,

    /// <summary>No options; <c>modelAnswer</c> is optional teacher reference.</summary>
    ShortAnswer = 2,
}

/// <summary>
/// Inbound request to the assignment question-generation endpoint (spec §3.4).
/// Carries the topic/context strings needed to ground the generation; the
/// request is stateless and reads no tenant data, so it is safe to send
/// across the cross-module boundary without a tenant header.
/// </summary>
public sealed record QuestionGenerationRequest(
    Guid TopicId,
    string TopicName,
    Guid? GradeLevelId = null,
    IReadOnlyList<string>? ContextStrands = null,
    int QuestionCount = 5,
    IReadOnlyList<GeneratedQuestionType>? Types = null,
    string? PromptOverride = null);

/// <summary>
/// A single option inside a <see cref="GeneratedQuestionDto"/>. <see cref="IsCorrect"/>
/// is required for multipleChoice/trueFalse (exactly one per question) and is
/// always false (or irrelevant) for shortAnswer questions.
/// </summary>
public sealed record GeneratedQuestionOptionDto(
    string Text,
    bool IsCorrect = false);

/// <summary>
/// A single generated question. <see cref="Options"/> is non-null for
/// multipleChoice/trueFalse and null for shortAnswer. <see cref="ModelAnswer"/>
/// is only meaningful for shortAnswer.
/// </summary>
public sealed record GeneratedQuestionDto(
    string Text,
    GeneratedQuestionType Type,
    IReadOnlyList<GeneratedQuestionOptionDto>? Options = null,
    string? ModelAnswer = null);

/// <summary>
/// The validated response body returned by the question-generation endpoint
/// (spec §4.3). <see cref="Questions"/> is always non-empty.
/// </summary>
public sealed record QuestionGenerationResponse(
    IReadOnlyList<GeneratedQuestionDto> Questions);
