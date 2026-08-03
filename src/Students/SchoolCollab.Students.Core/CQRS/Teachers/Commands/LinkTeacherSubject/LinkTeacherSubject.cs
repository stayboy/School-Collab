using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Commands.LinkTeacherSubject;

public sealed record LinkTeacherSubject(Guid TeacherId, Guid TopicId) : ICommand;
