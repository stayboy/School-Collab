using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.CQRS.Contacts.Commands.Unsubscribe;

public sealed record Unsubscribe(
    Guid ContactId,
    SubscriptionScope Scope,
    Guid? ScopeRefId) : ICommand;
