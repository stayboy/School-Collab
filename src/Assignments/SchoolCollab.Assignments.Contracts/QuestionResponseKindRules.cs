using System.ComponentModel;
using System.Reflection;

namespace SchoolCollab.Assignments.Contracts;

/// <summary>
/// Typed rejection for a question whose response kinds cannot be graded by the format its
/// assignment carries (QR/Q5, owner 2026-10-09). Thrown by
/// <see cref="QuestionResponseKindRules.EnsurePermitted"/> on the create/update surface before the
/// aggregate is built or mutated, and mapped to <c>400</c> by the assignment routes — the
/// typed-exception precedent D15 set.
/// </summary>
public sealed class QuestionResponseKindValidationException : Exception
{
    public QuestionResponseKindValidationException(string message) : base(message) { }
}

/// <summary>
/// Q5 (spec <c>question-response-types</c> §5.3, owner 2026-10-09): **a media response cannot be
/// auto-scored.** Every <see cref="QuestionResponseKindDto"/> is media — video, a recording, a
/// document or an image — so a question that defines any response kind is only valid on a
/// <see cref="GradingFormatDto.TeacherGraded"/> assignment. Auto Scored and Instant Feedback
/// questions keep to machine-scorable answer types (<c>MultipleChoice</c>, <c>TrueFalse</c>,
/// <c>ShortAnswer</c>).
///
/// <para>Lives in Contracts beside <see cref="AssignmentTypeGradingRules"/> for the same reason:
/// the authoring page (which does not reference Assignments.Core) disables the kinds the format
/// cannot grade, while the create/update command handlers reject the pair server-side.</para>
/// </summary>
public static class QuestionResponseKindRules
{
    /// <summary>Whether the grading format can grade a question that defines response kinds.
    /// True only for Teacher Marked — or when the question defines none.</summary>
    public static bool IsPermitted(
        GradingFormatDto gradingFormat, IReadOnlyList<QuestionResponseKindDto>? responseKinds) =>
        responseKinds is null || responseKinds.Count == 0
        || gradingFormat == GradingFormatDto.TeacherGraded;

    /// <summary>Q1(ii) (owner, 2026-10-09): whether a question on this format **must** define at
    /// least one response kind. True exactly where kinds are permitted — Teacher Marked — so the
    /// mandatory rule and the media rule can never contradict each other. On Auto Scored / Instant
    /// Feedback every kind is rejected by <see cref="IsPermitted"/>, so requiring one there would make
    /// the payload unsatisfiable <em>and</em> break AI generation, which those two formats are the
    /// only home of (<c>QuestionGenerationGate</c>, FR-220): the generator produces kind-less
    /// questions for the author to edit.</summary>
    public static bool RequiresResponseKinds(GradingFormatDto gradingFormat) =>
        gradingFormat == GradingFormatDto.TeacherGraded;

    /// <summary>The write-surface guard both command handlers call.</summary>
    public static void EnsurePermitted(
        GradingFormatDto gradingFormat, IReadOnlyList<QuestionResponseKindDto>? responseKinds)
    {
        if (!IsPermitted(gradingFormat, responseKinds))
        {
            throw new QuestionResponseKindValidationException(NotPermittedMessage(gradingFormat));
        }
    }

    /// <summary>The user-facing rejection message (400 body).</summary>
    public static string NotPermittedMessage(GradingFormatDto gradingFormat) =>
        "A question answered by video, audio, a document or an image needs Teacher Marked grading — "
        + $"{Describe(gradingFormat)} has nothing to score, and these answers can only be reviewed by you.";

    private static string Describe(GradingFormatDto format) =>
        typeof(GradingFormatDto).GetField(format.ToString())
            ?.GetCustomAttribute<DescriptionAttribute>()?.Description
        ?? format.ToString();

    /// <summary>The one-line rule the question editor states on the kind picker (no narrative).</summary>
    public const string TeacherMarkedOnlyHint =
        "Video, audio, document and image answers can only be reviewed by a teacher — switch the "
        + "assignment to Teacher Marked grading to use them.";
}
