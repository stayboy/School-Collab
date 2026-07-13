using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Core.CQRS.Contacts.Commands.Subscribe;

public sealed record Subscribe(
    Guid ContactId,
    SubscriptionScope Scope,
    Guid? ScopeRefId) : ICommand;
