using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Queries.GetTenantNotificationTemplates;

/// <summary>
/// Returns all notification templates for the current tenant, including their
/// constraints. Empty list means no tenant templates are configured; built-in
/// defaults apply everywhere.
/// </summary>
public sealed record GetTenantNotificationTemplates : IQuery<IReadOnlyList<TenantNotificationTemplateDto>>;