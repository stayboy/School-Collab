using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Queries.GetTenantNotificationTemplate;

/// <summary>
/// Returns the notification template for the current tenant's <see cref="Kind"/> ×
/// <see cref="Channel"/>, or <see langword="null"/> when none is configured (built-in
/// default applies).
/// </summary>
public sealed record GetTenantNotificationTemplate(
    NotificationTemplateKind Kind,
    NotificationChannel Channel) : IQuery<TenantNotificationTemplateDto?>;