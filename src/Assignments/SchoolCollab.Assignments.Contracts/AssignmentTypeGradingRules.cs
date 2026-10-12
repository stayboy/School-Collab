using System.ComponentModel;
using System.Reflection;

namespace SchoolCollab.Assignments.Contracts;

/// <summary>
/// Typed rejection for an assignment whose <c>type</c> does not permit its chosen
/// <c>grading format</c> (D15, owner 2026-10-09). Thrown by
/// <see cref="AssignmentTypeGradingRules.EnsurePermitted"/> on the create/update
/// command surface BEFORE the aggregate is built or mutated, and mapped to
/// <c>400</c> by <c>AssignmentRoutes</c> — the typed-exception precedent the
/// question/content validators set (never a bare <c>InvalidOperationException</c>
/// from a user-input path). Declared beside the rule so both consumers see it:
/// the handlers that throw it and the API that translates it.
/// </summary>
public sealed class AssignmentTypeGradingValidationException : Exception
{
    public AssignmentTypeGradingValidationException(string message) : base(message) { }
}

/// <summary>
/// D15 (owner, 2026-10-09): **the assignment type defines which grading formats are
/// possible.** Offline work is handwritten in school to build writing skills, so there
/// is nothing a machine can score — an Offline assignment is therefore ALWAYS Teacher
/// Marked. Online and Hybrid assignments publish their details online (Hybrid with some
/// written responses, uploaded or handed in), so all three formats stay available.
///
/// <para>Why this lives in Contracts: the rule is needed by BOTH consumers, and this is
/// the only project they share — the authoring page (Assignments.Application, which does
/// not reference Assignments.Core) filters its grading-format picker through
/// <see cref="PermittedFormats"/> and falls back via <see cref="FallbackFor"/> when a type
/// change invalidates the current pick, while the create/update command handlers
/// (Assignments.Core) reject an impossible pair through <see cref="EnsurePermitted"/> so no
/// API client can store one. The owner's full definitions of the three types live in
/// <c>documents/solution/assignment-type-grading-definitions.md</c>.</para>
/// </summary>
public static class AssignmentTypeGradingRules
{
    /// <summary>Owner's definition of the Online type (2026-10-09).</summary>
    public const string OnlineMeaning =
        "All assignment details are published online — students are not expected to write "
        + "their answers up.";

    /// <summary>Owner's definition of the Hybrid type (2026-10-09).</summary>
    public const string HybridMeaning =
        "Details are published online, but some answers are written to build writing skills; "
        + "written work is uploaded or submitted physically.";

    /// <summary>Owner's definition of the Offline type (2026-10-09).</summary>
    public const string OfflineMeaning =
        "Students write the questions and answers in school to build writing skills; the work "
        + "is uploaded or submitted physically.";

    /// <summary>The one-line rule the form states alongside the type's own meaning (D14f/D15).</summary>
    public const string TypeDefinesGradingHint =
        "The assignment type defines which grading formats are available";

    /// <summary>The reason shown when a type change forced the grading format to switch.</summary>
    public const string FormatSwitchedReason =
        "Offline assignments are always Teacher Marked — the grading format was switched for you.";

    /// <summary>The owner's sentence for one type — the durable definition, verbatim.</summary>
    public static string MeaningOf(AssignmentTypeDto type) => type switch
    {
        AssignmentTypeDto.Digital => OnlineMeaning,
        AssignmentTypeDto.SemiManual => HybridMeaning,
        AssignmentTypeDto.Manual => OfflineMeaning,
        _ => string.Empty,
    };

    /// <summary>
    /// Every grading format the type permits, in the picker's canonical order. Offline is
    /// Teacher Marked only; Online and Hybrid keep all three (a teacher may still mark a
    /// digital assignment by hand — the rule restricts only what the type makes impossible).
    /// </summary>
    public static GradingFormatDto[] PermittedFormats(AssignmentTypeDto type) =>
        type == AssignmentTypeDto.Manual
            ? [GradingFormatDto.TeacherGraded]
            : [GradingFormatDto.TeacherGraded, GradingFormatDto.AutoGraded, GradingFormatDto.InstantGraded];

    /// <summary>Whether the type permits the grading format.</summary>
    public static bool IsPermitted(AssignmentTypeDto type, GradingFormatDto gradingFormat) =>
        Array.IndexOf(PermittedFormats(type), gradingFormat) >= 0;

    /// <summary>The format the UI switches to when a type change invalidates the current pick.</summary>
    public static GradingFormatDto FallbackFor(AssignmentTypeDto type) => PermittedFormats(type)[0];

    /// <summary>
    /// Throws when the pair is impossible — the single write-surface guard the create and update
    /// handlers both call (each maps its domain enum onto these DTO twins by numeric value).
    /// </summary>
    public static void EnsurePermitted(AssignmentTypeDto type, GradingFormatDto gradingFormat)
    {
        if (!IsPermitted(type, gradingFormat))
        {
            throw new AssignmentTypeGradingValidationException(NotPermittedMessage(type, gradingFormat));
        }
    }

    /// <summary>The user-facing rejection message (400 body).</summary>
    public static string NotPermittedMessage(AssignmentTypeDto type, GradingFormatDto gradingFormat) =>
        $"{Describe(type)} assignments cannot be {Describe(gradingFormat)} — {MeaningOf(type)}";

    /// <summary>The enum member's <c>[Description]</c> — the user's vocabulary, as the pickers
    /// and D14f's hints already present it.</summary>
    private static string Describe<TEnum>(TEnum value) where TEnum : struct, Enum =>
        typeof(TEnum).GetField(value.ToString())?.GetCustomAttribute<DescriptionAttribute>()?.Description
        ?? value.ToString();
}
