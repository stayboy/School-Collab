namespace SchoolCollab.Students.Core.Domain.Events;

// --- Activity Group lifecycle (spec activity-group-enrollment.md FR-1..6) ---

public sealed record ActivityGroupCreatedEvent(Guid ActivityGroupId, string Name) : IDomainEvent;
public sealed record ActivityGroupUpdatedEvent(Guid ActivityGroupId, string Name) : IDomainEvent;
public sealed record ActivityGroupActivatedEvent(Guid ActivityGroupId, string Name) : IDomainEvent;
public sealed record ActivityGroupDeactivatedEvent(Guid ActivityGroupId, string Name) : IDomainEvent;
public sealed record ActivityGroupDeletedEvent(Guid ActivityGroupId, string Name) : IDomainEvent;

// Rev. 5 FR-53/50: next-window slot + rollover.
public sealed record ActivityGroupNextWindowSetEvent(
    Guid ActivityGroupId, string Name, DateOnly StartDate, DateOnly EndDate) : IDomainEvent;
public sealed record ActivityGroupRolledOverEvent(
    Guid ActivityGroupId, string Name, DateOnly NewStartDate, DateOnly NewEndDate) : IDomainEvent;

// --- Activity Group membership (spec activity-group-enrollment.md FR-7..14) ---

public sealed record ActivityGroupMemberAddedEvent(Guid MembershipId, Guid ActivityGroupId, Guid StudentId) : IDomainEvent;
public sealed record ActivityGroupMemberExitedEvent(Guid MembershipId, Guid ActivityGroupId, Guid StudentId) : IDomainEvent;
public sealed record ActivityGroupMemberRemovedEvent(Guid MembershipId, Guid ActivityGroupId, Guid StudentId) : IDomainEvent;
