using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Core.Services.Delivery;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Assignments.Api.Services;

/// <summary>
/// HTTP-backed <see cref="IContactAddressResolver"/> (WS-E2 / ar-16). Reads the contact
/// owner's contacts from the Students API (<c>GET /contacts?ownerType=..&amp;ownerId=..</c>,
/// whose <see cref="ContactDto.Value"/> is the email address / phone number) and returns
/// the value of the requested contact id. The named <c>students-api</c> client resolves
/// through Aspire service discovery — the same split that keeps HTTP out of Core as
/// <see cref="StudentsContactResolver"/> and <see cref="NotificationPolicyResolver"/>.
/// A fetch failure returns null: the broadcast records the recipient as
/// <c>Skipped</c> rather than blocking the publish.
/// </summary>
public sealed class StudentsContactAddressResolver(
    IHttpClientFactory httpClientFactory,
    ILogger<StudentsContactAddressResolver> logger) : IContactAddressResolver
{
    /// <inheritdoc />
    public async Task<string?> ResolveAddressAsync(
        Guid contactId,
        ContactOwnerType ownerType,
        Guid ownerId,
        CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient("students-api");
        try
        {
            var contacts = await client.GetFromJsonAsync<ContactDto[]>(
                $"contacts?ownerType={(int)ownerType}&ownerId={ownerId}", cancellationToken) ?? [];

            return contacts.FirstOrDefault(c => c.Id == contactId)?.Value;
        }
        // Genuine cancellation propagates (the caller aborts the broadcast and queues nothing);
        // a transport timeout or a fetch failure degrades to null → the recipient is Skipped.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Failed to resolve the address for contact {ContactId}", contactId);
            return null;
        }
    }
}
