using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.TopicAssignments.Commands.AssignGradeTopic;

public sealed record AssignGradeTopic(
    Guid GradeLevelId,
    Guid TopicId,
    DateOnly StartDate,
    DateOnly? EndDate = null,
    Guid? TopicStrandId = null,
    Guid? PeriodId = null) : ICommand;
