using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Commands.UnlinkTeacherGradeLevel;

public sealed record UnlinkTeacherGradeLevel(Guid TeacherId, Guid GradeLevelId) : ICommand;
