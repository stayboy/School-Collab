using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.AddMembership;

public sealed record AddMembership(
    Guid ActivityGroupId,
    Guid StudentId,
    DateOnly? JoinedOn = null) : ICommand;
