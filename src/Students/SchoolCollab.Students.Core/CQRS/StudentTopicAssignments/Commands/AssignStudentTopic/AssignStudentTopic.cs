using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.CQRS.StudentTopicAssignments.Commands.AssignStudentTopic;

public sealed record AssignStudentTopic(
    Guid StudentId,
    Guid TopicId,
    Guid PeriodId,
    bool IsOverride,
    SubjectAssignmentSource SourceType) : ICommand;