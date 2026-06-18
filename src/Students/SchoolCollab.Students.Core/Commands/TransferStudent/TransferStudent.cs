using SchoolCollab.Students.Core.CQRS;

namespace SchoolCollab.Students.Core.Commands.TransferStudent;

public sealed record TransferStudent(
    Guid EnrollmentId,
    Guid NewGradeLevelId,
    DateOnly? TransferDate) : ICommand;