using SchoolCollab.Assignments.Core.Domain;

namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// Pure scoring engine for AutoGraded / InstantGraded assignment submissions
/// (WS-A3 / spec §3.3). Distilled input records — no entity references —
/// keep the engine free of EF Core / DI dependencies (the
/// <c>NotificationRecipientFilter</c>-style pure-logic pattern). The
/// question kind is discriminated by <see cref="ScoringQuestion.CorrectOptionId"/>
/// presence on the distilled input rather than <c>QuestionType</c>: a
/// non-null <see cref="ScoringQuestion.CorrectOptionId"/> means an
/// option-match (MC/TF) question, a null means a text-match (ShortAnswer).
/// </summary>
public interface IScoringEngine
{
    /// <summary>Score one submission. Pure; no side effects. Throws
    /// <see cref="InvalidOperationException"/> when called with a
    /// <see cref="GradingFormat.TeacherGraded"/> input (the engine is
    /// never invoked for that grading format; the misuse guard
    /// mirrors the aggregate-guard pattern).</summary>
    ScoringResult Score(ScoringInput input);
}

/// <summary>The engine's distilled input — every entity reference
/// stripped. Constructed by the submission handlers from the loaded
/// <see cref="Assignment"/> + the inbound answer DTOs.</summary>
public sealed record ScoringInput(
    GradingFormat GradingFormat,
    /// <summary>The assignment's max score (decimal?). Null means the
    /// engine returns raw-point counts (one point per correct
    /// auto-scorable question).</summary>
    decimal? MaxScore,
    /// <summary>The assignment's pass/fail threshold (decimal?). Null
    /// means the engine leaves <see cref="ScoringResult.Passed"/>
    /// null (no pass/fail signal).</summary>
    decimal? PassScore,
    /// <summary>The auto-scorable question set. Ordering is preserved
    /// but the engine does not depend on it.</summary>
    IReadOnlyList<ScoringQuestion> Questions,
    /// <summary>The submitted answer set. Duplicate
    /// <see cref="ScoringAnswer.QuestionId"/>s are deduplicated by the
    /// engine (the first answer wins — defensive determinism;
    /// upstream validation rejects duplicates as a 400 first).</summary>
    IReadOnlyList<ScoringAnswer> Answers);

/// <summary>One distilled question. Option-match (MC/TF) when
/// <see cref="CorrectOptionId"/> is non-null; text-match (ShortAnswer)
/// when null. <see cref="ModelAnswer"/> is meaningful only for the
/// text-match path.</summary>
public sealed record ScoringQuestion(
    Guid Id,
    /// <summary>The correct option's id for option-match questions
    /// (MC/TF). Null means the question is text-match (ShortAnswer) OR
    /// a never-marked-correct option-match question — the engine
    /// excludes both from the auto-scorable count (recorded adjustment).</summary>
    Guid? CorrectOptionId,
    /// <summary>The reference answer for text-match questions. Null
    /// or whitespace means the question is not auto-scorable (excluded
    /// from the auto-scorable count; the per-question
    /// <see cref="ScoringQuestionResult.IsCorrect"/> is null
    /// regardless of any answer).</summary>
    string? ModelAnswer);

/// <summary>One distilled answer row. The engine expects
/// <see cref="QuestionId"/> to match a question in the input set;
/// orphan ids are silently ignored.</summary>
public sealed record ScoringAnswer(
    Guid QuestionId,
    /// <summary>The chosen option id for MC/TF answers. Null means
    /// the student left the option blank (the both-null trap guard —
    /// a null <see cref="SelectedOptionId"/> is never correct).</summary>
    Guid? SelectedOptionId,
    /// <summary>The free-text answer for ShortAnswer questions.</summary>
    string? TextAnswer);

/// <summary>The engine's distilled output — per-question correctness
/// plus the total score and the pass flag.</summary>
public sealed record ScoringResult(
    IReadOnlyList<ScoringQuestionResult> Questions,
    /// <summary>The auto-scored total. Decimal(5, 2) at the EF layer.
    /// Equals <c>correctCount * (MaxScore / autoScorableCount)</c>
    /// when <see cref="ScoringInput.MaxScore"/> is set, else the raw
    /// <c>correctCount</c>. Rounded to 2dp
    /// (<see cref="MidpointRounding.AwayFromZero"/>). Zero when no
    /// question is auto-scorable.</summary>
    decimal Score,
    /// <summary>True iff <see cref="ScoringInput.PassScore"/> is set
    /// and <see cref="Score"/> &gt;= threshold. Null when
    /// <see cref="ScoringInput.PassScore"/> is absent.</summary>
    bool? Passed);

/// <summary>The per-question outcome reported back to InstantGraded
/// clients via <c>SubmissionFeedbackDto</c>. Null means the question
/// was not auto-scorable (no model answer / no correct option);
/// true/false is the engine's evaluation.</summary>
public sealed record ScoringQuestionResult(
    Guid QuestionId,
    bool? IsCorrect);
