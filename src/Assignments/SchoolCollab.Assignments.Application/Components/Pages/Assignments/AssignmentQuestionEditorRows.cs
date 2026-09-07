using SchoolCollab.AI.Abstractions;
using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Application.Components.Pages.Assignments;

/// <summary>
/// One editable option row inside a <see cref="QuestionEditorRow"/>.
/// Plain data — correctness is tracked on the parent question via
/// <see cref="QuestionEditorRow.CorrectOptionIndex"/> (the single
/// source of truth, radio-friendly; null = no correct option picked).
/// </summary>
public sealed class OptionEditorRow
{
    /// <summary>The option's text. May be blank while the teacher is still typing.</summary>
    public string? OptionText { get; set; }
}

/// <summary>
/// One editable question row held in
/// <see cref="AssignmentEditFormModel.Questions"/>. Mirrors the spec §3.5
/// shape: question text, type discriminator, options, a single
/// correct-option index, optional model answer for ShortAnswer, and the
/// <see cref="DisplayOrder"/> that gets re-indexed 0..n over the whole
/// list after every mutation (EC-7).
/// </summary>
public sealed class QuestionEditorRow
{
    /// <summary>The question prompt text. May be blank while the teacher types.</summary>
    public string? QuestionText { get; set; }

    /// <summary>The wire-format question type (matches
    /// <see cref="QuestionTypeDto"/> in <c>SchoolCollab.Assignments.Contracts</c>).</summary>
    public QuestionTypeDto Type { get; set; }

    /// <summary>Stable, contiguous display order across the question list
    /// (EC-7). The form model re-indexes 0..n after every mutation.</summary>
    public int DisplayOrder { get; set; }

    /// <summary>The option rows. Always non-null; the contents depend on
    /// <see cref="Type"/> (MultipleChoice has user rows, TrueFalse has the
    /// canonical two, ShortAnswer has none).</summary>
    public List<OptionEditorRow> Options { get; } = [];

    /// <summary>Index into <see cref="Options"/> for the correct option.
    /// Null = no correct option picked (the submit gate rejects this for
    /// MC/TF per FR-252 / EC-5). -1 / out-of-range is also treated as
    /// "no correct option" by the gate.</summary>
    public int? CorrectOptionIndex { get; set; }

    /// <summary>Teacher reference for ShortAnswer questions (decision (c)):
    /// editable in the wizard, round-trips through
    /// <c>NewQuestionDto.ModelAnswer</c> at submit.</summary>
    public string? ModelAnswer { get; set; }

    /// <summary>Max options for a MultipleChoice question (spec §4.3 / EC-5 note).</summary>
    public const int MaxOptions = 6;

    /// <summary>Min options for a MultipleChoice question.</summary>
    public const int MinOptions = 2;

    /// <summary>Appends a blank option row, capped at <see cref="MaxOptions"/>
    /// for MultipleChoice rows (TrueFalse is fixed at two canonical rows,
    /// ShortAnswer has none).</summary>
    public void AddOption()
    {
        if (Type == QuestionTypeDto.MultipleChoice && Options.Count >= MaxOptions)
        {
            return;
        }
        Options.Add(new OptionEditorRow());
    }

    /// <summary>Removes the option at <paramref name="index"/> and clears
    /// <see cref="CorrectOptionIndex"/> when it pointed at or after the
    /// removed index (so correctness never silently re-points at a
    /// neighbouring option).</summary>
    public void RemoveOptionAt(int index)
    {
        if (index < 0 || index >= Options.Count)
        {
            return;
        }
        Options.RemoveAt(index);
        if (CorrectOptionIndex is int correct)
        {
            if (correct == index)
            {
                CorrectOptionIndex = null;
            }
            else if (correct > index)
            {
                CorrectOptionIndex = correct - 1;
            }
        }
    }

    /// <summary>Applies a type change to this row. Per spec §7 "type change
    /// resets options": the option list and correct pick are reset, then
    /// the canonical defaults are seeded for the new type (TrueFalse gets
    /// exactly the two "True" / "False" rows with no correct pick per
    /// EC-5; MultipleChoice gets two blank option rows so the teacher
    /// never lands on a 0-option MC row — mirrors
    /// <see cref="NewMultipleChoice"/>; ShortAnswer leaves the list
    /// empty). The submit gate still enforces one correct pick and
    /// non-empty option text, so seeding is a convenience only.</summary>
    public void ApplyTypeChange(QuestionTypeDto newType)
    {
        Type = newType;
        Options.Clear();
        CorrectOptionIndex = null;
        ModelAnswer = null;

        switch (newType)
        {
            case QuestionTypeDto.TrueFalse:
                Options.Add(new OptionEditorRow { OptionText = "True" });
                Options.Add(new OptionEditorRow { OptionText = "False" });
                break;
            case QuestionTypeDto.MultipleChoice:
                Options.Add(new OptionEditorRow());
                Options.Add(new OptionEditorRow());
                break;
            case QuestionTypeDto.ShortAnswer:
            default:
                // Empty; the teacher authors the model answer from scratch.
                break;
        }
    }

