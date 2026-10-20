using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Commands.CreateTenantNotificationTemplate;

/// <summary>
/// Creates one notification template for the current tenant (round notification-templates-b2c).
/// A tenant may hold several live templates per (kind, channel); a request with an empty constraint
/// set creates the tenant <b>default</b> and is rejected with a conflict when one already exists.
/// Every mutation writes a <see cref="Domain.TemplateAuditEntry"/>.
/// </summary>
public sealed record CreateTenantNotificationTemplate(
    CreateTenantNotificationTemplateRequest Request,
    string? Reason) : ICommand;
