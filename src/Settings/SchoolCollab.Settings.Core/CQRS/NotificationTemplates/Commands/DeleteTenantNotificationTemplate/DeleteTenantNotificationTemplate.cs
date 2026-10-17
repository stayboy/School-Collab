using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Commands.DeleteTenantNotificationTemplate;

/// <summary>
/// Deletes (soft-deletes, reverts to built-in default) the current tenant's
/// notification template for <see cref="Kind"/> × <see cref="Channel"/>.
/// Writes a <see cref="Domain.TemplateAuditEntry"/>.
/// </summary>
public sealed record DeleteTenantNotificationTemplate(
    NotificationTemplateKind Kind,
    NotificationChannel Channel,
    string Reason) : ICommand;