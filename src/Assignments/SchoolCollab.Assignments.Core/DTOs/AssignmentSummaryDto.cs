namespace SchoolCollab.Assignments.Core.DTOs;

public sealed record AssignmentSummaryDto(
    Guid Id,
    string Title,
    string? Description,
    string AssignmentType,
    Guid SubjectCodedValueId,
    Guid? GradeCodedValueId,
    string Status,
    DateTimeOffset? DueDate,
    decimal? MaxScore,
    Guid CreatedByTeacherId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);