using SchoolCollab.Assignments.Core.Domain;

namespace SchoolCollab.Assignments.Core.CQRS;

public sealed record CreateAssignmentCommand(
    string Title,
    string? Description,
    AssignmentType AssignmentType,
    Guid SubjectCodedValueId,
    Guid? GradeCodedValueId,
    DateTimeOffset? DueDate,
    decimal? MaxScore) : ICommand;

public sealed record UpdateAssignmentCommand(
    Guid Id,
    string Title,
    string? Description,
    AssignmentType AssignmentType,
    Guid SubjectCodedValueId,
    Guid? GradeCodedValueId,
    DateTimeOffset? DueDate,
    decimal? MaxScore) : ICommand;

public sealed record DeleteAssignmentCommand(Guid Id) : ICommand;

public sealed record PublishAssignmentCommand(Guid Id) : ICommand;

public sealed record UnpublishAssignmentCommand(Guid Id) : ICommand;

public sealed record CloseAssignmentCommand(Guid Id) : ICommand;

public sealed record ReviewAssignmentCommand(
    Guid Id,
    Guid TeacherId,
    decimal? Score,
    string? Comments) : ICommand;