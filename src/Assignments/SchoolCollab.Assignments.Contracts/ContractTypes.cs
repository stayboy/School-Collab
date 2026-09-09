using System.ComponentModel;

namespace SchoolCollab.Assignments.Contracts;

public enum AssignmentStatusDto
{
    Draft = 0,
    Published = 1,
    Closed = 2,
    /// <summary>Mirror of <c>SchoolCollab.Assignments.Core.Domain.AssignmentStatus.Scheduled</c>
    /// (spec §3.5 / WS-A2). Serializes as the string "Scheduled".</summary>
    Scheduled = 3,
    /// <summary>Mirror of <c>SchoolCollab.Assignments.Core.Domain.AssignmentStatus.Archived</c>
    /// (spec §7 Q6 — read-only retention). Serializes as the string "Archived".</summary>
    Archived = 4
}

/// <summary>Mirror of <c>SchoolCollab.Assignments.Core.Domain.ApprovalStatus</c>
/// (spec §7 Q2). The wire surface is <c>null</c> when the assignment has
/// not been submitted for approval yet. Serializes as the string
/// name (Pending / Approved / Rejected).</summary>
public enum ApprovalStatusDto
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

public enum AssignmentTypeDto
{
    [Description("Online")]
    Digital = 0,
    [Description("Hybrid")]
    SemiManual = 1,
    [Description("Offline")]
    Manual = 2
}

public enum GradingFormatDto
{
    [Description("Teacher Marked")]
    TeacherGraded = 0,
    [Description("Auto Scored")]
    AutoGraded = 1,
    [Description("Instant Feedback")]
    InstantGraded = 2
}

public enum TargetAudienceTypeDto
{
    [Description("Everyone")]
    AllStudents = 0,
    [Description("By Grade Level")]
    SelectedGrades = 1,
    [Description("By Group")]
    SelectedGroups = 2
}

/// <summary>Mirrors <c>SchoolCollab.Assignments.Core.Domain.QuestionType</c>
/// (AI spec §3.2 / decision 10). TrueFalse is stored as MC with two canonical
/// options — the discriminator is retained for client-side handling.</summary>
public enum QuestionTypeDto
{
    MultipleChoice = 0,
    TrueFalse = 1,
    ShortAnswer = 2
}

/// <summary>Mirrors <c>SchoolCollab.Assignments.Core.Domain.ModuleType</c>
/// (WS-A1 / spec §4.10). Student-facing content module kind on an
/// assignment — video or guide.</summary>
public enum ModuleTypeDto
{
    [Description("Video")] Video = 0,
    [Description("Guide")] Guide = 1
}

/// <summary>Mirrors <c>SchoolCollab.Assignments.Core.Domain.ResourceKind</c>
/// (WS-A1 / spec §3.2 / FR-211). AI-generation input kind — link, file,
/// or video.</summary>
public enum ResourceKindDto
{
    [Description("Link")] Url = 0,
    [Description("File")] File = 1,
    [Description("Video")] Video = 2
}

public record AssignmentSummaryDto(
    Guid Id,
    string Title,
    string? Description,
    AssignmentTypeDto AssignmentType,
    GradingFormatDto GradingFormat,
    TargetAudienceTypeDto TargetAudienceType,
    Guid TopicId,
    string? TopicName,
    Guid? GradeLevelId,
    string? GradeName,
    AssignmentStatusDto Status,
    DateTimeOffset? DueDate,
    decimal? MaxScore,
    bool MandatoryReview,
    Guid CreatedByTeacherId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    /// <summary>When the assignment is scheduled to auto-publish
    /// (spec §3.5 step 2). Null in any other state.</summary>
    DateTimeOffset? AvailableFromUtc = null,
    /// <summary>Days the archive sweep adds to <c>DueDate</c> before
    /// archiving the assignment (spec §7 Q6).</summary>
    int ArchiveGraceDays = 30,
    /// <summary>The approval status when the
    /// <c>FEATURE:RequireAssignmentApproval</c> flag is on
    /// (spec §7 Q2). Null when never submitted.</summary>
    ApprovalStatusDto? ApprovalStatus = null,
    /// <summary>The user who approved the assignment. Null until
    /// <see cref="ApprovalStatus"/> is <c>Approved</c>.</summary>
    Guid? ApprovedBy = null,
    /// <summary>The UTC moment an approval was granted.</summary>
    DateTimeOffset? ApprovedAt = null,
    /// <summary>WS-A3 (spec §3.3): pass/fail score threshold.
    /// Null = no pass/fail signal.</summary>
    decimal? PassScore = null,
    /// <summary>WS-A3 (spec §7 Q4): max submission attempts.
    /// Null = unlimited.</summary>
    int? MaxAttempts = null);

