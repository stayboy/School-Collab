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
    Guid SubjectId,
    string? SubjectName,
    Guid? GradeLevelId,
    string? GradeName,
    AssignmentStatusDto Status,
    DateTimeOffset? DueDate,
    decimal? MaxScore,
    Guid CreatedByTeacherId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record CreateAssignmentRequest(
    string Title,
    string? Description,
    AssignmentTypeDto AssignmentType,
    GradingFormatDto GradingFormat = GradingFormatDto.TeacherGraded,
    TargetAudienceTypeDto TargetAudienceType = TargetAudienceTypeDto.AllStudents,
    Guid SubjectId = default,
    Guid? GradeLevelId = null,
    DateTimeOffset? DueDate = null,
    decimal? MaxScore = null);

public record UpdateAssignmentRequest(
    string Title,
    string? Description,
    AssignmentTypeDto AssignmentType,
    GradingFormatDto GradingFormat = GradingFormatDto.TeacherGraded,
    TargetAudienceTypeDto TargetAudienceType = TargetAudienceTypeDto.AllStudents,
    Guid SubjectId = default,
    Guid? GradeLevelId = null,
    DateTimeOffset? DueDate = null,
    decimal? MaxScore = null);

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
    Guid StudentId,
    Guid GuardianId,
    string? Content);

public record ReviewSubmissionRequest(
    Guid TeacherId,
    decimal? Score,
    string? Grade,
    string? Comments);

/// <summary>Student self-submit (spec §4.11).</summary>
public record CreateStudentSubmissionRequest(string? Content);