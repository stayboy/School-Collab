using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Subjects.Commands.AssignLessonStrand;

public sealed record AssignLessonStrand(
    Guid LessonId,
    Guid? StrandId) : ICommand;
