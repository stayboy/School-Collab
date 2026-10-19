using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.Notifications;

namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// Resolves the tenant's notification template for a (kind, channel) pair cross-context
/// from the Settings service (spec §3.3, round notification-templates-b1). <b>Fail-open</b>:
/// any Settings outage, non-success response or unparseable payload returns
/// <see langword="null"/> — a template outage must never block a publish; the renderer
/// falls back to the built-in defaults (§3.3). Mirrors the <c>IAssignmentPolicyResolver</c>
/// posture (named client, explicit timeout, fail-open by contract).
/// </summary>
public interface INotificationTemplateResolver
{
    /// <summary>
    /// Resolves the tenant override template, or <see langword="null"/> when none is set
    /// or the Settings read fails (fail-open). <paramref name="tenantId"/> scopes the lookup.
    /// </summary>
    Task<ResolvedNotificationTemplate?> ResolveAsync(
        NotificationKind kind,
        NotificationChannel channel,
        Guid tenantId,
        CancellationToken cancellationToken = default);
}

/// <summary>A tenant template as resolved from Settings: its id (persisted on the
/// <c>NotificationLog</c> row as the send-time record of what rendered) plus the authored
/// subject (email only — §5: subject ignored for SMS) and body (HTML for email, plain
/// text for SMS/WhatsApp).</summary>
public sealed record ResolvedNotificationTemplate(Guid TemplateId, string? Subject, string Body);