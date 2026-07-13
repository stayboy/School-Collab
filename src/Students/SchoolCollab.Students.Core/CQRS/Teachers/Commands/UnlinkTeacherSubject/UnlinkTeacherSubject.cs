using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Commands.UnlinkTeacherSubject;

public sealed record UnlinkTeacherSubject(Guid TeacherId, Guid SubjectId) : ICommand;
