using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.Teachers.Commands.UnlinkTeacherTopic;

public sealed record UnlinkTeacherTopic(Guid TeacherId, Guid TopicId) : ICommand;
