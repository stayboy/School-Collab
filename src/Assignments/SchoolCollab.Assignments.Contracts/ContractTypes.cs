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
    Guid SubjectCodedValueId,
    string? SubjectName,
    Guid? GradeCodedValueId,
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
    Guid SubjectCodedValueId = default,
    Guid? GradeCodedValueId = null,
    DateTimeOffset? DueDate = null,
    decimal? MaxScore = null);

public record UpdateAssignmentRequest(
    string Title,
    string? Description,
    AssignmentTypeDto AssignmentType,
    GradingFormatDto GradingFormat = GradingFormatDto.TeacherGraded,
    TargetAudienceTypeDto TargetAudienceType = TargetAudienceTypeDto.AllStudents,
    Guid SubjectCodedValueId = default,
    Guid? GradeCodedValueId = null,
    DateTimeOffset? DueDate = null,
    decimal? MaxScore = null);

public record ReviewAssignmentRequest(
    Guid TeacherId,
    decimal? Score,
    string? Comments);