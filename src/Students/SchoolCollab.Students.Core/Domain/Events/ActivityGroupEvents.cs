namespace SchoolCollab.Students.Core.Domain.Events;

// --- Activity Group lifecycle (spec activity-group-enrollment.md FR-1..6) ---

public sealed record ActivityGroupCreatedEvent(Guid ActivityGroupId, string Name) : IDomainEvent;
public sealed record ActivityGroupUpdatedEvent(Guid ActivityGroupId, string Name) : IDomainEvent;
public sealed record ActivityGroupSuspendedEvent(Guid ActivityGroupId, string Name) : IDomainEvent;
public sealed record ActivityGroupArchivedEvent(Guid ActivityGroupId, string Name) : IDomainEvent;
public sealed record ActivityGroupReactivatedEvent(Guid ActivityGroupId, string Name) : IDomainEvent;
public sealed record ActivityGroupDeletedEvent(Guid ActivityGroupId, string Name) : IDomainEvent;

// --- Activity Group membership (spec activity-group-enrollment.md FR-7..14) ---

public sealed record ActivityGroupMemberAddedEvent(Guid MembershipId, Guid ActivityGroupId, Guid StudentId) : IDomainEvent;
public sealed record ActivityGroupMemberExitedEvent(Guid MembershipId, Guid ActivityGroupId, Guid StudentId) : IDomainEvent;
public sealed record ActivityGroupMemberRemovedEvent(Guid MembershipId, Guid ActivityGroupId, Guid StudentId) : IDomainEvent;
