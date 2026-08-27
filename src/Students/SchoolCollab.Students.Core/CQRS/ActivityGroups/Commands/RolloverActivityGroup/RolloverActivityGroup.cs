using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.RolloverActivityGroup;

public sealed record RolloverActivityGroup(
    Guid ActivityGroupId,
    DateOnly? TriggerDate = null) : ICommand;