    /// <summary>Factory for a fresh hand-written MultipleChoice question with
    /// two blank option rows (the minimum to enter the editor).</summary>
    public static QuestionEditorRow NewMultipleChoice()
    {
        var row = new QuestionEditorRow
        {
            Type = QuestionTypeDto.MultipleChoice,
        };
        row.Options.Add(new OptionEditorRow());
        row.Options.Add(new OptionEditorRow());
        return row;
    }

    /// <summary>Maps a <see cref="GeneratedQuestionDto"/> from the AI host
    /// into an editable row (decision (b) — the only place the two
    /// int-mirrored enums are bridged).</summary>
    public static QuestionEditorRow FromGenerated(GeneratedQuestionDto dto)
    {
        var row = new QuestionEditorRow
        {
            QuestionText = dto.Text,
            Type = FromGeneratedType(dto.Type),
            ModelAnswer = dto.ModelAnswer,
        };
        if (dto.Options is not null)
        {
            for (var i = 0; i < dto.Options.Count; i++)
            {
                var opt = dto.Options[i];
                row.Options.Add(new OptionEditorRow { OptionText = opt.Text });
                if (opt.IsCorrect && row.CorrectOptionIndex is null)
                {
                    row.CorrectOptionIndex = i;
                }
            }
        }
        return row;
    }

    /// <summary>Maps the AI wire-format enum to the
    /// <see cref="QuestionTypeDto"/> used by the form model. Decision (b)
    /// — explicit three-case switch (no implicit int conversion) so a
    /// future enum value addition is caught at compile time.</summary>
    public static QuestionTypeDto FromGeneratedType(GeneratedQuestionType generated)
    {
        return generated switch
        {
            GeneratedQuestionType.MultipleChoice => QuestionTypeDto.MultipleChoice,
            GeneratedQuestionType.TrueFalse => QuestionTypeDto.TrueFalse,
            GeneratedQuestionType.ShortAnswer => QuestionTypeDto.ShortAnswer,
            _ => throw new ArgumentOutOfRangeException(nameof(generated), generated, "Unknown GeneratedQuestionType value."),
        };
    }

    /// <summary>Maps the form-model <see cref="QuestionTypeDto"/> to the
    /// AI wire-format enum. Inverse of <see cref="FromGeneratedType"/>.</summary>
    public static GeneratedQuestionType ToGeneratedType(QuestionTypeDto type)
    {
        return type switch
        {
            QuestionTypeDto.MultipleChoice => GeneratedQuestionType.MultipleChoice,
            QuestionTypeDto.TrueFalse => GeneratedQuestionType.TrueFalse,
            QuestionTypeDto.ShortAnswer => GeneratedQuestionType.ShortAnswer,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown QuestionTypeDto value."),
        };
    }
}

/// <summary>
/// One row in the <see cref="AssignmentEditFormModel.Attachments"/> list
/// (WS-A1 / FR-210–212, spec §3.5 model half). The wizard's Resources
/// UI section, the upload control, and the stage-at-selection path
/// (EC-4 reconciliation, decision (b)) are owned by ar-4 this round;
/// the row carries the staged-file metadata the API client surfaces.
/// </summary>
public sealed class AttachmentEditorRow
{
    /// <summary>The original file name (e.g. <c>"syllabus.pdf"</c>).</summary>
    public string? FileName { get; set; }

    /// <summary>The MIME content type (e.g. <c>"application/pdf"</c>).</summary>
    public string? ContentType { get; set; }

    /// <summary>The file size in bytes (signed 64-bit; allowlists + caps
    /// enforced at the API boundary per FR-210).</summary>
    public long FileSize { get; set; }

    /// <summary>The opaque storage path returned by the file-store
    /// staging endpoint. Populated by ar-4 at selection time (decision (b)).</summary>
    public string? StoragePath { get; set; }
}
