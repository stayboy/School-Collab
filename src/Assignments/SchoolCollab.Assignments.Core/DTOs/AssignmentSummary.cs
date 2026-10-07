using SchoolCollab.Assignments.Core.Domain;

namespace SchoolCollab.Assignments.Core.Data.Repositories;

public record AssignmentSummary(
    Guid Id,
    string Title,
    string? Description,
    AssignmentType AssignmentType,
    GradingFormat GradingFormat,
    TargetAudienceType TargetAudienceType,
    Guid TopicId,
    /// <summary>The row's authored grade-target ids (persisted order) — the read surfaces' grade
    /// input now that the assignment carries no primary grade (round <c>drop-primary-grade</c>).
    /// Always populated by the summary projections.</summary>
    IReadOnlyList<Guid> TargetGradeIds,
    AssignmentStatus Status,
    DateTimeOffset? DueDate,
    decimal? MaxScore,
    bool MandatoryReview,
    Guid CreatedByTeacherId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    /// <summary>WS-A2 (spec §3.5 step 2): when the assignment is
    /// scheduled to auto-publish.</summary>
    DateTimeOffset? AvailableFromUtc = null,
    /// <summary>WS-A2 (spec §7 Q6): archive grace window in days.</summary>
    int ArchiveGraceDays = 30,
    /// <summary>WS-A2 (spec §7 Q2): null when the assignment has
    /// not been submitted for approval.</summary>
    ApprovalStatus? ApprovalStatus = null,
    /// <summary>WS-A2 (spec §7 Q2): who approved the assignment.
    /// Cleared on <see cref="Assignment.Reject"/>.</summary>
    Guid? ApprovedBy = null,
    /// <summary>WS-A2 (spec §7 Q2): when an approval was granted.
    /// Cleared on <see cref="Assignment.Reject"/>.</summary>
    DateTimeOffset? ApprovedAt = null,
    /// <summary>WS-A3 (spec §3.3): pass/fail score threshold.
    /// Null = no pass/fail signal.</summary>
    decimal? PassScore = null,
    /// <summary>WS-A3 (spec §7 Q4): max submission attempts.
    /// Null = unlimited.</summary>
    int? MaxAttempts = null,
    /// <summary>WS-C1 / spec §7 Q1: whether a guardian signature is
    /// required after completion. Projected so the summary list
    /// reports the create-time snapshot.</summary>
    bool RequiresSignature = false,
    /// <summary>WS-B2 (spec §3.4 line 70): requested per-difficulty counts.
    /// Projected so summaries never silently reset the AI difficulty mix.</summary>
    int? DifficultyEasyCount = null,
    int? DifficultyMediumCount = null,
    int? DifficultyHardCount = null,
    /// <summary>WS-E2b / ar-17: the UTC moment the assignment was most recently published;
    /// null when it has never been published (re-stamped by a later publish). Projected so
    /// the failure-surfacing gate can ask "has this ever been published" rather than testing
    /// the current status — <c>Unpublish</c> returns an assignment to <c>Draft</c> while its
    /// <c>NotificationLog</c> rows survive.</summary>
    DateTimeOffset? PublishedAt = null,
    /// <summary>INS-1/INS-2 (assignment-authoring-compartments §9): student-facing task
    /// text, distinct from <see cref="Description"/>.</summary>
    string? Instructions = null);