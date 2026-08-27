using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.DeactivateActivityGroup;

public sealed record DeactivateActivityGroup(Guid Id) : ICommand;