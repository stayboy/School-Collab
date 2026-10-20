using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SchoolCollab.Core.Notifications;

namespace SchoolCollab.Admin.Shared.Services;

// Mirrors the preview request/response of POST /assignments/notification-templates/preview
// (SchoolCollab.Assignments.Api/Endpoints/NotificationTemplatePreviewRoutes.cs). Re-declared here
// rather than referencing Assignments.Core — Admin.Shared holds no Assignments reference, matching
// the NotificationTemplateApiClient precedent above it.

/// <summary>
/// One live-preview request: the authored pair for a kind × channel, no constraints (spec §3.10 —
/// the render path consumes only subject and body once a template is selected).
/// </summary>
public sealed record NotificationTemplatePreviewRequest(
    NotificationTemplateKind Kind,
    NotificationChannel Channel,
    string? Subject,
    string Body);

/// <summary>The rendered pair the editor shows in its live preview.</summary>
public sealed record NotificationTemplatePreviewDto(string Subject, string Body);

/// <summary>
/// Admin client for the template live preview at
/// <c>POST /assignments/notification-templates/preview</c> (spec §3.10, round
/// notification-templates-b3b). The renderer lives in <c>Assignments.Core</c>, which the Settings
/// module may not reference, so the editor asks the owning host to run the exact sanitize →
/// substitute → escape path rather than re-implementing it.
/// <para>
/// <b>Enums travel by name</b> on this client: the request is serialized with a
/// <see cref="JsonStringEnumConverter"/>, so <c>"kind":"Reminder"</c> reaches the host (whose
/// Program.cs registers the matching converters) instead of the numeric value the admin clients'
/// default options would emit.
/// </para>
/// <para>
/// <b>Failure posture:</b> a non-success throws, matching the admin surface's show-the-error
/// posture — but the editor treats the throw as "preview unavailable" and never lets it reach the
/// save path. The registered HttpClient carries a ~5 s timeout so that note appears promptly rather
/// than after the 100 s default.
/// </para>
/// </summary>
public sealed class NotificationTemplatePreviewClient(HttpClient http)
{
    private const string PreviewPath = "/assignments/notification-templates/preview";

    /// <summary>Web defaults (camelCase) plus enum names — the host's own wire shape.</summary>
    private static readonly JsonSerializerOptions EnumNameJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Renders the authored pair through the Assignments host's exact render path.</summary>
    public async Task<NotificationTemplatePreviewDto> PreviewAsync(
        NotificationTemplateKind kind,
        NotificationChannel channel,
        string? subject,
        string body,
        CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync(
            PreviewPath,
            new NotificationTemplatePreviewRequest(kind, channel, subject, body),
            EnumNameJsonOptions,
            ct);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<NotificationTemplatePreviewDto>(cancellationToken: ct)
            ?? throw new HttpRequestException(
                $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} returned no body.");
    }
}
