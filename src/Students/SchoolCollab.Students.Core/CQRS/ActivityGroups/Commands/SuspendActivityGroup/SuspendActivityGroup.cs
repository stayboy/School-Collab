using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.SuspendActivityGroup;

public sealed record SuspendActivityGroup(Guid Id) : ICommand;
