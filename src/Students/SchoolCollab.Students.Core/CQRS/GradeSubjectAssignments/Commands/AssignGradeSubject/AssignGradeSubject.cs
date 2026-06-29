using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.GradeSubjectAssignments.Commands.AssignGradeSubject;

public sealed record AssignGradeSubject(
    Guid GradeLevelId,
    Guid SubjectId,
    Guid PeriodId,
    Guid? SubjectStrandId = null,
    Guid? SubjectLessonId = null) : ICommand;