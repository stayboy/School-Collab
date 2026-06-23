using SchoolCollab.Students.Core.CQRS;

namespace SchoolCollab.Students.Core.Commands.AssignGradeSubject;

public sealed record AssignGradeSubject(
    Guid GradeLevelId,
    Guid SubjectId,
    Guid PeriodId,
    Guid? SubjectStrandId = null,
    Guid? SubjectLessonId = null) : ICommand;