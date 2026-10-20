using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Queries.GetTenantNotificationTemplateById;

/// <summary>
/// Loads one template by <see cref="Id"/> within the caller's visible scope (redesign spec §5), or
/// <see langword="null"/> when the row does not exist or is outside that scope. The detail page's read:
/// unlike <see cref="GetTenantNotificationTemplate"/> (which answers "the default row for this
/// kind &#215; channel"), this addresses a row by identity, so a template can be linked to.
/// </summary>
public sealed record GetTenantNotificationTemplateById(Guid Id) : IQuery<TenantNotificationTemplateDto?>;