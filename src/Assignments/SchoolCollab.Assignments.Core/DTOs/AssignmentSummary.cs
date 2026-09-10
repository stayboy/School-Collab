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
    Guid? GradeLevelId,
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
    int? MaxAttempts = null);