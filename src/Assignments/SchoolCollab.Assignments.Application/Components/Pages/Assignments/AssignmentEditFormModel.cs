using SchoolCollab.AI.Abstractions;
using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Application.Components.Pages.Assignments;

/// <summary>
/// Form model for the assignment edit / create flow. Carries the
/// page-level fields (Title, Description, DueDate, MaxScore) used by
/// the wizard AND the question/attachment editor rows required by AI
/// spec §3.5 / §10 (phases 7–8). The DTO → form-model projection
/// (<see cref="From"/> / <see cref="LoadFrom"/>) and the create-request
/// projection (<see cref="ToCreateRequest"/>) live on the model itself
/// so they are discoverable and unit-testable — see
/// documents/solution/dto-form-model-mapping.md.
/// </summary>
public sealed class AssignmentEditFormModel
{
    [System.ComponentModel.DataAnnotations.Required]
    public string? Title { get; set; }

    public string? Description { get; set; }

    public DateTime? DueDate { get; set; }

    public decimal? MaxScore { get; set; }

    /// <summary>The editable question rows owned by this form model
    /// (spec §3.5). Always non-null; the list starts empty.</summary>
    public List<QuestionEditorRow> Questions { get; } = [];

    /// <summary>The attachment metadata rows (spec §3.5 model half).
    /// The Step-2 Resources UI section, the upload control, and the
    /// stage-at-submit (EC-4) are owned by ar-4-modules-resources
    /// (decision (a)).</summary>
    public List<AttachmentEditorRow> Attachments { get; } = [];

    /// <summary>Optional free-text prompt override (FR-230 / decision 8).
    /// When blank, the AI host loads the embedded system prompt. When
    /// set, the override is sent as a user-role framing message.</summary>
    public string? AiPromptOverride { get; set; }

    /// <summary>WS-A2 (spec §7 Q6): archive grace window in days.
    /// Persisted on the row so the archive sweep honours per-row
    /// overrides; the default of 30 mirrors the spec's retention floor.
    /// Pass-through only — no visible wizard/edit field this round
    /// (decision (j) recorded adjustment).</summary>
    public int ArchiveGraceDays { get; set; }

    /// <summary>Fixed question page size for the editor + review paginator
    /// (spec §0 decision 9 / FR-240).</summary>
    public const int QuestionPageSize = 5;

    /// <summary>
    /// Projects an <see cref="AssignmentSummaryDto"/> into a brand-new, fully-
    /// populated <see cref="AssignmentEditFormModel"/>. The question/attachment
    /// collections start empty — this round only persists them on create
    /// (FR-250/251); the edit page is a later round (round doc §Out).
    /// </summary>
    public static AssignmentEditFormModel From(AssignmentSummaryDto assignment)
    {
        var model = new AssignmentEditFormModel();
        model.LoadFrom(assignment);
        return model;
    }

    /// <summary>
    /// Loads this model's editable fields from an
    /// <see cref="AssignmentSummaryDto"/> in place. <see cref="DueDate"/>
    /// converts <see cref="DateTimeOffset"/> to <see cref="DateTime"/>.
    /// </summary>
    public void LoadFrom(AssignmentSummaryDto assignment)
    {
        Title = assignment.Title;
        Description = assignment.Description;
        DueDate = assignment.DueDate?.DateTime;
        MaxScore = assignment.MaxScore;
        ArchiveGraceDays = assignment.ArchiveGraceDays;
    }

