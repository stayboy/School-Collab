using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.SetMembershipAutoRenew;

public sealed record SetMembershipAutoRenew(
    Guid MembershipId,
    bool AutoRenew) : ICommand;