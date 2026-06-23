using SchoolCollab.Students.Core.CQRS;

namespace SchoolCollab.Students.Core.Commands.RemoveSubjectLesson;

public sealed record RemoveSubjectLesson(Guid Id) : ICommand;
