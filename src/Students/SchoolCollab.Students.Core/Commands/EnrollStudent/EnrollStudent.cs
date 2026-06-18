using SchoolCollab.Students.Core.CQRS;

namespace SchoolCollab.Students.Core.Commands.EnrollStudent;

public sealed record EnrollStudent(
    Guid StudentId,
    Guid PeriodId,
    Guid GradeLevelId,
    DateOnly? EnrolledOn) : ICommand;