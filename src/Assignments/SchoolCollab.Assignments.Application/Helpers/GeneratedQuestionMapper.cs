namespace SchoolCollab.Assignments.Application.Helpers;

using SchoolCollab.AI.Abstractions;
using SchoolCollab.Assignments.Application.Components.Pages.Assignments;
using SchoolCollab.Assignments.Contracts;

/// <summary>
/// WS-B2 (spec §3.4 line 73) — maps a generated AI question to the wire
/// <see cref="NewQuestionDto"/> shape used to stage a questions draft. Reuses
/// <see cref="QuestionEditorRow.FromGenerated"/> — the same enum map
/// (<see cref="QuestionEditorRow.FromGeneratedType"/>) + option canonicalization
/// the wizard's append path uses (no new canonicalization is invented) — and the
/// same option projection <see cref="AssignmentEditFormModel"/> uses to reach the
/// wire contract. <c>DisplayOrder</c> is left at 0; the caller assigns list order
/// so the staged draft is re-indexed 0..n.
/// </summary>
public static class GeneratedQuestionMapper
{
    public static NewQuestionDto ToNewQuestionDto(GeneratedQuestionDto dto)
    {
        var row = QuestionEditorRow.FromGenerated(dto);

        IReadOnlyList<NewQuestionOptionDto>? options = null;
        if (row.Type is QuestionTypeDto.MultipleChoice or QuestionTypeDto.TrueFalse)
        {
            options = row.Options.Select((o, j) =>
                new NewQuestionOptionDto(o.OptionText ?? string.Empty, row.CorrectOptionIndex == j)).ToArray();
        }

        return new NewQuestionDto(
            QuestionText: row.QuestionText ?? string.Empty,
            QuestionType: row.Type,
            DisplayOrder: 0,
            Options: options,
            ModelAnswer: row.ModelAnswer);
    }
}
