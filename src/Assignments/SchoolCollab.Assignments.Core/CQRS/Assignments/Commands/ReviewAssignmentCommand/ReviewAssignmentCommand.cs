using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.ReviewAssignmentCommand;

public sealed record ReviewAssignmentCommand(
    Guid Id,
    Guid TeacherId,
    decimal? Score,
    string? Comments) : ICommand;
