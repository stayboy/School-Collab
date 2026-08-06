using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Commands.LinkTeacherTopic;

public sealed record LinkTeacherTopic(Guid TeacherId, Guid TopicId, Guid? RoleCodedValueId = null) : ICommand;
