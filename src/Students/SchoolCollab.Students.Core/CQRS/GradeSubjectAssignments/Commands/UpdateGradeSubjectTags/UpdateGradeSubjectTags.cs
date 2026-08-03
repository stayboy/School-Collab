using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.GradeSubjectAssignments.Commands.UpdateGradeSubjectTags;

public sealed record UpdateGradeSubjectTags(
    Guid AssignmentId,
    Guid? TopicStrandId,
    Guid? TopicLessonId) : ICommand;
