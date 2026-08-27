using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.ActivateActivityGroup;

public sealed record ActivateActivityGroup(Guid Id) : ICommand;