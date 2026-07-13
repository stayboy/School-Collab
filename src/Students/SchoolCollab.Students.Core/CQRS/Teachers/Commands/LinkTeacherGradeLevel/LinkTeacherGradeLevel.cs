using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Commands.LinkTeacherGradeLevel;

public sealed record LinkTeacherGradeLevel(Guid TeacherId, Guid GradeLevelId) : ICommand;