public record CreateAssignmentRequest(
    string Title,
    string? Description,
    AssignmentTypeDto AssignmentType,
    GradingFormatDto GradingFormat = GradingFormatDto.TeacherGraded,
    TargetAudienceTypeDto TargetAudienceType = TargetAudienceTypeDto.AllStudents,
    Guid TopicId = default,
    Guid? GradeLevelId = null,
    DateTimeOffset? DueDate = null,
    decimal? MaxScore = null,
    bool MandatoryReview = true,
    string? AiPromptOverride = null,
    IReadOnlyList<NewQuestionDto>? Questions = null,
    IReadOnlyList<NewAttachmentDto>? Attachments = null,
    IReadOnlyList<NewContentModuleDto>? ContentModules = null,
    IReadOnlyList<NewResourceDto>? Resources = null,
    /// <summary>WS-A2 / spec §7 Q6: days added to <c>DueDate</c>
    /// before the archive sweep transitions the row to
    /// <see cref="AssignmentStatusDto.Archived"/>. Defaults to 30.</summary>
    int ArchiveGraceDays = 30,
    /// <summary>WS-A3 (spec §3.3): pass/fail score threshold.
    /// Null = no pass/fail signal.</summary>
    decimal? PassScore = null,
    /// <summary>WS-A3 (spec §7 Q4): max submission attempts.
    /// Null = unlimited.</summary>
    int? MaxAttempts = null);

public record UpdateAssignmentRequest(
    string Title,
    string? Description,
    AssignmentTypeDto AssignmentType,
    GradingFormatDto GradingFormat = GradingFormatDto.TeacherGraded,
    TargetAudienceTypeDto TargetAudienceType = TargetAudienceTypeDto.AllStudents,
    Guid TopicId = default,
    Guid? GradeLevelId = null,
    DateTimeOffset? DueDate = null,
    decimal? MaxScore = null,
    bool MandatoryReview = true,
    string? AiPromptOverride = null,
    IReadOnlyList<NewQuestionDto>? Questions = null,
    IReadOnlyList<NewAttachmentDto>? Attachments = null,
    IReadOnlyList<NewContentModuleDto>? ContentModules = null,
    IReadOnlyList<NewResourceDto>? Resources = null,
    /// <summary>WS-A2 / spec §7 Q6: days added to <c>DueDate</c>
    /// before the archive sweep transitions the row to
    /// <see cref="AssignmentStatusDto.Archived"/>. Defaults to 30.</summary>
    int ArchiveGraceDays = 30,
    /// <summary>WS-A3 (spec §3.3): pass/fail score threshold.
    /// Null = no pass/fail signal.</summary>
    decimal? PassScore = null,
    /// <summary>WS-A3 (spec §7 Q4): max submission attempts.
    /// Null = unlimited.</summary>
    int? MaxAttempts = null);

/// <summary>Schedule an assignment to auto-publish at a future
/// moment (spec §3.5 step 2). The sweep dispatches the existing
/// publish command when <see cref="AvailableFromUtc"/> arrives.</summary>
public record ScheduleAssignmentRequest(DateTimeOffset AvailableFromUtc);

/// <summary>Approve a pending assignment (spec §7 Q2). The
/// <see cref="ApproverId"/> is a placeholder until identity wiring
/// lands (the <c>ReviewAssignmentRequest.TeacherId</c> posture).</summary>
public record ApproveAssignmentRequest(Guid ApproverId);

/// <summary>Reject a pending assignment (spec §7 Q2). See
/// <see cref="ApproveAssignmentRequest"/> for the identity posture.</summary>
public record RejectAssignmentRequest(Guid ApproverId);

/// <summary>An inbound question option on the create/update request (AI spec §3.2).</summary>
public record NewQuestionOptionDto(string OptionText, bool IsCorrect);

/// <summary>An inbound question on the create/update request (AI spec §3.2).
/// <see cref="Options"/> is required for <see cref="QuestionTypeDto.MultipleChoice"/>
/// and <see cref="QuestionTypeDto.TrueFalse"/>; null/empty for
/// <see cref="QuestionTypeDto.ShortAnswer"/>.</summary>
public record NewQuestionDto(
    string QuestionText,
    QuestionTypeDto QuestionType,
    int DisplayOrder,
    IReadOnlyList<NewQuestionOptionDto>? Options,
    string? ModelAnswer = null);

/// <summary>An inbound attachment metadata record on the create/update request
/// (AI spec §3.2). <see cref="StoragePath"/> is opaque to the UI — the file is
/// already staged to storage before submit (EC-4).</summary>
public record NewAttachmentDto(
    string FileName,
    string ContentType,
    long FileSize,
    string StoragePath);

