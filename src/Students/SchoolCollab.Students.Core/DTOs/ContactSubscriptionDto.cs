using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.DTOs;

public sealed record ContactSubscriptionDto(
    Guid Id,
    Guid ContactId,
    SubscriptionScope Scope,
    SubscriptionStatus Status,
    Guid? ScopeRefId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
