namespace SchoolCollab.Students.Core.DTOs;

public sealed record GradeSubjectAssignmentDto(
    Guid Id,
    Guid GradeLevelId,
    Guid SubjectId,
    Guid PeriodId,
    Guid? SubjectStrandId,
    Guid? SubjectLessonId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);