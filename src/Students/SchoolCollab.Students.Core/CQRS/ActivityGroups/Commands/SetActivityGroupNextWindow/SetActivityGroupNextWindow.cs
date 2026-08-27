using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.SetActivityGroupNextWindow;

public sealed record SetActivityGroupNextWindow(
    Guid ActivityGroupId,
    DateOnly NextStartDate,
    DateOnly NextEndDate) : ICommand;