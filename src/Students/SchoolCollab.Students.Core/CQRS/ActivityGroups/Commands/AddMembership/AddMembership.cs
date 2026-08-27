using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.AddMembership;

public sealed record AddMembership(
    Guid ActivityGroupId,
    Guid StudentId,
    Guid? PeriodId = null,
    bool? AutoRenew = null,
    DateOnly? WindowStartDate = null,
    DateOnly? WindowEndDate = null,
    DateOnly? JoinedOn = null) : ICommand;