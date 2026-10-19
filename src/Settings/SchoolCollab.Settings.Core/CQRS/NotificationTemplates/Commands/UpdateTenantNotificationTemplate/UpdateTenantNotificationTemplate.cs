using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Commands.UpdateTenantNotificationTemplate;

/// <summary>
/// Replaces one notification template addressed by id (round notification-templates-b2c). Kind and
/// channel are immutable per row — a different (kind, channel) is a create plus a delete. Every
/// mutation writes a <see cref="Domain.TemplateAuditEntry"/>.
/// </summary>
public sealed record UpdateTenantNotificationTemplate(
    Guid TemplateId,
    UpdateTenantNotificationTemplateRequest Request,
    string? Reason) : ICommand;
