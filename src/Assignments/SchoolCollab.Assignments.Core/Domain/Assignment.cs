using SchoolCollab.Assignments.Core.Domain.Events;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Core.Domain;

public sealed class Assignment : ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion
{
    private readonly List<AssignmentQuestion> _questions = [];
    private readonly List<AssignmentReview> _reviews = [];
    private readonly List<AssignmentAttachment> _attachments = [];
    private readonly List<ContentModule> _modules = [];
    private readonly List<AssignmentResource> _resources = [];
    private readonly List<AssignmentTarget> _targets = [];
    private readonly List<IDomainEvent> _domainEvents = [];

    private Assignment() { }

    public Guid Id { get; private set; }
    public string Title { get; private set; } = default!;
    public string? Description { get; private set; }
    /// <summary>INS-1 (assignment-authoring-compartments §9) — student-facing task
    /// text, distinct from <see cref="Description"/> (the internal/author summary).</summary>
    public string? Instructions { get; private set; }
    public AssignmentType AssignmentType { get; private set; }
    public GradingFormat GradingFormat { get; private set; }
    public TargetAudienceType TargetAudienceType { get; private set; }
    // Operational reference into the Students bounded context (the global Topic
    // entity). This replaces the former coded-value id so the assignment reports
    // against the real operational entity; the display name is still resolved
    // client-side from tenant-resolved coded values (spec §5.7). The assignment's
    // grades live in its <see cref="Targets"/> rows (TargetKind.GradeLevel) — there is
    // no separately-authored primary grade.
    public Guid TopicId { get; private set; }
    /// <summary>Auto-generated assignment code (e.g. ASGA01) — spec §3.6.</summary>
    public string? AssignmentNumber { get; private set; }