/// <summary>An inbound content module on the create/update request
/// (WS-A1 / spec §4.10 / FR-210–212). The wizard's Step-2 Resources UI
/// section projects to this DTO and the create payload carries it
/// alongside the attachments. <see cref="Url"/> is required for both
/// <see cref="ModuleTypeDto.Video"/> and <see cref="ModuleTypeDto.Guide"/>
/// (external embed URL or staged file URL returned by
/// <c>POST /assignments/attachments/stage</c>).</summary>
public record NewContentModuleDto(
    ModuleTypeDto ModuleType,
    string? Title,
    string Url,
    string? StoragePath,
    int DisplayOrder,
    int MinCompletionThresholdPercent = 100,
    bool IsRequired = false);

/// <summary>A persisted content module row (WS-A1). Read-back surfaces
/// land in later rounds; declared here so the contract shape is
/// discoverable.</summary>
public record ContentModuleDto(
    Guid Id,
    Guid AssignmentId,
    ModuleTypeDto ModuleType,
    string? Title,
    string Url,
    string? StoragePath,
    int DisplayOrder,
    int MinCompletionThresholdPercent,
    bool IsRequired);

/// <summary>An inbound AI-generation input on the create/update request
/// (WS-A1 / spec §3.2 / FR-211). The kind matrix is validator-owned —
/// each kind has a different required-field shape (Url OR StoragePath,
/// or exactly one of the two for Video).</summary>
public record NewResourceDto(
    ResourceKindDto ResourceKind,
    string? Url,
    string? StoragePath,
    string? DisplayName,
    bool IncludedInGeneration = true);

/// <summary>A persisted resource row (WS-A1). Read-back surfaces land in
/// later rounds.</summary>
public record ResourceDto(
    Guid Id,
    Guid AssignmentId,
    ResourceKindDto ResourceKind,
    string? Url,
    string? StoragePath,
    string? DisplayName,
    bool IncludedInGeneration);

/// <summary>Result of <c>POST /assignments/attachments/stage</c> (WS-A1 /
/// FR-210 / EC-4). The wizard stages one file at selection time and
/// rides <see cref="StoragePath"/> on the create payload.</summary>
public record StagedAttachmentDto(
    string FileName,
    string ContentType,
    long FileSize,
    string StoragePath);

/// <summary>Publish an assignment (spec §8). Optional contact selection:
/// when <see cref="ContactIds"/> is non-empty, only those subscribed contacts
/// receive the broadcast; null/empty = all subscribed contacts.</summary>
public record PublishAssignmentRequest(IReadOnlyList<Guid>? ContactIds);

public record ReviewAssignmentRequest(
    Guid TeacherId,
    decimal? Score,
    string? Comments);

public enum ReviewStateDto
{
    Pending = 0,
    Reviewed = 1,
    Graded = 2
}

/// <summary>Teacher review queue item (spec §4.13).</summary>
public record SubmissionForReviewDto(
    Guid SubmissionId,
    Guid AssignmentId,
    string AssignmentTitle,
    Guid StudentId,
    int CurrentVersionNumber,
    ReviewStateDto ReviewState,
    DateTimeOffset LastSubmittedAt);

/// <summary>Guardian portal view of a submission gate (spec §4.10).</summary>
public record GuardianGateDto(
    Guid GateId,
    Guid AssignmentId,
    Guid StudentId,
    bool SubmissionEnabledForStudent,
    DateTimeOffset? ReviewedAt,
    Guid? ReviewedByGuardianId,
    string? ReviewComment,
    Guid? SubmittedByGuardianId,
    DateTimeOffset? SubmittedByGuardianAt);

public record ReviewSubmissionGateRequest(
    Guid ReviewerGuardianId,
    bool Approve,
    string? Comment);

public record SubmitAssignmentOnBehalfRequest(
    Guid GuardianId,
    string? Content,
    /// <summary>WS-A3 (spec §3.3): structured per-question answers
    /// carried with the submission. Null/empty is valid (free-text
    /// only).</summary>
    IReadOnlyList<SubmissionAnswerDto>? Answers = null);

public record ReviewSubmissionRequest(
    Guid TeacherId,
    decimal? Score,
    string? Grade,
    string? Comments);

/// <summary>Student self-submit (spec §4.11).</summary>
public record CreateStudentSubmissionRequest(
    string? Content,
    /// <summary>WS-A3 (spec §3.3): structured per-question answers
    /// carried with the submission. Null/empty is valid (free-text
    /// only).</summary>
    IReadOnlyList<SubmissionAnswerDto>? Answers = null);

