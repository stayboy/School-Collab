using SchoolCollab.Core.Notifications;
using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.Settings.Core.DTOs;

/// <summary>
/// Wire shape for a <see cref="Domain.TenantNotificationTemplate"/> including its
/// constraints. Nullable fields so callers can distinguish "unset" from a value;
/// the UI treats null subject/body as "built-in default applies".
/// <para>
/// Round notification-templates-b2c: a tenant holds several live rows per (kind, channel) —
/// the unconstrained one is the tenant default and every constrained one is a candidate for the
/// applicability matcher (spec §3.1.1/Q15). <see cref="CreatedAt"/> is the Q67 tie-break input
/// (<c>UpdatedAt</c> moves on edit and would be non-deterministic, so it cannot order ties).
/// </para>
/// <para>
/// Naming spec (D3/D6): <see cref="Title"/> is the row's identity everywhere and
/// <see cref="Description"/> is display-only metadata; <see cref="Code"/> is the generated exact
/// reference, minted on create and never writable — null for a row that predates the code column,
/// which the UI renders as a dash.
/// </para>
/// </summary>
public sealed record TenantNotificationTemplateDto(
    Guid Id,
    NotificationTemplateKind Kind,
    NotificationChannel Channel,
    string Title,
    string? Description,
    string? Code,
    string? Subject,
    string Body,
    IReadOnlyList<TemplateConstraintDto> Constraints,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? TenantId,
    string OwnerName);

/// <summary>One constraint dimension on a template (wire shape).</summary>
public sealed record TemplateConstraintDto(
    Guid Id,
    TemplateConstraintDimension Dimension,
    string Value);

/// <summary>
/// Create request for a notification template (<c>POST notification-templates</c>, round
/// notification-templates-b2c). The row's identity is its id, so the (kind, channel) pair is part
/// of the body rather than the route. <see cref="Title"/> is required — a blank one is a 400, and the
/// domain is the only place that rule lives; <see cref="Description"/> is optional. The generated
/// <c>code</c> is not part of this body: the create handler mints it (naming spec D5/D6).
/// <see cref="Subject"/> is ignored for SMS templates.
/// <see cref="Reason"/> is an optional free-text note copied onto the <c>TemplateAuditEntry</c>.
/// </summary>
public sealed record CreateTenantNotificationTemplateRequest(
    NotificationTemplateKind Kind,
    NotificationChannel Channel,
    string Title,
    string? Description,
    string? Subject,
    string Body,
    IReadOnlyList<TemplateConstraintRequest> Constraints,
    string? Reason = null);

/// <summary>
/// Replace request for one notification template, addressed by id (<c>PUT
/// notification-templates/{id}</c>). Kind and channel are immutable per row — changing them is a
/// create plus a delete, and the generated code is immutable too (it is never part of this body).
/// <see cref="Title"/> is required and <see cref="Description"/> optional, exactly as on create.
/// <see cref="Subject"/> is ignored for SMS templates.
/// </summary>
public sealed record UpdateTenantNotificationTemplateRequest(
    string Title,
    string? Description,
    string? Subject,
    string Body,
    IReadOnlyList<TemplateConstraintRequest> Constraints,
    string? Reason = null);

/// <summary>One constraint dimension in a create/update request.</summary>
public sealed record TemplateConstraintRequest(
    TemplateConstraintDimension Dimension,
    string Value);
