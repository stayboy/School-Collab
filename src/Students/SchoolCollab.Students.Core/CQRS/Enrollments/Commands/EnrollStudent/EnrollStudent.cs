using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Enrollments.Commands.EnrollStudent;

public sealed record EnrollStudent(
    Guid StudentId,
    Guid PeriodId,
    Guid GradeLevelId,
    DateOnly? EnrolledOn) : ICommand;