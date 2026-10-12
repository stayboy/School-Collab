using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Application.Components.Pages.Assignments;

/// <summary>
/// Form state for <see cref="QuestionEditDialog"/> (spec §6.2 QA-11, round
/// content-questions-modern-ui). It holds a WORKING COPY of the row being edited, so Cancel is a
/// true discard — the live list row is never touched until the dialog confirms.
/// </summary>
/// <param name="Position">The row's absolute index in the list (used for ids and the title).</param>
/// <param name="IsNew">True when the dialog is editing a row that does not exist yet.</param>
/// <param name="Row">The working copy the dialog binds to.</param>
public sealed record QuestionEditModel(int Position, bool IsNew, QuestionEditorRow Row)
{
    /// <summary>QR-2/Q1(ii): the assignment's grading format, so the dialog's response-kind picker
    /// renders its disabled-with-reason state where the media rule forbids kinds (Auto Scored /
    /// Instant Feedback). Defaults to Teacher Marked — the state in which every kind is selectable —
    /// so non-format-aware call sites and tests keep their meaning.</summary>
    public GradingFormatDto GradingFormat { get; init; } = GradingFormatDto.TeacherGraded;

    /// <summary>QR-5/§5.6 (U5): the host's instruction-media staging seam, threaded to the shared
    /// editor. Null means this context offers no uploads — the editor says so rather than rendering a
    /// dead file input (the same threading the grading format takes).</summary>
    public StageInstructionMediaAsync? StageMediaAsync { get; init; }

    /// <summary>A fresh MultipleChoice row (two blank options), matching the hand-add default.</summary>
    public static QuestionEditModel ForCreate(
        int position,
        GradingFormatDto gradingFormat = GradingFormatDto.TeacherGraded,
        StageInstructionMediaAsync? stageMediaAsync = null) =>
        new(position, true, QuestionEditorRow.NewMultipleChoice())
        {
            GradingFormat = gradingFormat,
            StageMediaAsync = stageMediaAsync,
        };

    /// <summary>An edit over a detached copy of <paramref name="row"/>.</summary>
    public static QuestionEditModel ForEdit(
        int position,
        QuestionEditorRow row,
        GradingFormatDto gradingFormat = GradingFormatDto.TeacherGraded,
        StageInstructionMediaAsync? stageMediaAsync = null) =>
        new(position, false, CopyRow(row))
        {
            GradingFormat = gradingFormat,
            StageMediaAsync = stageMediaAsync,
        };

    /// <summary>Deep copy — the dialog must never mutate the live row, or Cancel would already
    /// have applied half the edit.</summary>
    private static QuestionEditorRow CopyRow(QuestionEditorRow row)
    {
        var copy = new QuestionEditorRow
        {
            QuestionText = row.QuestionText,
            Type = row.Type,
            DisplayOrder = row.DisplayOrder,
            CorrectOptionIndex = row.CorrectOptionIndex,
            ModelAnswer = row.ModelAnswer,
            GenerationId = row.GenerationId,
        };
        foreach (var option in row.Options)
        {
            copy.Options.Add(new OptionEditorRow { OptionText = option.OptionText });
        }

        // D16/QR-2 + QR-5: the definition and the instruction blocks ARE part of the row — a copy
        // that dropped them would silently wipe them on the next confirm, because the dialog owns the
        // working copy and the confirmed row replaces the live one.
        copy.ResponseKinds.AddRange(row.ResponseKinds);
        foreach (var instruction in row.Instructions)
        {
            copy.Instructions.Add(new InstructionEditorRow
            {
                Kind = instruction.Kind,
                Text = instruction.Text,
                Url = instruction.Url,
                FileName = instruction.FileName,
                ContentType = instruction.ContentType,
                FileSize = instruction.FileSize,
                StoragePath = instruction.StoragePath,
            });
        }

        return copy;
    }
}

/// <summary>The dialog's confirmed result: the edited row, ready for the list to adopt.</summary>
public sealed record QuestionEditResult(QuestionEditorRow Row);
