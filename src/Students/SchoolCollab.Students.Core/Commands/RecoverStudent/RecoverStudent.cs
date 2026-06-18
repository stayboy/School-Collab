using SchoolCollab.Students.Core.CQRS;

namespace SchoolCollab.Students.Core.Commands.RecoverStudent;

public sealed record RecoverStudent(Guid Id) : ICommand;