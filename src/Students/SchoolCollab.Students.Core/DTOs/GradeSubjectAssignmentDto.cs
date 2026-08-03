namespace SchoolCollab.Students.Core.DTOs;

public sealed record GradeSubjectAssignmentDto(
    Guid Id,
    Guid? GradeLevelId,
    Guid? ActivityGroupId,
    Guid TopicId,
    DateOnly StartDate,
    DateOnly? EndDate,
    Guid? TopicStrandId,
    Guid? TopicLessonId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);