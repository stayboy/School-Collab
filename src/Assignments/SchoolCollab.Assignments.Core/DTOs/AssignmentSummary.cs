using SchoolCollab.Assignments.Core.Domain;

namespace SchoolCollab.Assignments.Core.Data.Repositories;

public record AssignmentSummary(
    Guid Id,
    string Title,
    string? Description,
    AssignmentType AssignmentType,
    GradingFormat GradingFormat,
    TargetAudienceType TargetAudienceType,
    Guid SubjectId,
    Guid? GradeLevelId,
    AssignmentStatus Status,
    DateTimeOffset? DueDate,
    decimal? MaxScore,
    Guid CreatedByTeacherId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);