using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Students.Commands.DeleteStudent;

public sealed record DeleteStudent(Guid Id) : ICommand;