    /// <summary>
    /// Projects this form model into a <see cref="CreateAssignmentRequest"/>
    /// that the API client submits. Page-level values that live outside the
    /// model (type, grading, audience, subject, grade level, mandatory
    /// review) are passed in as arguments — the student-model precedent
    /// (documents/solution/dto-form-model-mapping.md). Re-indexes
    /// <c>DisplayOrder</c> to 0..n over the whole question list (EC-7).
    /// Returns <c>Questions</c>/<c>Attachments</c> as <c>null</c> when the
    /// collections are empty (the wire contract's default).
    /// </summary>
    public CreateAssignmentRequest ToCreateRequest(
        AssignmentTypeDto assignmentType,
        GradingFormatDto gradingFormat,
        TargetAudienceTypeDto targetAudienceType,
        Guid topicId,
        Guid? gradeLevelId,
        bool mandatoryReview)
    {
        IReadOnlyList<NewQuestionDto>? questions = null;
        if (Questions.Count > 0)
        {
            var list = new List<NewQuestionDto>(Questions.Count);
            for (var i = 0; i < Questions.Count; i++)
            {
                var row = Questions[i];
                IReadOnlyList<NewQuestionOptionDto>? options = null;
                if (row.Type is QuestionTypeDto.MultipleChoice or QuestionTypeDto.TrueFalse)
                {
                    var optionList = new List<NewQuestionOptionDto>(row.Options.Count);
                    for (var j = 0; j < row.Options.Count; j++)
                    {
                        var isCorrect = row.CorrectOptionIndex == j;
                        optionList.Add(new NewQuestionOptionDto(row.Options[j].OptionText ?? string.Empty, isCorrect));
                    }
                    options = optionList;
                }
                list.Add(new NewQuestionDto(
                    QuestionText: row.QuestionText ?? string.Empty,
                    QuestionType: row.Type,
                    DisplayOrder: i,
                    Options: options,
                    ModelAnswer: row.ModelAnswer));
            }
            questions = list;
        }

        IReadOnlyList<NewAttachmentDto>? attachments = null;
        if (Attachments.Count > 0)
        {
            attachments = Attachments
                .Select(a => new NewAttachmentDto(
                    a.FileName ?? string.Empty,
                    a.ContentType ?? string.Empty,
                    a.FileSize,
                    a.StoragePath ?? string.Empty))
                .ToList();
        }

        return new CreateAssignmentRequest(
            Title: Title ?? string.Empty,
            Description: Description,
            AssignmentType: assignmentType,
            GradingFormat: gradingFormat,
            TargetAudienceType: targetAudienceType,
            TopicId: topicId,
            GradeLevelId: gradeLevelId,
            DueDate: DueDate.HasValue ? new DateTimeOffset(DueDate.Value, TimeSpan.Zero) : null,
            MaxScore: MaxScore,
            MandatoryReview: mandatoryReview,
            AiPromptOverride: AiPromptOverride,
            Questions: questions,
            Attachments: attachments);
    }

    /// <summary>
    /// Client-side submit gate mirroring the server-side
    /// <c>QuestionOptionDtoValidator</c> rules (FR-252) plus the
    /// auto-graded/instant-feedback minimum (zero questions → fail for
    /// those grading formats). Returns the first violation found (or
    /// <c>null</c> when the form passes).
    /// </summary>
    public bool QuestionsPassSubmitGate(GradingFormatDto gradingFormat, out string? error)
    {
        if (Questions.Count == 0)
        {
            if (gradingFormat is GradingFormatDto.AutoGraded or GradingFormatDto.InstantGraded)
            {
                error = "Auto-scored and instant-feedback assignments need at least one question.";
                return false;
            }
            error = null;
            return true;
        }

        for (var i = 0; i < Questions.Count; i++)
        {
            var row = Questions[i];
            var n = i + 1; // 1-based for user-facing messages

            if (string.IsNullOrWhiteSpace(row.QuestionText))
            {
                error = $"Question {n} needs text.";
                return false;
            }

            switch (row.Type)
            {
                case QuestionTypeDto.MultipleChoice:
                {
                    if (row.Options.Count < 2)
                    {
                        error = $"Question {n} needs at least 2 options.";
                        return false;
                    }
                    for (var j = 0; j < row.Options.Count; j++)
                    {
                        if (string.IsNullOrWhiteSpace(row.Options[j].OptionText))
                        {
                            error = $"Question {n} has an empty option.";
                            return false;
                        }
                    }
                    if (row.CorrectOptionIndex is not int correct || correct < 0 || correct >= row.Options.Count)
                    {
                        error = $"Question {n} needs one correct option.";
                        return false;
                    }
                    break;
                }
                case QuestionTypeDto.TrueFalse:
                {
                    if (!HasCanonicalTrueFalseOptions(row))
                    {
                        error = $"Question {n} must have exactly True and False options.";
                        return false;
                    }
                    if (row.CorrectOptionIndex is not int correct || correct < 0 || correct >= row.Options.Count)
                    {
                        error = $"Question {n} needs one correct option.";
                        return false;
                    }
                    break;
                }
                case QuestionTypeDto.ShortAnswer:
                default:
                    // No option rules for ShortAnswer.
                    break;
            }
        }

        error = null;
        return true;
    }