    // Multi-tenancy: all assignments belong to a tenant (e.g., school or organization)
    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }
    public Guid TenantId { get; private set; }
    public DateTimeOffset? DueDate { get; private set; }
    public decimal? MaxScore { get; private set; }
    /// <summary>WS-A3 (spec §3.3) — the score threshold at which the
    /// submission is considered passed. Null = no pass/fail signal.
    /// Mutually validated with <see cref="MaxScore"/> in
    /// <see cref="Create"/> / <see cref="Update"/>.</summary>
    public decimal? PassScore { get; private set; }
    /// <summary>WS-A3 (spec §7 Q4) — the maximum number of attempts a
    /// student may submit on this assignment. Null = unlimited. The cap
    /// is enforced by the submission handlers; a teacher override
    /// (<see cref="AssignmentSubmission.OverrideAttemptLimit"/>) or a
    /// higher value on this field clears the cap for the affected
    /// submission. Validated &gt;= 1 when set (a 0-attempt assignment
    /// would deadlock the literal cap check).</summary>
    public int? MaxAttempts { get; private set; }
    /// <summary>WS-C1 / spec §7 Q1 — whether a guardian signature is required
    /// after completion. Snapshotted at create/update from the resolved grade/tenant
    /// policy; never an author input (D6/D10). Defaults to
    /// <see langword="false"/> when not supplied.</summary>
    public bool RequiresSignature { get; private set; }
    /// <summary>WS-B2 (spec §3.4 line 70) — the requested easy-question count,
    /// when the author constrains the AI difficulty mix. Null = let the model
    /// decide; no cross-field sum validation (the prompt reconciles).</summary>
    public int? DifficultyEasyCount { get; private set; }
    /// <summary>WS-B2 (spec §3.4 line 70) — requested medium-question count.</summary>
    public int? DifficultyMediumCount { get; private set; }
    /// <summary>WS-B2 (spec §3.4 line 70) — requested hard-question count.</summary>
    public int? DifficultyHardCount { get; private set; }
    /// <summary>WS-B2 (spec §3.4 line 73) — a staged-but-unconfirmed AI question
    /// set (JSON of <c>NewQuestionDto[]</c>). Only confirmed questions enter
    /// <see cref="Questions"/>; staging is server-side so a draft survives page
    /// reloads. Null when no draft is staged.</summary>
    public string? QuestionsDraftJson { get; private set; }
    public AssignmentStatus Status { get; private set; }
    public Guid CreatedByTeacherId { get; private set; }
    /// <summary>
    /// When true (default), student self-submit is blocked until a Primary
    /// guardian reviews + enables (or submits on behalf). When false, the
    /// gate is optional (spec §4.7). Snapshotted at create/update from the resolved
    /// effective policy (the author supplies only the fallback the policy leaves
    /// unset — D3/OD1) and constrained by the D4 implication:
    /// <see cref="RequiresSignature"/> implies this flag.
    /// </summary>
    public bool MandatoryReview { get; private set; }
    /// <summary>Set when the assignment is published (spec §4.8). Null while Draft.</summary>
    public DateTimeOffset? PublishedAt { get; private set; }
    /// <summary>Optional free-text override for the assignment-question-generation
    /// system prompt (spec §3.2 / decision 8). Null falls back to the embedded prompt.</summary>
    public string? AiPromptOverride { get; private set; }
    /// <summary>When the assignment is scheduled to auto-publish
    /// (spec §3.5 step 2 / WS-A2). Cleared on unpublish. Null while
    /// the assignment is in any other state.</summary>
    public DateTimeOffset? AvailableFromUtc { get; private set; }
    /// <summary>Days added to <see cref="DueDate"/> to compute the
    /// archive moment (spec §7 Q6). Defaults to 30 — the archive
    /// sweep honours per-row overrides.</summary>
    public int ArchiveGraceDays { get; private set; }
    /// <summary>The approval status (spec §7 Q2). Null when the
    /// assignment has not been submitted for approval — the default
    /// state. Only meaningful when the effective assignment policy's
    /// <c>RequiresApprovalBeforePublish</c> is true, or the
    /// <c>FEATURE:RequireAssignmentApproval</c> flag is on (the two are
    /// OR'd for one release — D3, documents/solution/assignment-policy-fields.md).</summary>
    public ApprovalStatus? ApprovalStatus { get; private set; }
    /// <summary>The id of the user who approved the assignment.
    /// Cleared on <see cref="Reject"/>. Null until the assignment is
    /// approved.</summary>
    public Guid? ApprovedBy { get; private set; }
    /// <summary>The UTC moment an approval was granted.</summary>
    public DateTimeOffset? ApprovedAt { get; private set; }
    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<AssignmentQuestion> Questions => _questions.AsReadOnly();
    public IReadOnlyList<AssignmentReview> Reviews => _reviews.AsReadOnly();
    public IReadOnlyList<AssignmentAttachment> Attachments => _attachments.AsReadOnly();
    /// <summary>Standalone tenant child (WS-A1): content modules the
    /// student consumes to satisfy the assignment. The FK is declared
    /// once from the aggregate side (cascade); rows persist as
    /// first-class records, not owned types.</summary>
    public IReadOnlyList<ContentModule> Modules => _modules.AsReadOnly();
    /// <summary>Standalone tenant child (WS-A1): AI-generation inputs
    /// (links / files / videos). Same persistence model as
    /// <see cref="Modules"/>.</summary>
    public IReadOnlyList<AssignmentResource> Resources => _resources.AsReadOnly();

    /// <summary>The authored targeting constraints (TGT-1) — the sole authored source of
    /// who receives a published assignment. <see cref="TargetAudienceType"/> is DERIVED from
    /// this set (D-1), never authored against it. Unordered at the EF level; consumers that
    /// render the author's order sort by <c>DisplayOrder</c>.</summary>
    public IReadOnlyList<AssignmentTarget> Targets => _targets.AsReadOnly();
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public static Assignment Create(
        string title,
        string? description,
        AssignmentType assignmentType,
        GradingFormat gradingFormat,
        TargetAudienceType targetAudienceType,
        Guid topicId,
        DateTimeOffset? dueDate,
        decimal? maxScore,
        Guid createdByTeacherId = default,
        bool mandatoryReview = true,
        string? assignmentNumber = null,
        string? aiPromptOverride = null,
        int archiveGraceDays = 30,
        /// <summary>WS-A3 (spec §3.3): pass/fail score threshold. Null
        /// means no pass/fail signal. When both are set must be &lt;=
        /// <paramref name="maxScore"/>.</summary>
        decimal? passScore = null,
        /// <summary>WS-A3 (spec §7 Q4): max submission attempts. Null
        /// means unlimited. When set must be &gt;= 1 (a 0-attempt
        /// assignment would deadlock the literal cap check).</summary>
        int? maxAttempts = null,
        /// <summary>WS-C1 / spec §7 Q1: whether a guardian signature is
        /// required after completion. Snapshotted from the resolved
        /// grade/tenant policy; never an author override (D10).</summary>
        bool requiresSignature = false,
        /// <summary>WS-B2 (spec §3.4 line 70): requested optional per-difficulty
        /// counts. Null = let the model decide; no cross-field sum validation.</summary>
        int? difficultyEasy = null,
        int? difficultyMedium = null,
        int? difficultyHard = null,
        /// <summary>INS-1 (assignment-authoring-compartments §9): student-facing
        /// task text. Null when the author supplied none.</summary>
        string? instructions = null)
    {
        if (topicId == Guid.Empty)
            throw new ArgumentException("Topic is required.", nameof(topicId));
        if (maxScore.HasValue && passScore.HasValue && passScore.Value > maxScore.Value)
            throw new ArgumentException("Pass score must not exceed the max score.", nameof(passScore));
        if (maxAttempts.HasValue && maxAttempts.Value < 1)
            throw new ArgumentException("Max attempts must be at least 1.", nameof(maxAttempts));
        if (difficultyEasy < 0 || difficultyMedium < 0 || difficultyHard < 0)
            throw new ArgumentException("Difficulty counts must be zero or greater.", nameof(difficultyEasy));
        // D4 backstop: a signed assignment must also require guardian review, exactly as
        // passScore <= maxScore is asserted here rather than only at the resolution seam. A
        // caller that bypasses the effective-policy resolver cannot persist the contradiction.
        if (requiresSignature && !mandatoryReview)
            throw new ArgumentException(
                "A signature-required assignment must also require guardian review.", nameof(mandatoryReview));

        var now = DateTimeOffset.UtcNow;
        var assignment = new Assignment
        {
            Id = Guid.NewGuid(),
            Title = title.Trim(),
            Description = description?.Trim(),
            Instructions = instructions?.Trim(),
            AssignmentType = assignmentType,
            GradingFormat = gradingFormat,
            TargetAudienceType = targetAudienceType,
            TopicId = topicId,
            DueDate = dueDate,
            MaxScore = maxScore,
            PassScore = passScore,
            MaxAttempts = maxAttempts,
            RequiresSignature = requiresSignature,
            DifficultyEasyCount = difficultyEasy,
            DifficultyMediumCount = difficultyMedium,
            DifficultyHardCount = difficultyHard,
            Status = AssignmentStatus.Draft,
            CreatedByTeacherId = createdByTeacherId,
            // Mandatory review is the default (spec §4.7); callers may opt out.
            MandatoryReview = mandatoryReview,
            AssignmentNumber = assignmentNumber?.Trim(),
            AiPromptOverride = aiPromptOverride?.Trim(),
            // WS-A2: archive grace window (spec §7 Q6). Defaults to 30 —
            // the archive sweep honours per-row overrides.
            ArchiveGraceDays = archiveGraceDays,
            // TenantId will be set by the command handler via ITenantEntity.WithTenant()
            CreatedAt = now,
            UpdatedAt = now
        };

        // TGT-15 / D-1: the legacy compat column is derived from the authored target rows,
        // which the caller attaches with SetTargets once the tenant is stamped (the child
        // rows carry the tenant id, so they cannot be created before WithTenant runs).
        assignment.SyncDerivedTargeting();

        assignment._domainEvents.Add(new AssignmentCreatedEvent(assignment.Id, assignment.Title));
        return assignment;
    }

    public void Update(string title, string? description, AssignmentType assignmentType,
        GradingFormat gradingFormat, TargetAudienceType targetAudienceType,
        Guid topicId, DateTimeOffset? dueDate, decimal? maxScore,
        bool mandatoryReview, string? aiPromptOverride = null, int archiveGraceDays = 30,
        /// <summary>WS-A3 (spec §3.3): pass/fail score threshold. Null
        /// means no pass/fail signal. When both are set must be &lt;=
        /// <paramref name="maxScore"/>.</summary>
        decimal? passScore = null,
        /// <summary>WS-A3 (spec §7 Q4): max submission attempts. Null
        /// means unlimited. When set must be &gt;= 1.</summary>
        int? maxAttempts = null,
        /// <summary>WS-C1 / spec §7 Q1: whether a guardian signature is
        /// required after completion. Re-snapshotted from the currently resolved
        /// grade/tenant policy; never an author override (D6/D10).</summary>
        bool requiresSignature = false,
        /// <summary>WS-B2 (spec §3.4 line 70): requested optional per-difficulty
        /// counts. Null = let the model decide; no cross-field sum validation.</summary>
        int? difficultyEasy = null,
        int? difficultyMedium = null,
        int? difficultyHard = null,
        /// <summary>INS-1 (assignment-authoring-compartments §9): student-facing
        /// task text. Null when the author supplied none.</summary>
        string? instructions = null)
    {
        if (Status is not (AssignmentStatus.Draft or AssignmentStatus.Scheduled))
            throw new InvalidOperationException("Only draft or scheduled assignments can be updated.");
        if (topicId == Guid.Empty)
            throw new ArgumentException("Topic is required.", nameof(topicId));
        if (maxScore.HasValue && passScore.HasValue && passScore.Value > maxScore.Value)
            throw new ArgumentException("Pass score must not exceed the max score.", nameof(passScore));
        if (maxAttempts.HasValue && maxAttempts.Value < 1)
            throw new ArgumentException("Max attempts must be at least 1.", nameof(maxAttempts));
        if (difficultyEasy < 0 || difficultyMedium < 0 || difficultyHard < 0)
            throw new ArgumentException("Difficulty counts must be zero or greater.", nameof(difficultyEasy));
        // D4 backstop — the Update half of the Create guard above.
        if (requiresSignature && !mandatoryReview)
            throw new ArgumentException(
                "A signature-required assignment must also require guardian review.", nameof(mandatoryReview));

        Title = title.Trim();
        Description = description?.Trim();
        Instructions = instructions?.Trim();
        AssignmentType = assignmentType;
        GradingFormat = gradingFormat;
        TargetAudienceType = targetAudienceType;
        TopicId = topicId;
        DueDate = dueDate;
        MaxScore = maxScore;
        PassScore = passScore;
        MaxAttempts = maxAttempts;
        RequiresSignature = requiresSignature;
        DifficultyEasyCount = difficultyEasy;
        DifficultyMediumCount = difficultyMedium;
        DifficultyHardCount = difficultyHard;
        MandatoryReview = mandatoryReview;
        AiPromptOverride = aiPromptOverride?.Trim();
        ArchiveGraceDays = archiveGraceDays;
        UpdatedAt = DateTimeOffset.UtcNow;
        SyncDerivedTargeting();
        _domainEvents.Add(new AssignmentUpdatedEvent(Id, Title));
    }

    /// <summary>
    /// Replaces the authored targeting constraints (TGT-1, UX-21 full-replacement semantics):
    /// a non-null <paramref name="targets"/> replaces the whole set and re-indexes
    /// <c>DisplayOrder</c> 0..n-1; <see langword="null"/> preserves the current rows (the
    /// questions/attachments/modules null-means-preserve contract). Validation, in the order
    /// the spec states it: TGT-13 at-least-one, TGT-2 <c>AllStudents</c> exclusivity and
    /// uniqueness, per-kind duplicate <c>(Kind, RefId)</c> rejection, and the D-8.1 archived-group
    /// rejection for NEWLY-ADDED group targets.
    /// <para>D-8.1: an already-persisted group target whose group was archived after linking
    /// is NOT re-validated, so a re-save never silently drops the historical row; archived
    /// state still filters recipients at resolution (EC-4) and at the topic gate.</para>
    /// </summary>
    /// <param name="inactiveActivityGroupIds">The activity-group ids the caller resolved as
    /// not active via <c>IActivityGroupLookup.GetByIdsAsync</c> (the port lives outside the
    /// domain). Only ids absent from the persisted set are treated as newly added.</param>
    public void SetTargets(
        IReadOnlyList<(TargetKind Kind, Guid? RefId)>? targets,
        Guid tenantId,
        IReadOnlyCollection<Guid>? inactiveActivityGroupIds = null)
    {
        if (targets is null)
            return;

        // TGT-13: at least one target is required.
        if (targets.Count == 0)
            throw new ArgumentException("At least one target is required.", nameof(targets));

        // TGT-2: AllStudents is mutually exclusive with every other kind, and may appear once.
        var allStudentsCount = targets.Count(t => t.Kind == TargetKind.AllStudents);
        if (allStudentsCount > 1)
            throw new ArgumentException("Only one AllStudents target is allowed.", nameof(targets));
        if (allStudentsCount == 1 && targets.Count > 1)
            throw new ArgumentException(
                "An AllStudents target cannot be combined with any other target.", nameof(targets));

        // One target per (kind, reference) — the DB unique index backstops this.
        var seen = new HashSet<(TargetKind Kind, Guid? RefId)>();
        foreach (var target in targets)
        {
            if (!seen.Add((target.Kind, target.RefId)))
                throw new ArgumentException($"Duplicate {target.Kind} target.", nameof(targets));
        }

        // D-8.1 (FR-22 / AC-15): reject a NEWLY-ADDED group target whose group is not active.
        var persistedGroupIds = _targets
            .Where(t => t.Kind == TargetKind.ActivityGroup && t.RefId.HasValue)
            .Select(t => t.RefId!.Value)
            .ToHashSet();
        if (inactiveActivityGroupIds is { Count: > 0 })
        {
            var archivedNewIds = targets
                .Where(t => t.Kind == TargetKind.ActivityGroup && t.RefId.HasValue)
                .Select(t => t.RefId!.Value)
                .Where(id => !persistedGroupIds.Contains(id) && inactiveActivityGroupIds.Contains(id))
                .Distinct()
                .ToArray();
            if (archivedNewIds.Length > 0)
                throw new ArgumentException(
                    $"Cannot link archived activity group(s): {string.Join(", ", archivedNewIds)}");
        }

        _targets.Clear();
        for (var i = 0; i < targets.Count; i++)
        {
            _targets.Add(AssignmentTarget.Create(tenantId, Id, targets[i].Kind, targets[i].RefId, i));
        }

        UpdatedAt = DateTimeOffset.UtcNow;
        SyncDerivedTargeting();
    }

    /// <summary>
    /// D-1 (TGT-15): derives the legacy compat columns from the authored target rows. Called
    /// at the end of <see cref="Create"/>, <see cref="Update"/> and <see cref="SetTargets"/> —
    /// the three write points — so the list DTO, the Admin list surface and the ward
    /// projection keep reading a correct value without any change of their own.
    /// </summary>
    private void SyncDerivedTargeting()
    {
        if (_targets.Any(t => t.Kind == TargetKind.AllStudents))
            TargetAudienceType = TargetAudienceType.AllStudents;
        else if (_targets.Any(t => t.Kind == TargetKind.GradeLevel))
            TargetAudienceType = TargetAudienceType.SelectedGrades;
        else if (_targets.Any(t => t.Kind == TargetKind.ActivityGroup))
            TargetAudienceType = TargetAudienceType.SelectedGroups;
        else
            TargetAudienceType = TargetAudienceType.Mixed;
    }

    /// <summary>WS-B2 (spec §3.4 line 73) — stages an unconfirmed AI question
    /// set (JSON of <c>NewQuestionDto[]</c>) on <see cref="QuestionsDraftJson"/>.
    /// Draft-only; the staging handler validates + serializes before calling, so an
    /// empty payload is rejected here as <see cref="ArgumentException"/>. Stamps
    /// <see cref="UpdatedAt"/>.</summary>
    public void StageQuestionsDraft(string questionsJson)
    {
        if (Status != AssignmentStatus.Draft)
            throw new InvalidQuestionsDraftException("Question drafts can only be staged on draft assignments.");
        if (string.IsNullOrWhiteSpace(questionsJson))
            throw new ArgumentException("A questions draft payload is required.", nameof(questionsJson));
        QuestionsDraftJson = questionsJson;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>WS-B2 (spec §3.4 line 73) — drops the staged questions draft.
    /// Idempotent when no draft exists (returns without error). Stamps
    /// <see cref="UpdatedAt"/> only when a draft was actually cleared.</summary>
    public void DiscardQuestionsDraft()
    {
        if (Status != AssignmentStatus.Draft)
            throw new InvalidQuestionsDraftException("Question drafts can only be discarded on draft assignments.");
        if (QuestionsDraftJson is null)
            return;
        QuestionsDraftJson = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>WS-B2 (spec §3.4 line 73) — clears the staged blob after the
    /// caller (the confirm handler) has materialized the draft questions into
    /// <see cref="Questions"/>. The guard requires a staged blob to confirm.
    /// Stamps <see cref="UpdatedAt"/>.</summary>
    public void ConfirmQuestionsDraft()
    {
        if (Status != AssignmentStatus.Draft)
            throw new InvalidQuestionsDraftException("Question drafts can only be confirmed on draft assignments.");
        if (QuestionsDraftJson is null)
            throw new InvalidQuestionsDraftException("No staged questions draft to confirm.");
        QuestionsDraftJson = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Publish(bool approvalRequired = false)
    {
        if (Status == AssignmentStatus.Archived)
            throw new InvalidOperationException("Archived assignments are read-only.");
        if (Status == AssignmentStatus.Published)
            return;

        // WS-A2 / spec §7 Q2: when the effective assignment policy requires
        // approval (or the tenant has the approval flag on — the two are OR'd
        // for one release, D3) the publish path is gated on an explicit approve
        // decision. Serve both immediate publish (Draft) and publish-now
        // (Scheduled) with the same handler — the window has fired by definition.
        if (approvalRequired && ApprovalStatus != Domain.ApprovalStatus.Approved)
            throw new AssignmentApprovalRequiredException("This assignment requires approval before it can be published.");

        Status = AssignmentStatus.Published;
        PublishedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new AssignmentPublishedEvent(Id, Title));
    }

    public void Unpublish()
    {
        if (Status is not (AssignmentStatus.Published or AssignmentStatus.Scheduled))
            throw new InvalidOperationException("Only published or scheduled assignments can be unpublished.");

        Status = AssignmentStatus.Draft;
        AvailableFromUtc = null;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new AssignmentUnpublishedEvent(Id, Title));
    }

    public void Close()
    {
        if (Status == AssignmentStatus.Archived)
            throw new InvalidOperationException("Archived assignments are read-only.");
        if (Status == AssignmentStatus.Closed)
            return;

        Status = AssignmentStatus.Closed;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new AssignmentClosedEvent(Id, Title));
    }

    /// <summary>Schedules an assignment to auto-publish at
    /// <paramref name="availableFromUtc"/> (spec §3.5 step 2). Allowed
    /// from Draft OR Scheduled (reschedule). Past dates are rejected
    /// with <see cref="ArgumentException"/>. When
    /// <paramref name="approvalRequired"/> is true the effective
    /// policy requires approval (or the tenant's
    /// <c>FEATURE:RequireAssignmentApproval</c> flag is on — OR'd for one
    /// release, D3) — the assignment must already carry
    /// an <see cref="ApprovalStatus.Approved"/> decision.</summary>
    public void Schedule(DateTimeOffset availableFromUtc, bool approvalRequired = false)
    {
        if (Status is AssignmentStatus.Archived)
            throw new InvalidOperationException("Archived assignments are read-only.");
        if (Status is not (AssignmentStatus.Draft or AssignmentStatus.Scheduled))
            throw new InvalidOperationException("Only draft or scheduled assignments can be scheduled.");
        if (availableFromUtc <= DateTimeOffset.UtcNow)
            throw new ArgumentException("Available-from must be in the future.", nameof(availableFromUtc));
        if (approvalRequired && ApprovalStatus != Domain.ApprovalStatus.Approved)
            throw new AssignmentApprovalRequiredException("This assignment requires approval before it can be scheduled.");

        AvailableFromUtc = availableFromUtc;
        Status = AssignmentStatus.Scheduled;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new AssignmentScheduledEvent(Id, Title));
    }

    /// <summary>Archives the assignment (spec §7 Q6 — read-only
    /// retention). Idempotent on already-Archived rows; allowed from
    /// Published OR Closed only. The PublishedAt / DueDate history is
    /// preserved (the archive sweep never blanks the row).</summary>
    public void Archive()
    {
        if (Status == AssignmentStatus.Archived)
            return;
        if (Status is not (AssignmentStatus.Published or AssignmentStatus.Closed))
            throw new InvalidOperationException("Only published or closed assignments can be archived.");

        Status = AssignmentStatus.Archived;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new AssignmentArchivedEvent(Id, Title));
    }

    /// <summary>Submits a Draft assignment for approval (spec §7 Q2).
    /// Any other status is a programming error — the API surface
    /// routes this only from Draft rows.</summary>
    public void SubmitForApproval()
    {
        if (Status != AssignmentStatus.Draft)
            throw new InvalidOperationException("Only draft assignments can be submitted for approval.");

        ApprovalStatus = Domain.ApprovalStatus.Pending;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new AssignmentApprovalSubmittedEvent(Id, Title));
    }

    /// <summary>Approves a pending assignment (spec §7 Q2). Only valid
    /// when the row is currently <see cref="ApprovalStatus.Pending"/>.
    /// The empty-approver guard is argument-hygiene — the API surface
    /// uses <see cref="Guid.Empty"/> as the placeholder until identity
    /// wiring lands.</summary>
    public void Approve(Guid approverId)
    {
        if (approverId == Guid.Empty)
            throw new ArgumentException("Approver is required.", nameof(approverId));
        if (ApprovalStatus != Domain.ApprovalStatus.Pending)
            throw new InvalidOperationException("Only pending assignments can be approved.");

        ApprovalStatus = Domain.ApprovalStatus.Approved;
        ApprovedBy = approverId;
        ApprovedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new AssignmentApprovedEvent(Id, approverId));
    }

    /// <summary>Rejects a pending assignment (spec §7 Q2). Same posture
    /// as <see cref="Approve"/>; clears any prior stamps because the
    /// row is now terminal until the teacher edits and re-submits.</summary>
    public void Reject(Guid approverId)
    {
        if (approverId == Guid.Empty)
            throw new ArgumentException("Approver is required.", nameof(approverId));
        if (ApprovalStatus != Domain.ApprovalStatus.Pending)
            throw new InvalidOperationException("Only pending assignments can be rejected.");

        ApprovalStatus = Domain.ApprovalStatus.Rejected;
        ApprovedBy = null;
        ApprovedAt = null;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new AssignmentRejectedEvent(Id, approverId));
    }

    /// <summary>
    /// Appends a question to the assignment. <paramref name="generationId"> is R3's (P1-2)
    /// provenance link to the <see cref="AssignmentQuestionGeneration"/> header that produced it,
    /// or null for a hand-written row.
    /// <para>Declared as an optional parameter rather than a new overload on purpose: the existing
    /// fourth parameter is <c>string? ModelAnswer</c>, so a 4-argument overload would make the call
    /// <c>AddQuestion(text, type, 0, null)</c> ambiguous at the call site.</para>
    /// </summary>
    public AssignmentQuestion AddQuestion(
        string questionText,
        QuestionType questionType,
        int displayOrder,
        string? modelAnswer = null,
        Guid? generationId = null)
    {
        var question = new AssignmentQuestion(Id, questionText, questionType, displayOrder, modelAnswer, generationId);
        _questions.Add(question);
        UpdatedAt = DateTimeOffset.UtcNow;
        return question;
    }

    public void RemoveQuestion(Guid questionId)
    {
        var question = _questions.SingleOrDefault(q => q.Id == questionId);
        if (question is not null)
        {
            _questions.Remove(question);
            UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Appends an attachment to the assignment (WS-A1). The R3 (D4/P1-3) extraction outcome is an
    /// optional tail mirrored from <c>NewAttachmentDto</c> so it survives this row being re-minted by
    /// the next save; omitting it reproduces the pre-R3 shape exactly.
    /// </summary>
    public AssignmentAttachment AddAttachment(
        string fileName,
        string contentType,
        long fileSize,
        string storagePath,
        AttachmentExtractionStatus extractionStatus = AttachmentExtractionStatus.NotAttempted,
        string? extractedText = null,
        DateTimeOffset? extractedAt = null,
        string? extractionError = null)
    {
        var attachment = new AssignmentAttachment(
            Id, fileName, contentType, fileSize, storagePath,
            extractionStatus, extractedText, extractedAt, extractionError);
        _attachments.Add(attachment);
        UpdatedAt = DateTimeOffset.UtcNow;
        return attachment;
    }

    public void RemoveAttachment(Guid attachmentId)
    {
        var attachment = _attachments.SingleOrDefault(a => a.Id == attachmentId);
        if (attachment is not null)
        {
            _attachments.Remove(attachment);
            UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    public AssignmentReview AddReview(Guid teacherId, decimal? score, string? comments)
    {
        var review = new AssignmentReview(Id, teacherId, score, comments);
        _reviews.Add(review);
        UpdatedAt = DateTimeOffset.UtcNow;
        return review;
    }

    // ── Content modules + resources (WS-A1) ──────────────────────────

    /// <summary>Appends a content module to the assignment (draft-only).
    /// Validation is the caller's responsibility (mirrors AddQuestion).</summary>
    public ContentModule AddModule(
        ModuleType moduleType,
        string url,
        string? title = null,
        string? storagePath = null,
        int minCompletionThresholdPercent = 100,
        bool isRequired = false)
    {
        var module = ContentModule.Create(
            TenantId, Id, moduleType, url, title, storagePath,
            _modules.Count,
            minCompletionThresholdPercent, isRequired);
        _modules.Add(module);
        UpdatedAt = DateTimeOffset.UtcNow;
        return module;
    }

    /// <summary>Removes a module by id; silent no-op when not found
    /// (mirrors RemoveQuestion) so a stale inbound id never throws.</summary>
    public void RemoveModule(Guid moduleId)
    {
        var module = _modules.SingleOrDefault(m => m.Id == moduleId);
        if (module is not null)
        {
            _modules.Remove(module);
            UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>Re-indexes <c>DisplayOrder</c> 0..n over the given
    /// ordered ids. Ids not found in the current collection are
    /// ignored; modules not mentioned keep their current order
    /// (the only caller — the update handler — passes the complete
    /// re-ordered list).</summary>
    public void ReorderModules(IReadOnlyList<Guid> orderedModuleIds)
    {
        for (var i = 0; i < orderedModuleIds.Count; i++)
        {
            var id = orderedModuleIds[i];
            var module = _modules.SingleOrDefault(m => m.Id == id);
            if (module is not null)
            {
                module.SetDisplayOrder(i);
            }
        }
    }

    /// <summary>Appends an AI-generation resource (draft-only).</summary>
    public AssignmentResource AddResource(
        ResourceKind resourceKind,
        string? url = null,
        string? storagePath = null,
        string? displayName = null,
        bool includedInGeneration = true)
    {
        var resource = AssignmentResource.Create(
            TenantId, Id, resourceKind, url, storagePath, displayName,
            includedInGeneration);
        _resources.Add(resource);
        UpdatedAt = DateTimeOffset.UtcNow;
        return resource;
    }

    /// <summary>Removes a resource by id; silent no-op when not found.</summary>
    public void RemoveResource(Guid resourceId)
    {
        var resource = _resources.SingleOrDefault(r => r.Id == resourceId);
        if (resource is not null)
        {
            _resources.Remove(resource);
            UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}