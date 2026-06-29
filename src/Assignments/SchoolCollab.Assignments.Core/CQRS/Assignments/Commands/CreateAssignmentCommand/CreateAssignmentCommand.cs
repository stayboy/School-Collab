using SchoolCollab.Core.CQRS;

using SchoolCollab.Assignments.Core.Domain;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.CreateAssignmentCommand;

public sealed record CreateAssignmentCommand(
    string Title,
    string? Description,
    AssignmentType AssignmentType,
    GradingFormat GradingFormat,
    TargetAudienceType TargetAudienceType,
    Guid SubjectCodedValueId,
    Guid? GradeCodedValueId,
    DateTimeOffset? DueDate,
    decimal? MaxScore) : ICommand;
