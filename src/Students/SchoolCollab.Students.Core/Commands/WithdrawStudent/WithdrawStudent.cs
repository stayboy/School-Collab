using SchoolCollab.Students.Core.CQRS;

namespace SchoolCollab.Students.Core.Commands.WithdrawStudent;

public sealed record WithdrawStudent(
    Guid EnrollmentId,
    DateOnly? ExitDate) : ICommand;