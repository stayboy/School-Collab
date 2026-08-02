using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.CreateActivityGroup;

public sealed record CreateActivityGroup(
    string Name,
    string? Description = null,
    string? Category = null,
    Guid? PeriodId = null,
    int? Capacity = null) : ICommand;
