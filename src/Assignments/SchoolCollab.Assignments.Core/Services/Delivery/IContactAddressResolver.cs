using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Assignments.Core.Services.Delivery;

/// <summary>
/// Resolves the delivery address (email address / phone number) of a contact by id.
/// The interface lives in Assignments.Core; the HTTP implementation (Students API
/// <c>GET /contacts?ownerType=..&amp;ownerId=..</c>, whose <c>ContactDto.Value</c> is the
/// address) lives in Assignments.Api — the same cross-context resolver split used by
/// <see cref="IContactResolver"/> and <see cref="INotificationPolicyResolver"/>.
///
/// <para>Resolution happens once, at queue time: the rendered address is persisted on
/// the <c>NotificationLog</c> row so a retry never re-resolves.</para>
/// </summary>
public interface IContactAddressResolver
{
    /// <summary>Returns the contact's address value, or null when it cannot be resolved.</summary>
    Task<string?> ResolveAddressAsync(
        Guid contactId,
        ContactOwnerType ownerType,
        Guid ownerId,
        CancellationToken cancellationToken = default);
}
