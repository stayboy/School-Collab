using SchoolCollab.Students.Core.CQRS;

namespace SchoolCollab.Students.Core.Commands.AssignLessonStrand;

public sealed record AssignLessonStrand(
    Guid LessonId,
    Guid? StrandId) : ICommand;
