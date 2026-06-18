using SchoolCollab.Students.Core.CQRS;

namespace SchoolCollab.Students.Core.Commands.DeleteStudent;

public sealed record DeleteStudent(Guid Id) : ICommand;