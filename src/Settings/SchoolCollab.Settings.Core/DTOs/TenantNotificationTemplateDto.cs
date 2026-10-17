using SchoolCollab.Core.Notifications;
using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.Settings.Core.DTOs;

/// <summary>
/// Wire shape for a <see cref="Domain.TenantNotificationTemplate"/> including its
/// constraints. Nullable fields so callers can distinguish "unset" from a value;
/// the UI treats null subject/body as "built-in default applies".
/// </summary>
public sealed record TenantNotificationTemplateDto(
    Guid Id,
    NotificationTemplateKind Kind,
    NotificationChannel Channel,
    string? Subject,
    string Body,
    IReadOnlyList<TemplateConstraintDto> Constraints,
    DateTimeOffset UpdatedAt);

/// <summary>One constraint dimension on a template (wire shape).</summary>
public sealed record TemplateConstraintDto(
    Guid Id,
    TemplateConstraintDimension Dimension,
    string Value);

/// <summary>
/// Upsert request for a notification template (<c>PUT …/{kind}/{channel}</c>, spec §5).
/// <see cref="Subject"/> is ignored for SMS templates. <see cref="Reason"/> is an
/// optional free-text note copied onto the <c>TemplateAuditEntry</c>; omit it and the
/// audit row records the mutation without a note.
/// </summary>
public sealed record UpsertTenantNotificationTemplateRequest(
    string? Subject,
    string Body,
    IReadOnlyList<UpsertTemplateConstraintRequest> Constraints,
    string? Reason = null);

/// <summary>One constraint dimension in an upsert request.</summary>
public sealed record UpsertTemplateConstraintRequest(
    TemplateConstraintDimension Dimension,
    string Value);