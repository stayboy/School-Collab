using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.Notifications;

namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// Reads the tenant's notification-template <b>candidates</b> for a (kind, channel) pair cross-context
/// from the Settings service (spec §3.1.1/§3.3, rounds notification-templates-b1/b2c). A tenant holds
/// several live templates per slot — one unconstrained tenant default plus any number of
/// constraint-scoped rows — so the read returns the whole set and the ordering belongs to
/// <see cref="NotificationTemplateEvaluator"/> (which alone knows the assignment's dimensions).
/// <b>Fail-open</b>: any Settings outage, non-success response or unparseable payload returns
/// <see cref="NotificationTemplateSlotState.Unreadable"/> with an empty list — a template outage must
/// never block a publish; the renderer falls back to the built-in defaults (§3.3).
/// </summary>
public interface INotificationTemplateResolver
{
    /// <summary>
    /// Reads the tenant's live candidates for <paramref name="kind"/> × <paramref name="channel"/>,
    /// <b>unordered</b>. <paramref name="tenantId"/> scopes the lookup.
    /// </summary>
    Task<NotificationTemplateCandidatesRead> ReadCandidatesAsync(
        NotificationKind kind,
        NotificationChannel channel,
        Guid tenantId,
        CancellationToken cancellationToken = default);
}

/// <summary>Discriminant for <see cref="NotificationTemplateCandidatesRead"/>.</summary>
public enum NotificationTemplateSlotState
{
    /// <summary>The tenant has no template configured for this (kind, channel) — no candidates.</summary>
    NotSet,

    /// <summary>The Settings read failed; callers should fail-open and not block the operation.</summary>
    Unreadable,

    /// <summary>At least one live, parseable candidate was returned.</summary>
    Found
}

/// <summary>
/// The result of <see cref="INotificationTemplateResolver.ReadCandidatesAsync"/>: a state plus the
/// candidate rows (empty unless <see cref="State"/> is <see cref="NotificationTemplateSlotState.Found"/>).
/// </summary>
public sealed record NotificationTemplateCandidatesRead(
    NotificationTemplateSlotState State,
    IReadOnlyList<NotificationTemplateCandidate> Candidates)
{
    /// <summary>The tenant has no template for the slot.</summary>
    public static NotificationTemplateCandidatesRead NotSet { get; } = new(NotificationTemplateSlotState.NotSet, []);

    /// <summary>The Settings read failed — fail open to the built-in default.</summary>
    public static NotificationTemplateCandidatesRead Unreadable { get; } = new(NotificationTemplateSlotState.Unreadable, []);

    /// <summary>A non-empty candidate set, unordered.</summary>
    public static NotificationTemplateCandidatesRead Found(IReadOnlyList<NotificationTemplateCandidate> candidates) =>
        new(NotificationTemplateSlotState.Found, candidates);
}

/// <summary>
/// One tenant template as read from Settings (round notification-templates-b2c): its id (persisted on
/// the <c>NotificationLog</c> row as the send-time record of what rendered), the authored subject
/// (email only — §5: subject ignored for SMS) and body (HTML for email, plain text for SMS/WhatsApp),
/// its constraint set (the applicability input, spec §3.1.1) and <see cref="CreatedAt"/> (Q67's
/// tie-break — <c>UpdatedAt</c> moves on edit and would be non-deterministic).
/// </summary>
public sealed record NotificationTemplateCandidate(
    Guid TemplateId,
    string? Subject,
    string Body,
    IReadOnlyList<NotificationTemplateConstraint> Constraints,
    DateTimeOffset CreatedAt,
    Guid? TenantId = null,
    string? OwnerName = null)
{
    /// <summary>
    /// Whether this candidate is part of the system blueprint (<see cref="TenantId"/> is
    /// <see langword="null"/>) rather than a tenant-owned row — the scope rank the matcher orders by
    /// (redesign spec §3.9).
    /// </summary>
    public bool IsSystem => TenantId is null;

    /// <summary>The display/render projection of this candidate.</summary>
    public ResolvedNotificationTemplate ToResolved() => new(TemplateId, Subject, Body);
}

/// <summary>
/// A tenant template as the render path consumes it: its id plus the authored subject and body.
/// </summary>
public sealed record ResolvedNotificationTemplate(Guid TemplateId, string? Subject, string Body);

/// <summary>One constraint dimension on a template, as the Settings wire carries it. The
/// dimension travels as a <b>string</b> (its wire enum name, e.g. <c>GradeLevel</c>) because
/// Assignments.Core holds no cross-context reference to Settings.Core — the
/// <c>TenantNotificationPolicyPayload</c> local-mirror precedent. An unrecognised dimension is
/// simply one the assignment never intersects, so it degrades to "not applicable", never an error.</summary>
public sealed record NotificationTemplateConstraint(string Dimension, string Value);
