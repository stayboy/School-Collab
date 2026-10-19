using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.Notifications;

namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// HTTP-backed <see cref="INotificationTemplateResolver"/> (round notification-templates-b1,
/// spec §3.3). Reads the tenant's override template from the Settings API's
/// <c>GET /api/settings/notification-templates/{kind}/{channel}</c> on the documented
/// <c>settings-api</c> named client (Aspire service discovery; same client as the
/// policy/consent resolvers) — one implementation registered by <b>both</b> hosts
/// (Assignments.Api and Assignments.Worker), the <c>NotificationPolicyResolver</c>
/// relocation precedent: the E3 sweeps must render an edited Reminder/Completion/Overdue
/// template exactly as the publish path renders Publish.
///
/// <para><b>Fail-open by contract</b> (§3.3): any transport failure, non-success status,
/// unparseable or non-JSON payload, or the 204 "no template set" returns
/// <see langword="null"/> — the renderer then falls back to the built-in defaults and a
/// template outage can never block a publish. A per-request 5-second timeout bounds the
/// cross-context read so a hung Settings service delays a publish by at most one
/// resolution.</para>
/// </summary>
public sealed class HttpNotificationTemplateResolver(
    IHttpClientFactory httpClientFactory,
    ILogger<HttpNotificationTemplateResolver> logger) : INotificationTemplateResolver
{
    /// <summary>The cross-context read wall clock — a hung Settings call must not stall a publish.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <inheritdoc />
    public async Task<ResolvedNotificationTemplate?> ResolveAsync(
        NotificationKind kind,
        NotificationChannel channel,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var settings = httpClientFactory.CreateClient("settings-api");
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(Timeout);

            var response = await settings.GetAsync(
                $"api/settings/notification-templates/{kind}/{channel}", timeoutCts.Token);

            // 204 = no tenant template set ⇒ built-in defaults apply (not a failure).
            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadFromJsonAsync<TenantNotificationTemplatePayload>(timeoutCts.Token);
            return payload is null
                ? null
                : new ResolvedNotificationTemplate(payload.Id, payload.Subject, payload.Body);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The 5s wall clock fired, not the caller — degrade, never abort the publish.
            logger.LogWarning(
                "Timed out resolving the {Kind}/{Channel} notification template; using built-in defaults",
                kind, channel);
            return null;
        }
        catch (OperationCanceledException)
        {
            throw; // genuine caller cancellation propagates
        }
        catch (Exception ex) when (ex is HttpRequestException
            or InvalidOperationException
            or System.Text.Json.JsonException
            // ReadFromJsonAsync throws NotSupportedException when a 200 carries a non-JSON
            // content type (a gateway/HTML error page): §3.3's fail-open covers "garbage", not
            // just outages — it must not escape as a thrown exception that aborts the publish.
            or NotSupportedException)
        {
            logger.LogWarning(
                ex,
                "Failed to resolve the {Kind}/{Channel} notification template; using built-in defaults",
                kind, channel);
            return null;
        }
    }

    /// <summary>
    /// The wire mirror of the Settings template read — exactly the three fields the render
    /// path consumes. A local mirror, not <c>Settings.Core</c>'s DTO, because Assignments.Core
    /// holds no cross-context reference to Settings (the <c>TenantNotificationPolicyPayload</c>
    /// precedent); <c>Kind</c> and <c>Channel</c> are the request's own URL keys and are not
    /// re-read from the payload.
    /// </summary>
    internal sealed record TenantNotificationTemplatePayload(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("subject")] string? Subject,
        [property: JsonPropertyName("body")] string Body);
}
