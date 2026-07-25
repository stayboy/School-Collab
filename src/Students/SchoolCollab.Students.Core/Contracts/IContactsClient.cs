using System.Threading;
using System.Threading.Tasks;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.Contracts;

/// <summary>
/// Contact / subscription surface exposed by the students API. Extracted as an
/// interface so that shared UI (e.g. <c>ContactsEditor</c> in Admin.Shared)
/// can depend on the contract from <c>SchoolCollab.Students.Core</c> instead of
/// the concrete <c>StudentsApiClient</c> in the Students Admin module.
/// </summary>
public interface IContactsClient
{
    Task<ContactDto[]?> ListContactsAsync(ContactOwnerType ownerType, Guid ownerId, CancellationToken ct = default);

    Task<Guid> AddContactAsync(AddContactRequest req, CancellationToken ct = default);

    Task UpdateContactAsync(Guid id, UpdateContactRequest req, CancellationToken ct = default);

    Task DeleteContactAsync(Guid id, CancellationToken ct = default);

    Task VerifyContactAsync(Guid id, CancellationToken ct = default);

    Task SetPrimaryContactAsync(Guid id, CancellationToken ct = default);

    Task<SubscribedContactDto[]?> ListSubscribedContactsAsync(
        ContactOwnerType ownerType, Guid? ownerId = null, SubscriptionScope? scope = null, CancellationToken ct = default);

    Task SubscribeAsync(
        Guid contactId, SubscriptionScope scope = SubscriptionScope.AllAssignments, Guid? scopeRefId = null, CancellationToken ct = default);

    Task UnsubscribeAsync(
        Guid contactId, SubscriptionScope scope = SubscriptionScope.AllAssignments, Guid? scopeRefId = null, CancellationToken ct = default);
}

/// <summary>Request body for creating a contact on a contact owner.</summary>
public record AddContactRequest(
    ContactOwnerType OwnerType,
    Guid OwnerId,
    ContactChannel Channel,
    string Value,
    string? Label,
    bool IsPrimary)
{
    public string? CountryCode { get; init; }
}

/// <summary>Request body for updating a contact's value / label.</summary>
public record UpdateContactRequest(string Value, string? Label)
{
    public string? CountryCode { get; init; }
}

/// <summary>Request body for (un)subscribing a contact to a scope.</summary>
public record SubscriptionRequest(SubscriptionScope Scope, Guid? ScopeRefId);
