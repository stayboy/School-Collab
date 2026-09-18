using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Core.Services.Delivery;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Assignments.Worker.Services;

/// <summary>
/// HTTP-backed <see cref="IContactAddressResolver"/> for the Assignments.Worker sweeps
/// (E3). Reads the contact owner's contacts from the Students API (<c>GET
/// /contacts?ownerType=..&amp;ownerId=..</c>, whose <see cref="ContactDto.Value"/> is the
/// email address / phone number) and returns the requested contact id's value. Mirrors
/// the Api-layer resolver so the worker stays HTTP-free at the boundary the same way.
/// A fetch failure returns null — the sweep records the recipient as <c>Skipped</c>.
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
