using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.Notifications;

namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// HTTP-backed <see cref="INotificationTemplateResolver"/> (rounds notification-templates-b1/b2c, spec
/// §3.3). Reads the tenant's whole template list from the Settings API's
/// <c>GET /api/settings/notification-templates</c> on the documented <c>settings-api</c> named client
/// (Aspire service discovery; same client as the policy/consent resolvers) — one implementation
/// registered by <b>both</b> hosts (Assignments.Api and Assignments.Worker), the
/// <c>NotificationPolicyResolver</c> relocation precedent: the E3 sweeps must render an edited
/// Reminder/Completion/Overdue template exactly as the publish path renders Publish.
///
/// <para>One HTTP read per (kind, channel) — the list is filtered to the requested pair in memory, so
/// no second Settings route exists for candidates (the round's explicit decision). The result is
/// <b>unordered</b>: the read has no assignment context, so the total order belongs to
/// <see cref="NotificationTemplateEvaluator"/> (Q67).</para>
///
/// <para><b>Fail-open by contract</b> (§3.3): any transport failure, non-success status, unparseable
/// or non-JSON payload returns <see cref="NotificationTemplateSlotState.Unreadable"/> — the renderer
/// then falls back to the built-in defaults and a template outage can never block a publish. A
/// per-request 5-second timeout bounds the cross-context read so a hung Settings service delays a
/// publish by at most one resolution. Genuine caller cancellation propagates.</para>
/// </summary>
public sealed class HttpNotificationTemplateResolver(
    IHttpClientFactory httpClientFactory,
    ILogger<HttpNotificationTemplateResolver> logger) : INotificationTemplateResolver
{
    /// <summary>The cross-context read wall clock — a hung Settings call must not stall a publish.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <inheritdoc />
    public async Task<NotificationTemplateCandidatesRead> ReadCandidatesAsync(
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

            var response = await settings.GetAsync("api/settings/notification-templates", timeoutCts.Token);

            // 204 carries no body: the tenant has no templates at all.
            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return NotificationTemplateCandidatesRead.NotSet;
            }

            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadFromJsonAsync<List<TenantNotificationTemplatePayload>>(
                timeoutCts.Token);
            if (payload is null)
            {
                logger.LogWarning(
                    "Settings returned an empty notification-template list payload; treating the {Kind}/{Channel} "
                    + "read as unreadable",
                    kind, channel);
                return NotificationTemplateCandidatesRead.Unreadable;
            }

            var candidates = payload
                .Where(row => row.Kind == (int)kind && row.Channel == (int)channel)
                .Select(row => new NotificationTemplateCandidate(
                    row.Id,
                    row.Subject,
                    row.Body,
                    row.Constraints?
                        .Select(c => new NotificationTemplateConstraint(DimensionName(c.Dimension), c.Value))
                        .ToList() ?? [],
                    row.CreatedAt))
                .ToList();

            return candidates.Count == 0
                ? NotificationTemplateCandidatesRead.NotSet
                : NotificationTemplateCandidatesRead.Found(candidates);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The 5s wall clock fired, not the caller — degrade, never abort the publish.
            logger.LogWarning(
                "Timed out resolving the {Kind}/{Channel} notification templates; using built-in defaults",
                kind, channel);
            return NotificationTemplateCandidatesRead.Unreadable;
        }
        catch (OperationCanceledException)
        {
            throw; // genuine caller cancellation propagates
        }
        catch (Exception ex) when (ex is HttpRequestException
            or InvalidOperationException
            or System.Text.Json.JsonException
            // ReadFromJsonAsync throws NotSupportedException when a 200 carries a non-JSON
            // content type (a gateway/HTML error page): fail-open covers "garbage", not just outages.
            or NotSupportedException)
        {
            logger.LogWarning(
                ex,
                "Failed to read the {Kind}/{Channel} notification templates; using built-in defaults",
                kind, channel);
            return NotificationTemplateCandidatesRead.Unreadable;
        }
    }

    /// <summary>
    /// The wire mirror of the Settings template list row. A local mirror, not <c>Settings.Core</c>'s
    /// DTO, because Assignments.Core holds no cross-context reference to Settings (the
    /// <c>TenantNotificationPolicyPayload</c> precedent). <c>kind</c>, <c>channel</c> and each
    /// constraint's <c>dimension</c> arrive as <b>numbers</b> — the Settings host registers no
    /// <c>JsonStringEnumConverter</c>, so its enum properties serialize with their underlying values,
    /// which is also the encoding the settings-db columns persist — and the dimension is translated to
    /// its name by <see cref="DimensionName"/>.
    /// </summary>
    internal sealed record TenantNotificationTemplatePayload(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("kind")] int Kind,
        [property: JsonPropertyName("channel")] int Channel,
        [property: JsonPropertyName("subject")] string? Subject,
        [property: JsonPropertyName("body")] string Body,
        [property: JsonPropertyName("constraints")]
        IReadOnlyList<TenantNotificationTemplateConstraintPayload>? Constraints,
        [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt);

    /// <summary>One constraint row from the Settings template payload.</summary>
    internal sealed record TenantNotificationTemplateConstraintPayload(
        [property: JsonPropertyName("dimension")] int Dimension,
        [property: JsonPropertyName("value")] string Value);

    /// <summary>
    /// The wire enum value of a constraint dimension as its dimension name. The Settings host
    /// registers no <c>JsonStringEnumConverter</c>, so the value arrives as the enum's underlying
    /// integer — the same encoding the settings-db column persists (<c>HasConversion&lt;int&gt;</c>),
    /// so these numbers are a frozen contract, not an incidental order. A value with no name is
    /// returned as its invariant text, which matches no assignment dimension and therefore degrades to
    /// "not applicable" (never an error).
    /// </summary>
    private static string DimensionName(int value) => value switch
    {
        0 => TemplateConstraintDimensions.GradeLevel,
        1 => TemplateConstraintDimensions.Stream,
        2 => TemplateConstraintDimensions.ActivityGroup,
        3 => TemplateConstraintDimensions.Subject,
        4 => TemplateConstraintDimensions.AssignmentType,
        _ => value.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}
