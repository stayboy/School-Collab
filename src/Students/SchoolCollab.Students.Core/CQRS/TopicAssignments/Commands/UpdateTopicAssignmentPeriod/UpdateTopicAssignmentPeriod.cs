using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.TopicAssignments.Commands.UpdateTopicAssignmentPeriod;

/// <summary>
/// Changes the period scope of an existing topic assignment (grade or
/// activity-group subtype) — Rev. 6 FR-55/56/57. Null reverts to the
/// year-spanning (grade) / date-based-window (group) delivery. Mirrors
/// <see cref="UpdateTopicAssignmentTags"/>.
/// </summary>
public sealed record UpdateTopicAssignmentPeriod(
    Guid AssignmentId,
    Guid? PeriodId) : ICommand;
