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
    /// <summary>A fresh MultipleChoice row (two blank options), matching the hand-add default.</summary>
    public static QuestionEditModel ForCreate(int position) =>
        new(position, true, QuestionEditorRow.NewMultipleChoice());

    /// <summary>An edit over a detached copy of <paramref name="row"/>.</summary>
    public static QuestionEditModel ForEdit(int position, QuestionEditorRow row) =>
        new(position, false, CopyRow(row));

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
        return copy;
    }
}

/// <summary>The dialog's confirmed result: the edited row, ready for the list to adopt.</summary>
public sealed record QuestionEditResult(QuestionEditorRow Row);
