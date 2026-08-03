using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.RemoveMembership;

public sealed record RemoveMembership(
    Guid ActivityGroupId,
    Guid StudentId) : ICommand;
