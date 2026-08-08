using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Enrollments.Commands.EnrollStudent;

public sealed record EnrollStudent(
    Guid StudentId,
    Guid PeriodId,
    Guid GradeLevelId,
    Guid? StreamCodedValueId,
    DateOnly? EnrolledOn) : ICommand;