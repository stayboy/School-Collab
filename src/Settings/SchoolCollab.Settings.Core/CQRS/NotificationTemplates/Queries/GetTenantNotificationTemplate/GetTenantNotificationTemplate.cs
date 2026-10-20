using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Queries.GetTenantNotificationTemplate;

/// <summary>
/// Returns the current tenant's <b>default</b> notification template for
/// <see cref="Kind"/> × <see cref="Channel"/> — the row with an empty constraint set — or
/// <see langword="null"/> when none is configured (built-in default applies).
/// <para>
/// Round notification-templates-b2c: the row set is no longer one-per-slot, so a context-less read can
/// only mean the tenant default (spec Q15). Constrained candidates are read through the list query;
/// the selection path takes them from there and orders them itself.
/// </para>
/// </summary>
public sealed record GetTenantNotificationTemplate(
    NotificationTemplateKind Kind,
    NotificationChannel Channel) : IQuery<TenantNotificationTemplateDto?>;
