using SchoolCollab.Assignments.Core.Domain;

namespace SchoolCollab.Assignments.Core.Data.Repositories;

public record AssignmentSummary(
    Guid Id,
    string Title,
    string? Description,
    AssignmentType AssignmentType,
    Guid SubjectCodedValueId,
    Guid? GradeCodedValueId,
    AssignmentStatus Status,
    DateTimeOffset? DueDate,
    decimal? MaxScore,
    Guid CreatedByTeacherId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);