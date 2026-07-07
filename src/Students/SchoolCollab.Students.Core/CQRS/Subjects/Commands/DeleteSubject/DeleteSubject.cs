using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Subjects.Commands.DeleteSubject;

public sealed record DeleteSubject(Guid Id) : ICommand;