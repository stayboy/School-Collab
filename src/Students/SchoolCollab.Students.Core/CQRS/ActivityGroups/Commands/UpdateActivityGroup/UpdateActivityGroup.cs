using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.UpdateActivityGroup;

public sealed record UpdateActivityGroup(
    Guid Id,
    string Name,
    string? Description = null,
    string? Category = null,
    Guid? PeriodId = null,
    int? Capacity = null) : ICommand;
