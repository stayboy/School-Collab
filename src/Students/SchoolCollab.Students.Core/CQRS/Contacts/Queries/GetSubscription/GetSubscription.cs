using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Contacts.Queries.GetSubscription;

public sealed record GetSubscription(
    Guid ContactId,
    SubscriptionScope Scope,
    Guid? ScopeRefId) : IQuery<ContactSubscriptionDto?>;
