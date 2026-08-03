using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.DeleteActivityGroup;

public sealed record DeleteActivityGroup(Guid Id) : ICommand;
