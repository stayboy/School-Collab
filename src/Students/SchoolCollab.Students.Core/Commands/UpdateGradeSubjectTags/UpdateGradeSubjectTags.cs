using SchoolCollab.Students.Core.CQRS;

namespace SchoolCollab.Students.Core.Commands.UpdateGradeSubjectTags;

public sealed record UpdateGradeSubjectTags(
    Guid AssignmentId,
    Guid? SubjectStrandId,
    Guid? SubjectLessonId) : ICommand;
