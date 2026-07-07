using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.GradeLevels.Commands.DeleteGradeLevel;

public sealed record DeleteGradeLevel(Guid Id) : ICommand;