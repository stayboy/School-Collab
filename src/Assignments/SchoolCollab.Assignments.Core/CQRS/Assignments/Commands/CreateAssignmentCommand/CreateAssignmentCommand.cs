using SchoolCollab.Core.CQRS;

using SchoolCollab.Assignments.Core.Domain;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.CreateAssignmentCommand;

public sealed record CreateAssignmentCommand(
    string Title,
    string? Description,
    AssignmentType AssignmentType,
    GradingFormat GradingFormat,
    TargetAudienceType TargetAudienceType,
    Guid TopicId,
    Guid? GradeLevelId,
    DateTimeOffset? DueDate,
    decimal? MaxScore,
    bool MandatoryReview = true) : ICommand;