    /// <summary>True iff the row carries exactly the canonical
    /// <c>"True"</c>/<c>"False"</c> options in any order (case-insensitive).
    /// Used by <see cref="QuestionsPassSubmitGate"/>.</summary>
    private static bool HasCanonicalTrueFalseOptions(QuestionEditorRow row)
    {
        if (row.Options.Count != 2) return false;
        var a = row.Options[0].OptionText?.Trim();
        var b = row.Options[1].OptionText?.Trim();
        var aIsTrue = string.Equals(a, "True", StringComparison.OrdinalIgnoreCase);
        var aIsFalse = string.Equals(a, "False", StringComparison.OrdinalIgnoreCase);
        var bIsTrue = string.Equals(b, "True", StringComparison.OrdinalIgnoreCase);
        var bIsFalse = string.Equals(b, "False", StringComparison.OrdinalIgnoreCase);
        return (aIsTrue && bIsFalse) || (aIsFalse && bIsTrue);
    }

    /// <summary>Returns the question page slice (FR-240). Clamps
    /// <paramref name="pageIndex"/> to the valid range so a stale
    /// page-index from before a removal/append cannot escape the
    /// data set.</summary>
    public IReadOnlyList<QuestionEditorRow> GetQuestionPage(int pageIndex, int pageSize = QuestionPageSize)
    {
        if (Questions.Count == 0)
        {
            return [];
        }
        var pageCount = Math.Max(1, (Questions.Count + pageSize - 1) / pageSize);
        var clamped = pageIndex < 0 ? 0 : Math.Min(pageIndex, pageCount - 1);
        return Questions
            .Skip(clamped * pageSize)
            .Take(pageSize)
            .ToList();
    }

    /// <summary>Total page count for the current question list using
    /// <see cref="QuestionPageSize"/>. Always at least 1 so callers
    /// that pin <c>CurrentPageIndex = 0</c> never go negative.</summary>
    public int QuestionPageCount(int pageSize = QuestionPageSize)
    {
        return Math.Max(1, (Questions.Count + pageSize - 1) / pageSize);
    }

    /// <summary>Appends a hand-written question (decision (d): never
    /// replaces; the teacher prunes rows explicitly). Re-indexes
    /// <c>DisplayOrder</c> 0..n over the whole list (EC-7).</summary>
    public void AddQuestion(QuestionEditorRow row)
    {
        Questions.Add(row);
        ReindexQuestions();
    }

    /// <summary>Removes the question at <paramref name="index"/>, then
    /// re-indexes <c>DisplayOrder</c> 0..n over the whole list (EC-7).
    /// Out-of-range indices are ignored so a stale click cannot
    /// throw inside the editor.</summary>
    public void RemoveQuestionAt(int index)
    {
        if (index < 0 || index >= Questions.Count)
        {
            return;
        }
        Questions.RemoveAt(index);
        ReindexQuestions();
    }

    /// <summary>Appends generated questions to the list (never replaces;
    /// see decision (d)). Maps each DTO via
    /// <see cref="QuestionEditorRow.FromGenerated"/>, then re-indexes
    /// <c>DisplayOrder</c> 0..n (EC-7).</summary>
    public void AppendGenerated(IReadOnlyList<GeneratedQuestionDto> generated)
    {
        if (generated is null || generated.Count == 0)
        {
            return;
        }
        foreach (var dto in generated)
        {
            Questions.Add(QuestionEditorRow.FromGenerated(dto));
        }
        ReindexQuestions();
    }

    /// <summary>Re-assigns <c>DisplayOrder</c> 0..n over the whole list
    /// (EC-7). Called by every mutating helper.</summary>
    private void ReindexQuestions()
    {
        for (var i = 0; i < Questions.Count; i++)
        {
            Questions[i].DisplayOrder = i;
        }
    }

    /// <summary>Appends a staged <see cref="AttachmentEditorRow"/> to
    /// <see cref="Attachments"/> (WS-A1 / FR-210–212). Called by the
    /// Resources UI section after a successful
    /// <c>StageAttachmentAsync</c> returns the <c>StoragePath</c>.
    /// No re-indexing is needed — the create payload projects the list
    /// in order, and the server keeps that order.</summary>
    public void AddAttachment(AttachmentEditorRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        Attachments.Add(row);
    }

    /// <summary>Removes the staged attachment at <paramref name="index"/>
    /// (FR-212 — soft remove-X; the orphaned blob is swept server-side
    /// by <c>StagedFileSweepService</c> per decision (b)). Out-of-range
    /// indices are silently ignored so a stale click cannot throw
    /// inside the section.</summary>
    public void RemoveAttachmentAt(int index)
    {
        if (index < 0 || index >= Attachments.Count)
        {
            return;
        }
        Attachments.RemoveAt(index);
    }
}