/// <summary>WS-A3 — one structured answer to one question on a
/// submission. MC/TF carries <see cref="SelectedOptionId"/>;
/// ShortAnswer carries <see cref="TextAnswer"/>. Exactly one of the
/// two is expected; the
/// <c>SubmissionAnswerValidator</c> runs cross-checks against the
/// referenced question.</summary>
public record SubmissionAnswerDto(
    Guid QuestionId,
    Guid? SelectedOptionId,
    string? TextAnswer);

/// <summary>WS-A3 (spec §3.3) — per-question correctness verdict in the
/// <see cref="SubmissionFeedbackDto"/>. Null means the question was
/// not auto-scorable (no model answer / no correct option); true/false
/// is the engine's evaluation.</summary>
public record SubmissionQuestionResultDto(
    Guid QuestionId,
    bool? IsCorrect);

/// <summary>WS-A3 (spec §3.3) — the InstantGraded feedback envelope
/// returned by the student-submit route. Per-question correctness plus
/// the total score / pass flag. AutoGraded and TeacherGraded routes do
/// NOT return this envelope (the score persists on the version but the
/// per-question verdict is held until teacher review).</summary>
public record SubmissionFeedbackDto(
    IReadOnlyList<SubmissionQuestionResultDto> Questions,
    decimal Score,
    bool? Passed);

/// <summary>WS-A3 (spec §7 Q4) — request body for the
/// <c>POST /assignments/{id}/students/{studentId}/override-attempts</c>
/// teacher override route. <see cref="TeacherId"/> is the identity
/// placeholder until identity wiring lands (D-6, the
/// <c>ReviewAssignmentRequest.TeacherId</c> posture).</summary>
public record OverrideStudentSubmissionAttemptsRequest(Guid TeacherId);

// ── Phase 7: publish recipients + submission detail (spec §8/§12) ────────────

/// <summary>Who owns a publish contact (mirrors Students.Core ContactOwnerType).</summary>
public enum ContactOwnerTypeDto
{
    [Description("Student")] Student = 0,
    [Description("Guardian")] Guardian = 1
}

/// <summary>Contact channel (mirrors Students.Core ContactChannel).</summary>
public enum ContactChannelDto
{
    [Description("Email")] Email = 0,
    [Description("SMS")] SMS = 1,
    [Description("WhatsApp")] WhatsApp = 2
}

/// <summary>Guardian role relative to a student (mirrors Students.Core GuardianRole).</summary>
public enum GuardianRoleDto
{
    [Description("Primary")] Primary = 0,
    [Description("CC")] CC = 1
}

/// <summary>Submission source (mirrors Assignments.Core SubmissionSource).</summary>
public enum SubmissionSourceDto
{
    [Description("Student")] Student = 0,
    [Description("Guardian")] GuardianOnBehalf = 1
}

/// <summary>Per-(assignment, contact) publish recipient (spec §4.6).</summary>
public record AssignmentRecipientDto(
    Guid Id,
    Guid AssignmentId,
    ContactOwnerTypeDto OwnerType,
    Guid OwnerId,
    Guid? WardStudentId,
    Guid ContactId,
    ContactChannelDto Channel,
    GuardianRoleDto? Role,
    bool NotifyOnBroadcast,
    bool SubscriptionActive);

/// <summary>A single submission version (spec §4.11).</summary>
public record SubmissionVersionDto(
    Guid Id,
    int VersionNumber,
    SubmissionSourceDto Source,
    string? Content,
    Guid? SubmittedByGuardianId,
    DateTimeOffset SubmittedAt,
    /// <summary>WS-A3 (spec §3.3): auto-scored total for this version.
    /// Null for TeacherGraded submissions or when no scoring ran.</summary>
    decimal? Score = null,
    /// <summary>WS-A3 (spec §3.3): whether this version scored at or
    /// above the assignment's <c>PassScore</c>. Null when
    /// <c>PassScore</c> is absent or scoring did not run.</summary>
    bool? Passed = null);

/// <summary>Teacher review/grade attached to a submission (spec §4.13).</summary>
public record SubmissionReviewDto(
    Guid Id,
    Guid SubmissionId,
    Guid TeacherId,
    decimal? Score,
    string? Grade,
    string? Comments,
    DateTimeOffset CreatedAt);

/// <summary>A submission with its version history + review (spec §4.11/§4.13).</summary>
public record SubmissionDetailDto(
    Guid SubmissionId,
    Guid AssignmentId,
    Guid StudentId,
    int CurrentVersionNumber,
    ReviewStateDto ReviewState,
    DateTimeOffset LastSubmittedAt,
    SubmissionVersionDto[] Versions,
    SubmissionReviewDto? Review);

public record EnableStudentSubmissionRequest(Guid? ReviewerGuardianId);