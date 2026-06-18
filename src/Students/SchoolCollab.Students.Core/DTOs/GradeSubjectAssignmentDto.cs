namespace SchoolCollab.Students.Core.DTOs;

public sealed record GradeSubjectAssignmentDto(
    Guid Id,
    Guid GradeLevelId,
    Guid SubjectId,
    Guid PeriodId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);