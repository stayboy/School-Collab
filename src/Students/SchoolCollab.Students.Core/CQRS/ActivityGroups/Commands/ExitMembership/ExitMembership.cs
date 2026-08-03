using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.ExitMembership;

public sealed record ExitMembership(
    Guid ActivityGroupId,
    Guid StudentId) : ICommand;
