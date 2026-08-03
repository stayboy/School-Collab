using System.ComponentModel;

namespace SchoolCollab.Assignments.Contracts;

public enum AssignmentStatusDto
{
    Draft = 0,
    Published = 1,
    Closed = 2
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
    DateTimeOffset UpdatedAt);

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
    bool MandatoryReview = true);

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
    bool MandatoryReview = true);

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
    string? Content);

public record ReviewSubmissionRequest(
    Guid TeacherId,
    decimal? Score,
    string? Grade,
    string? Comments);

/// <summary>Student self-submit (spec §4.11).</summary>
public record CreateStudentSubmissionRequest(string? Content);

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
    DateTimeOffset SubmittedAt);

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