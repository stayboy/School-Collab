using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Application.Helpers;

/// <summary>
/// Pure, UI- and test-friendly gate that decides whether AI question
/// generation is offered for a given <see cref="AssignmentTypeDto"/> +
/// <see cref="GradingFormatDto"/> combination (spec §0 decision 2 /
/// FR-220). Also owns the user-facing hint + tooltip strings the wizard
/// and the bUnit tests both bind to so wording cannot drift.
/// </summary>
public static class QuestionGenerationGate
{
    /// <summary>Step-1 hint shown when AI generation IS available
    /// (Digital/SemiManual + AutoGraded/InstantGraded).</summary>
    public const string EnabledHint =
        "AI question generation will be available in the Details step.";

    /// <summary>Step-1 hint shown when AI generation is NOT available
    /// (Manual any grading, or any type with TeacherGraded). The teacher
    /// may still hand-write questions in the editor.</summary>
    public const string DisabledHint =
        "AI question generation is not available for this combination — questions can still be added by hand once started.";

    /// <summary>Tooltip on the disabled Generate button (EC-3).</summary>
    public const string DisabledTooltip =
        "AI question generation is available for Online and Hybrid assignments with Auto Scored or Instant Feedback grading.";

    /// <summary>Returns <c>true</c> when AI question generation should be
    /// offered for the given type + grading format combination (FR-220).</summary>
    public static bool IsEnabled(AssignmentTypeDto? assignmentType, GradingFormatDto gradingFormat)
    {
        var typeOk = assignmentType is AssignmentTypeDto.Digital or AssignmentTypeDto.SemiManual;
        var gradingOk = gradingFormat is GradingFormatDto.AutoGraded or GradingFormatDto.InstantGraded;
        return typeOk && gradingOk;
    }

    /// <summary>Step-1 hint text that updates live with the user's
    /// selection (FR-220). Null assignments default to "not enabled".</summary>
    public static string HintText(AssignmentTypeDto? assignmentType, GradingFormatDto gradingFormat)
    {
        return IsEnabled(assignmentType, gradingFormat) ? EnabledHint : DisabledHint;
    }
}
