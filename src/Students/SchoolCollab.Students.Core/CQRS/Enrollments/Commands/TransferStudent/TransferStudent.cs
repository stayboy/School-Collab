using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Enrollments.Commands.TransferStudent;

public sealed record TransferStudent(
    Guid EnrollmentId,
    Guid NewGradeLevelId,
    DateOnly? TransferDate) : ICommand;