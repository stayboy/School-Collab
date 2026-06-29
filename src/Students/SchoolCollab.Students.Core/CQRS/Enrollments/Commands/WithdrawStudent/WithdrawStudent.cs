using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Enrollments.Commands.WithdrawStudent;

public sealed record WithdrawStudent(
    Guid EnrollmentId,
    DateOnly? ExitDate) : ICommand;