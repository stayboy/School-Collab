using SchoolCollab.Core.CQRS;

namespace SchoolCollab.Settings.Core.CQRS.NotificationTemplates.Commands.DeleteTenantNotificationTemplate;

/// <summary>
/// Tombstones one notification template addressed by id (round notification-templates-b2c) — the
/// candidate disappears from selection and the tenant falls back to the default (or the built-in
/// default). Writes a <see cref="Domain.TemplateAuditEntry"/>, which is why the reason is required.
/// </summary>
public sealed record DeleteTenantNotificationTemplate(Guid TemplateId, string Reason) : ICommand;
