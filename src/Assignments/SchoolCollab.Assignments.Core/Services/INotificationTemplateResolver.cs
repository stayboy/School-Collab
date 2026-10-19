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

    /// <summary>
    /// The same single-slot read as <see cref="ResolveAsync"/>, without discarding the
    /// constraint metadata (round notification-templates-b2a, WS5): the publish-options surface
    /// shows a slot's constraints <b>as display-only text</b>, and the publish path validates an
    /// explicit author stamp against the slot's id. The render path keeps using
    /// <see cref="ResolveAsync"/> — one HTTP read, one fail-open filter, two projections.
    /// </summary>
    Task<NotificationTemplateSlot?> ResolveSlotAsync(
        NotificationKind kind,
        NotificationChannel channel,
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Discriminated single-slot read (round notification-templates-b2a final fix): tells the
    /// caller whether the tenant has no template set, the slot was found, or the Settings read
    /// failed (outage/timeout/unparseable). The publish path needs this to fail-open on Settings
    /// problems without rejecting an explicit stamp when no live template exists.
    /// </summary>
    Task<NotificationTemplateSlotRead> ReadSlotAsync(
        NotificationKind kind,
        NotificationChannel channel,
        Guid tenantId,
        CancellationToken cancellationToken = default);
}

/// <summary>Discriminant for <see cref="NotificationTemplateSlotRead"/>.</summary>
public enum NotificationTemplateSlotState
{
    /// <summary>The tenant has no template configured for this (kind, channel).</summary>
    NotSet,

    /// <summary>The Settings read failed; callers should fail-open and not block the operation.</summary>
    Unreadable,

    /// <summary>A live, parseable template slot was returned.</summary>
    Found
}

/// <summary>The result of <see cref="INotificationTemplateResolver.ReadSlotAsync"/>:
/// a state plus the slot (only meaningful when <see cref="State"/> is
/// <see cref="NotificationTemplateSlotState.Found"/>).</summary>
public sealed record NotificationTemplateSlotRead(NotificationTemplateSlotState State, NotificationTemplateSlot? Slot);

/// <summary>A tenant template as resolved from Settings: its id (persisted on the
/// <c>NotificationLog</c> row as the send-time record of what rendered) plus the authored
/// subject (email only — §5: subject ignored for SMS) and body (HTML for email, plain
/// text for SMS/WhatsApp).</summary>
public sealed record ResolvedNotificationTemplate(Guid TemplateId, string? Subject, string Body);

/// <summary>
/// The full single-slot read (round notification-templates-b2a, WS5): the render fields plus
/// the slot's identity and constraint metadata. Constraints are <b>display-only</b> here — with
/// the applicability matcher deferred to B2c there is exactly one live template per
/// (tenant, kind, channel), so nothing orders or filters on them yet.
/// </summary>
public sealed record NotificationTemplateSlot(
    Guid TemplateId,
    NotificationKind Kind,
    NotificationChannel Channel,
    string? Subject,
    string Body,
    IReadOnlyList<NotificationTemplateConstraint> Constraints);

/// <summary>One constraint dimension on a template, as the Settings wire carries it. The
/// dimension travels as a <b>string</b> (its wire enum name, e.g. <c>GradeLevel</c>) because
/// Assignments.Core holds no cross-context reference to Settings.Core — the
/// <c>TenantNotificationPolicyPayload</c> local-mirror precedent.</summary>
public sealed record NotificationTemplateConstraint(string Dimension, string Value);
