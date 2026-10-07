namespace SchoolCollab.Assignments.Application.Components.Pages.Assignments;

/// <summary>
/// Form state for <see cref="QuestionPromptDialog"/> (spec §6.1a PB-1…PB-11). A working copy of the
/// generation knobs plus the read-only context the composition will use.
/// <para><b>Defaults are the caller's job (PB-6).</b> Difficulty opens from the PERSISTED assignment
/// counts (PB-6(i)); the question count and type mix open from the narrow template parse when the
/// current prompt still matches PB-4's skeleton, else from the surface's own current values
/// (PB-6(ii)). The dialog itself never re-parses — it just renders what it is given.</para>
/// </summary>
public sealed class QuestionPromptModel
{
    /// <summary>The selected subject's display name (null = none selected).</summary>
    public required string? TopicName { get; init; }

    /// <summary>The targeted grades' canonical <c>GradeLevel.Level</c> values (PB-3).</summary>
    public required IReadOnlyList<int> GradeLevels { get; init; }

    /// <summary>Attached file/link display names, for the read-only Grounding row (PB-3).</summary>
    public required IReadOnlyList<string> ResourceNames { get; init; }

    /// <summary>True when the tenant locked the org prompt — knobs stay live, composition is skipped.</summary>
    public required bool PromptLocked { get; init; }

    // ── Editable knobs (the working copy) ────────────────────────────────────
    public int QuestionCount { get; set; } = 5;
    public int? DifficultyEasyCount { get; set; }
    public int? DifficultyMediumCount { get; set; }
    public int? DifficultyHardCount { get; set; }
    public bool IncludeMultipleChoice { get; set; } = true;
    public bool IncludeTrueFalse { get; set; } = true;
    public bool IncludeShortAnswer { get; set; } = true;
}

/// <summary>
/// The dialog's confirmed knobs (PB-1). It deliberately carries <b>no prompt text</b>: PB-5's
/// replace-vs-confirm happens in the CALLER right after the dialog closes, because the repo forbids
/// nested dialogs (<c>dialog-ui</c>) and a <c>ConfirmDialog</c> inside a shell dialog is exactly
/// that. Confirming therefore never costs the author their knob edits.
/// </summary>
public sealed record QuestionPromptResult(
    int QuestionCount,
    int? DifficultyEasyCount,
    int? DifficultyMediumCount,
    int? DifficultyHardCount,
    bool IncludeMultipleChoice,
    bool IncludeTrueFalse,
    bool IncludeShortAnswer);
