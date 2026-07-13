using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.Contacts.Queries.GetSubscription;

public sealed class GetSubscriptionHandler(
    IContactRepository repository) : IQueryHandler<GetSubscription, ContactSubscriptionDto?>
{
    public async Task<ContactSubscriptionDto?> HandleAsync(GetSubscription query, CancellationToken cancellationToken = default)
    {
        var s = await repository.GetSubscriptionAsync(query.ContactId, query.Scope, query.ScopeRefId, cancellationToken);
        return s is null
            ? null
            : new ContactSubscriptionDto(
                s.Id, s.ContactId, s.Scope, s.Status, s.ScopeRefId, s.CreatedAt, s.UpdatedAt);
    }
}
