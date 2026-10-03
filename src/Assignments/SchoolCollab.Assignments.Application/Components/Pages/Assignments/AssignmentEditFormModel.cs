using SchoolCollab.AI.Abstractions;
using SchoolCollab.Assignments.Contracts;
using System.Text.Json;

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

    /// <summary>INS-1 (assignment-authoring-compartments §9): student-facing task text,
    /// distinct from <see cref="Description"/> (the internal/author summary).
    /// Round-trips through create/update so an edit never silently drops it.</summary>
    public string? Instructions { get; set; }

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

    /// <summary>WS-B2 (spec §3.4 / decision g): the URL rows the author added
    /// as AI reference material. Mapped into the create request's
    /// <c>Resources</c> as <see cref="ResourceKindDto.Url"/> rows and fed to the
    /// question generator as <see cref="Resources"/> texts (≤3).</summary>
    public List<ResourceUrlRow> ResourceUrls { get; } = [];

    /// <summary>Persisted resources whose kind the URL editor does not own
    /// (<see cref="ResourceKindDto.File"/> / <see cref="ResourceKindDto.Video"/>, which
    /// only a direct API write or a duplicated assignment can produce).
    /// <para>Loaded by <see cref="LoadChildren"/> and re-projected verbatim, because the
    /// update handler full-replaces <c>Resources</c> whenever the collection is non-null:
    /// without them, editing one URL would silently delete every non-URL resource of the
    /// assignment.</para></summary>
    public List<NewResourceDto> PreservedResources { get; } = [];

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

    /// <summary>WS-A3 (spec §3.3): pass/fail score threshold on the
    /// assignment (decimal?, null = no pass/fail signal). Hidden in the
    /// UI for TeacherGraded assignments (the
    /// <see cref="ScoringFieldsSection"/> conditional); surfaced for
    /// AutoGraded + InstantGraded only. Round-trips through the
    /// create / update request.</summary>
    public decimal? PassScore { get; set; }

    /// <summary>WS-A3 (spec §7 Q4): max submission attempts (int?, null
    /// = unlimited). When set must be &gt;= 1. Same conditional rule
    /// as <see cref="PassScore"/> — hidden for TeacherGraded.</summary>
    public int? MaxAttempts { get; set; }

    /// <summary>WS-C1 / spec §7 Q1: whether a guardian signature is
    /// required after completion. Round-trips through create/update so
    /// the edit page never silently resets the flag.</summary>
    public bool RequiresSignature { get; set; }

    /// <summary>WS-B2 (spec §3.4 line 70): requested per-difficulty counts.
    /// Round-trip through create/update so the edit page never silently resets
    /// the AI difficulty mix.</summary>
    public int? DifficultyEasyCount { get; set; }

    /// <summary>WS-B2 (spec §3.4 line 70): requested medium-question count.</summary>
    public int? DifficultyMediumCount { get; set; }

    /// <summary>WS-B2 (spec §3.4 line 70): requested hard-question count.</summary>
    public int? DifficultyHardCount { get; set; }

    /// <summary>R2 (TGT-1): one authored targeting constraint in the form model — the kind plus
    /// its reference (null exactly for <see cref="TargetKindDto.AllStudents"/>). The list order IS
    /// the display order, re-indexed 0..n-1 by <see cref="ToTargetDtos"/>.</summary>
    public sealed record AssignmentTargetSpec(TargetKindDto Kind, Guid? RefId);

    private readonly List<AssignmentTargetSpec> _targets = [];
    private readonly List<AssignmentTargetSpec> _loadedTargets = [];

    /// <summary>F4: the constraint set that was in place when Everyone was switched ON, kept so the
    /// OFF transition restores the author's work instead of silently discarding it. Null whenever
    /// Everyone was not switched on in this session.</summary>
    private List<AssignmentTargetSpec>? _everyonePriorTargets;

    /// <summary>The authored targeting constraints (TGT-1) — the Audience &amp; Targets
    /// compartment's selection, in display order.</summary>
    public IReadOnlyList<AssignmentTargetSpec> Targets => _targets.AsReadOnly();

    /// <summary>UX-21 fail-closed gate: true once the persisted target set has been read (or a
    /// Create surface has nothing to read). False means an editable Edit surface could not load
    /// the constraints, so the editor must render disabled-with-reason — a first add must never
    /// turn an unknown set into a full-replacement payload.</summary>
    public bool TargetsLoaded { get; private set; }

    public bool HasAllStudents => _targets.Any(t => t.Kind == TargetKindDto.AllStudents);

    public int CountOf(TargetKindDto kind) => _targets.Count(t => t.Kind == kind);

    /// <summary>The target set as the wire DTOs, DisplayOrder re-indexed 0..n-1 by list
    /// position (the EC-7 re-indexing convention).</summary>
    public IReadOnlyList<AssignmentTargetDto> ToTargetDtos() =>
        _targets.Select((t, i) => new AssignmentTargetDto(t.Kind, t.RefId, i)).ToList();

    /// <summary>
    /// Loads the persisted targeting rows (D-8.3) in their stored <c>DisplayOrder</c> and records
    /// the same sequence as the change-detection baseline. A null <paramref name="targets"/> clears
    /// the set and marks it NOT loaded (the caller renders the editor disabled in that case).
    /// <para>R2-10 (P2): the load also discards a pending Everyone snapshot. The snapshot belongs
    /// to the target set that was live when Everyone was switched ON, and a reused component
    /// instance (the host page's <c>Id</c> changes) loads the next assignment through this same
    /// model — leaving the snapshot behind let switching Everyone OFF restore the PREVIOUS
    /// assignment's constraints onto the freshly loaded one.</para>
    /// </summary>
    public void LoadTargets(IReadOnlyList<AssignmentTargetDto>? targets)
    {
        _everyonePriorTargets = null;
        _targets.Clear();
        _loadedTargets.Clear();
        TargetsLoaded = false;

        if (targets is null)
        {
            return;
        }

        foreach (var target in targets.OrderBy(t => t.DisplayOrder))
        {
            _targets.Add(new AssignmentTargetSpec(target.Kind, target.RefId));
            _loadedTargets.Add(new AssignmentTargetSpec(target.Kind, target.RefId));
        }

        TargetsLoaded = true;
    }

    /// <summary>
    /// Replaces every target of one kind with <paramref name="refIds"/>, keeping the other kinds
    /// (a picker reports only its own kind's selection). A duplicate id inside
    /// <paramref name="refIds"/> is collapsed — the server rejects a duplicate
    /// <c>(Kind, RefId)</c> at save time (TGT-1), so the form must not offer one.
    /// </summary>
    public void SetTargetsOfKind(TargetKindDto kind, IReadOnlyList<Guid> refIds)
    {
        _targets.RemoveAll(t => t.Kind == kind);
        foreach (var refId in refIds.Distinct())
        {
            _targets.Add(new AssignmentTargetSpec(kind, refId));
        }
    }

    /// <summary>
    /// TGT-2: the "Everyone" toggle. On, the target set becomes exactly one
    /// <see cref="TargetKindDto.AllStudents"/> row (mutually exclusive with every other kind — the
    /// constraint pickers render disabled). Off, the constraint set that was in place when Everyone
    /// was switched on is RESTORED — the author's work is kept, not silently discarded (F4). With no
    /// such snapshot (an Everyone row that was loaded, never toggled) the set is emptied for
    /// re-authoring, which is the pre-F4 behaviour.
    /// </summary>
    public void SetEveryoneTarget(bool everyone)
    {
        if (everyone)
        {
            // An AllStudents row IS the Everyone state, never a prior constraint, so it is not part
            // of what the OFF transition puts back.
            _everyonePriorTargets = _targets.Where(t => t.Kind != TargetKindDto.AllStudents).ToList();
            _targets.Clear();
            _targets.Add(new AssignmentTargetSpec(TargetKindDto.AllStudents, null));
            return;
        }

        _targets.Clear();
        if (_everyonePriorTargets is { Count: > 0 })
        {
            _targets.AddRange(_everyonePriorTargets);
        }

        // Consumed: a later ON re-snapshots whatever the set is at that point.
        _everyonePriorTargets = null;
    }

    /// <summary>Removes the target at <paramref name="index"/> (the chip list's removal action);
    /// out-of-range indices are ignored so a stale click cannot throw. DisplayOrder re-indexes
    /// implicitly because the list order IS the order.</summary>
    public void RemoveTargetAt(int index)
    {
        if (index < 0 || index >= _targets.Count)
        {
            return;
        }

        _targets.RemoveAt(index);
    }

    /// <summary>D-4/UX-21 change gate: true when the current selection differs from the loaded
    /// baseline, so an untouched editor never issues a full-replacement write.</summary>
    public bool TargetsChanged =>
        _targets.Count != _loadedTargets.Count
        || !_targets.Select(t => (t.Kind, t.RefId))
            .SequenceEqual(_loadedTargets.Select(t => (t.Kind, t.RefId)));

    /// <summary>Fixed question page size for the editor + review paginator
    /// (spec §0 decision 9 / FR-240).</summary>
    public const int QuestionPageSize = 5;

    /// <summary>
    /// Projects an <see cref="AssignmentSummaryDto"/> into a brand-new, fully-
    /// populated <see cref="AssignmentEditFormModel"/>. The question/attachment/resource
    /// collections are NOT part of the summary DTO — the Edit surface loads them separately
    /// via <see cref="LoadChildren"/>.
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
        // INS-1 (assignment-authoring-compartments §9): student-facing text round-trip.
        Instructions = assignment.Instructions;
        DueDate = assignment.DueDate?.DateTime;
        MaxScore = assignment.MaxScore;
        ArchiveGraceDays = assignment.ArchiveGraceDays;
        // WS-A3 (spec §3.3 + §7 Q4): pass/fail threshold + attempt cap
        // — round-trip so the edit page never resets them to defaults.
        PassScore = assignment.PassScore;
        MaxAttempts = assignment.MaxAttempts;
        // WS-C1 (spec §7 Q1): guardian-signature snapshot round-trip.
        RequiresSignature = assignment.RequiresSignature;
        // WS-B2 (spec §3.4 line 70): difficulty mix round-trip.
        DifficultyEasyCount = assignment.DifficultyEasyCount;
        DifficultyMediumCount = assignment.DifficultyMediumCount;
        DifficultyHardCount = assignment.DifficultyHardCount;
    }

    /// <summary>
    /// Loads this model's editable child collections from the authoring child read
    /// (assignment-authoring P1 rework — the load half of Edit parity). Replaces
    /// <see cref="Questions"/>, <see cref="Attachments"/>, <see cref="ResourceUrls"/> and
    /// <see cref="PreservedResources"/> outright, so calling it never leaves a stale
    /// row behind. A null <paramref name="children"/> clears them (the caller renders
    /// the editors disabled in that case — a cleared form is never submitted as a
    /// replacement).
    /// </summary>
    public void LoadChildren(AssignmentAuthoringChildrenDto? children)
    {
        Questions.Clear();
        Attachments.Clear();
        ResourceUrls.Clear();
        PreservedResources.Clear();

        if (children is null)
        {
            // R2 (UX-21): the target load shares this fail-closed contract — a null children read
            // leaves the targeting constraints NOT loaded, so their editor renders disabled.
            LoadTargets(null);
            return;
        }

        LoadTargets(children.Targets);

        // Display order is re-indexed 0..n by load position (EC-7) — the read already
        // ordered by the persisted DisplayOrder.
        foreach (var question in children.Questions)
        {
            var row = new QuestionEditorRow
            {
                QuestionText = question.QuestionText,
                Type = question.QuestionType,
                ModelAnswer = question.ModelAnswer,
            };

            var options = question.Options ?? [];
            for (var i = 0; i < options.Count; i++)
            {
                row.Options.Add(new OptionEditorRow { OptionText = options[i].OptionText });
                if (options[i].IsCorrect && row.CorrectOptionIndex is null)
                {
                    row.CorrectOptionIndex = i;
                }
            }

            AddQuestion(row);
        }

        foreach (var attachment in children.Attachments)
        {
            Attachments.Add(new AttachmentEditorRow
            {
                FileName = attachment.FileName,
                ContentType = attachment.ContentType,
                FileSize = attachment.FileSize,
                StoragePath = attachment.StoragePath,
            });
        }

        foreach (var resource in children.Resources)
        {
            if (resource.ResourceKind is ResourceKindDto.Url && !string.IsNullOrWhiteSpace(resource.Url))
            {
                ResourceUrls.Add(new ResourceUrlRow(resource.Url, resource.DisplayName));
            }
            else
            {
                // No editor owns this row (a File/Video resource has no URL block), so it
                // is kept verbatim and re-projected by ToCreateRequest — dropping it would
                // make the next save delete it.
                PreservedResources.Add(new NewResourceDto(
                    resource.ResourceKind,
                    resource.Url,
                    resource.StoragePath,
                    resource.DisplayName,
                    resource.IncludedInGeneration));
            }
        }
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
        bool mandatoryReview,
        bool requiresSignature = false,
        /// <summary>R2 (TGT-1): the authored targeting constraints. Null on a create means "no
        /// targets supplied" (publish is then refused until they are authored); on an update null
        /// preserves the persisted set (the caller's change gate).</summary>
        IReadOnlyList<AssignmentTargetDto>? targets = null)
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

        IReadOnlyList<NewResourceDto>? resources = null;
        if (ResourceUrls.Count > 0 || PreservedResources.Count > 0)
        {
            // Preserved rows (File/Video resources loaded from the persisted set) come
            // first so the projection round-trips the whole resource set, not just the
            // URL rows the editor owns.
            resources = PreservedResources
                .Concat(ResourceUrls.Select(r => new NewResourceDto(
                    ResourceKind: ResourceKindDto.Url,
                    Url: r.Url,
                    StoragePath: null,
                    DisplayName: r.DisplayName,
                    IncludedInGeneration: true)))
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
            Attachments: attachments,
            Resources: resources,
            // WS-A3 (spec §3.3 + §7 Q4): pass/fail threshold + attempt
            // cap threaded to the wire surface.
            PassScore: PassScore,
            MaxAttempts: MaxAttempts,
            // WS-C1 (spec §7 Q1): guardian-signature snapshot.
            RequiresSignature: requiresSignature,
            // WS-B2 (spec §3.4 line 70): difficulty mix threaded to create.
            DifficultyEasyCount: DifficultyEasyCount,
            DifficultyMediumCount: DifficultyMediumCount,
            DifficultyHardCount: DifficultyHardCount,
            // INS-1 (assignment-authoring-compartments §9): student-facing text.
            Instructions: Instructions,
            // R2 (TGT-1 / D-1): the authored targeting constraints.
            Targets: targets);
    }

    /// <summary>
    /// Projects this form model into an <see cref="UpdateAssignmentRequest"/> for the
    /// assignment identified by the route. Page-level values that live outside the model
    /// (type, grading, audience, subject, grade level, review/signature flags) are passed in
    /// as arguments — the <see cref="ToCreateRequest"/> precedent.
    /// <para>Child collections follow the wire contract's null-means-preserve rule: an empty
    /// editor projects <c>null</c> (not an empty list) so an edit that touches only scalar
    /// fields can never wipe the persisted questions/attachments/resources — the update
    /// handler's full-replacement semantics treat a non-null empty collection as "clear".</para>
    /// </summary>
    public UpdateAssignmentRequest ToUpdateRequest(
        AssignmentTypeDto assignmentType,
        GradingFormatDto gradingFormat,
        TargetAudienceTypeDto targetAudienceType,
        Guid topicId,
        Guid? gradeLevelId,
        bool mandatoryReview,
        bool requiresSignature = false,
        IReadOnlyList<AssignmentTargetDto>? targets = null)
    {
        var create = ToCreateRequest(
            assignmentType, gradingFormat, targetAudienceType, topicId, gradeLevelId,
            mandatoryReview, requiresSignature, targets);

        return new UpdateAssignmentRequest(
            Title: create.Title,
            Description: create.Description,
            AssignmentType: create.AssignmentType,
            GradingFormat: create.GradingFormat,
            TargetAudienceType: create.TargetAudienceType,
            TopicId: create.TopicId,
            GradeLevelId: create.GradeLevelId,
            DueDate: create.DueDate,
            MaxScore: create.MaxScore,
            MandatoryReview: create.MandatoryReview,
            AiPromptOverride: create.AiPromptOverride,
            Questions: create.Questions,
            Attachments: create.Attachments,
            ContentModules: create.ContentModules,
            Resources: create.Resources,
            ArchiveGraceDays: ArchiveGraceDays,
            PassScore: create.PassScore,
            MaxAttempts: create.MaxAttempts,
            RequiresSignature: create.RequiresSignature,
            DifficultyEasyCount: create.DifficultyEasyCount,
            DifficultyMediumCount: create.DifficultyMediumCount,
            DifficultyHardCount: create.DifficultyHardCount,
            Instructions: Instructions,
            Targets: create.Targets);
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

    /// <summary>WS-B2 (spec §3.4 / decision g) — adds a reference URL row.
    /// Trims the input; an empty/blank URL throws <see cref="ArgumentException"/>
    /// (mirrors the server-side guard), and an exact duplicate returns
    /// <see langword="false"/> without adding while a new row returns
    /// <see langword="true"/>.</summary>
    public bool AddResourceUrl(string url)
    {
        var trimmed = url.Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("A URL is required.", nameof(url));
        }
        if (ResourceUrls.Any(r => string.Equals(r.Url, trimmed, StringComparison.Ordinal)))
        {
            return false;
        }
        ResourceUrls.Add(new ResourceUrlRow(trimmed, null));
        return true;
    }

    /// <summary>WS-B2 — removes the reference-URL row at <paramref name="index"/>.</summary>
    public void RemoveResourceUrlAt(int index)
    {
        if (index < 0 || index >= ResourceUrls.Count)
        {
            return;
        }
        ResourceUrls.RemoveAt(index);
    }

    /// <summary>
    /// UX-7 (D3): a canonical snapshot of everything a save from this form would write — the update
    /// payload, serialized. Page-level values that live outside the model are passed in exactly as
    /// <see cref="ToUpdateRequest"/> takes them, so two form states compare equal exactly when the
    /// next save would write the same thing; a value changed and then changed back is equal again,
    /// which is what makes the page's unsaved-changes guard a BASELINE comparison rather than an
    /// event flag (an event flag prompts for a difference that no longer exists).
    /// <para>The authored target rows always ride the snapshot — never the update path's
    /// null-means-preserve gate — so an untouched targeting editor compares equal to itself.</para>
    /// </summary>
    public string CaptureSaveSnapshot(
        AssignmentTypeDto assignmentType,
        GradingFormatDto gradingFormat,
        TargetAudienceTypeDto targetAudienceType,
        Guid topicId,
        Guid? gradeLevelId,
        bool mandatoryReview,
        bool requiresSignature) =>
        JsonSerializer.Serialize(ToUpdateRequest(
            assignmentType, gradingFormat, targetAudienceType, topicId, gradeLevelId,
            mandatoryReview, requiresSignature, ToTargetDtos()));

    /// <summary>WS-A3 (spec §3.3 + §7 Q4) — client-side submit gate
    /// mirroring the server-side <c>Assignment.Create</c> /
    /// <c>Assignment.Update</c> validation rules on
    /// <see cref="PassScore"/> + <see cref="MaxAttempts"/>. Mirrors the
    /// <see cref="QuestionsPassSubmitGate"/> shape: returns false +
    /// the first violation message, or true with <c>error = null</c>.
    /// For <see cref="GradingFormatDto.TeacherGraded"/> the gate
    /// always passes — the fields are hidden (stale values must not
    /// block submit with an invisible error).</summary>
    public bool ScoringFieldsPassSubmitGate(GradingFormatDto gradingFormat, out string? error)
    {
        if (gradingFormat is not (GradingFormatDto.AutoGraded or GradingFormatDto.InstantGraded))
        {
            // TeacherGraded (and any future non-auto format): the
            // fields are hidden, never block submit.
            error = null;
            return true;
        }

        if (PassScore is decimal passScore && passScore < 0m)
        {
            error = "Pass score cannot be negative.";
            return false;
        }

        if (MaxAttempts is int maxAttempts && maxAttempts < 1)
        {
            error = "Max attempts must be at least 1.";
            return false;
        }

        if (PassScore.HasValue && MaxScore.HasValue && PassScore.Value > MaxScore.Value)
        {
            error = "Pass score must not exceed the max score.";
            return false;
        }

        error = null;
        return true;
    }
}
