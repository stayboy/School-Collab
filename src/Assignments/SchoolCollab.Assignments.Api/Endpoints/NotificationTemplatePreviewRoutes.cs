using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.Notifications;

namespace SchoolCollab.Assignments.Api.Endpoints;

/// <summary>
/// Request body for the template live preview (spec §3.10): the authored subject/body pair for one
/// kind × channel. No constraints ride the request — <see cref="ResolvedNotificationTemplate"/>
/// consumes only subject and body, so the preview renders exactly what the render path would render
/// once a template has been selected. Both enums travel by <b>name</b> on this host (the feature's
/// established wire precedent; the matching converters are registered in Program.cs).
/// </summary>
public sealed record NotificationTemplatePreviewRequest(
    NotificationKind Kind,
    NotificationChannel Channel,
    string? Subject,
    string Body);

/// <summary>The rendered subject/body pair the editor shows in its live preview.</summary>
public sealed record NotificationTemplatePreviewResponse(string Subject, string Body);

/// <summary>
/// The Settings editor's live-preview seam (spec §3.10, round notification-templates-b3b). The
/// renderer lives in <c>Assignments.Core</c>, which the Settings module may not reference, and §3.10
/// demands the <b>exact</b> path (sanitize → substitute → escape) — so the preview is a read on the
/// Assignments host, never a second substituter in the UI.
/// <para>
/// Enrichment read only: a failure degrades to the editor's inline "preview unavailable" note and
/// never touches the save path. No authorization call lives here — the <c>/assignments</c>
/// orchestrator group's <c>RequireAuthIfOidc</c> owns the policy.
/// </para>
/// </summary>
public static class NotificationTemplatePreviewRoutes
{
    /// <summary>The email sample link — the long <c>/deeplink/{token}</c> form (spec §3.2).</summary>
    private const string SampleEmailLink =
        "https://families.schoolcollab.example/deeplink/SAMPLE-EMAIL-TOKEN";

    /// <summary>The SMS/WhatsApp sample link — the short <c>/l/{code}</c> form (spec §3.2).</summary>
    private const string SampleSmsLink =
        "https://families.schoolcollab.example/l/SAMPLE01";

    /// <summary>
    /// Maps the live-preview route under the <c>/assignments</c> group:
    /// <c>POST /assignments/notification-templates/preview</c>.
    /// </summary>
    public static RouteGroupBuilder MapNotificationTemplatePreviewRoutes(this RouteGroupBuilder group)
    {
        group.MapPost("/notification-templates/preview",
            Task<Ok<NotificationTemplatePreviewResponse>> (
            [FromBody] NotificationTemplatePreviewRequest request,
            CancellationToken ct) =>
        {
            ct.ThrowIfCancellationRequested();

            // Guid.Empty is the preview sentinel: the renderer only echoes TemplateId back, and a
            // preview is never persisted. A null template would render the BUILT-IN defaults and
            // silently ignore the authored body, which is the opposite of a preview.
            var template = new ResolvedNotificationTemplate(Guid.Empty, request.Subject, request.Body);

            var rendered = NotificationTemplateRenderer.Render(
                request.Kind, request.Channel, template, SampleTags(request.Channel));

            return Task.FromResult(TypedResults.Ok(
                new NotificationTemplatePreviewResponse(rendered.Subject, rendered.Body)));
        });

        return group;
    }

    /// <summary>
    /// The documented sample data the preview renders against. Its one channel-sensitive part is
    /// <c>{{Link}}</c> (spec §3.2): the long email URL for Email, the short code form for
    /// SMS/WhatsApp — the sample bit a preview can get visibly wrong.
    /// </summary>
    private static NotificationTagValues SampleTags(NotificationChannel channel) => new()
    {
        AssignmentTitle = "Grade 8 Science: the water cycle investigation",
        Description = "A two-page write-up of the class experiment, with a labelled diagram of each stage.",
        DueDate = new DateTimeOffset(2026, 11, 5, 15, 0, 0, TimeSpan.Zero),
        Instructions = "<p>Read the method on page 12, then record each stage in your own words.</p>",
        ContactName = "Mrs Dlamini",
        GuardianRole = "Primary",
        StudentName = "Thabo Dlamini",
        SchoolName = "Riverside High",
        Link = channel == NotificationChannel.Email ? SampleEmailLink : SampleSmsLink,
        LinkExpiry = new DateTimeOffset(2026, 11, 12, 15, 0, 0, TimeSpan.Zero),
    };
}
