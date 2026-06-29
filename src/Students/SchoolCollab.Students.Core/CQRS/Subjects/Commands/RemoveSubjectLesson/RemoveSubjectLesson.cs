using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Subjects.Commands.RemoveSubjectLesson;

public sealed record RemoveSubjectLesson(Guid Id) : ICommand;
