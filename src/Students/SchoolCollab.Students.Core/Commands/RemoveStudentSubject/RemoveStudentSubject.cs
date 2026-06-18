using SchoolCollab.Students.Core.CQRS;

namespace SchoolCollab.Students.Core.Commands.RemoveStudentSubject;

public sealed record RemoveStudentSubject(Guid Id) : ICommand;