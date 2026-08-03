using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.GradeSubjectAssignments.Commands.AssignGradeSubject;

public sealed record AssignGradeSubject(
    Guid? GradeLevelId,
    Guid? ActivityGroupId,
    Guid TopicId,
    DateOnly StartDate,
    DateOnly? EndDate = null,
    Guid? TopicStrandId = null,
    Guid? TopicLessonId = null) : ICommand;