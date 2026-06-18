using SchoolCollab.Students.Core.CQRS;

namespace SchoolCollab.Students.Core.Commands.RemoveGradeSubject;

public sealed record RemoveGradeSubject(Guid Id) : ICommand;