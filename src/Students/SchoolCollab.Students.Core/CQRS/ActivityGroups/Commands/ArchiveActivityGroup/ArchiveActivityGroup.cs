using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.ArchiveActivityGroup;

public sealed record ArchiveActivityGroup(Guid Id) : ICommand;
