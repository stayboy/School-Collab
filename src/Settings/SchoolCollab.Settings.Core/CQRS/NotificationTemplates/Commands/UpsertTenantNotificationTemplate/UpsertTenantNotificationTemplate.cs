using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Commands.UpsertTenantNotificationTemplate;

/// <summary>
/// Creates or replaces the current tenant's notification template for
/// <see cref="Kind"/> × <see cref="Channel"/>. Every mutation writes a
/// <see cref="Domain.TemplateAuditEntry"/>.
/// </summary>
public sealed record UpsertTenantNotificationTemplate(
    NotificationTemplateKind Kind,
    NotificationChannel Channel,
    UpsertTenantNotificationTemplateRequest Request,
    string Reason) : ICommand;