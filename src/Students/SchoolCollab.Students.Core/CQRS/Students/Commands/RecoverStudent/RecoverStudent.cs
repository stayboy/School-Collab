using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Students.Commands.RecoverStudent;

public sealed record RecoverStudent(Guid Id) : ICommand;