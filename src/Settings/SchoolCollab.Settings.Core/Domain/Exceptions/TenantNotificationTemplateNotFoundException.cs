namespace SchoolCollab.Settings.Core.Domain.Exceptions;

/// <summary>
/// Raised when a by-id template mutation names a row that is neither live nor in the current tenant
/// (the tenant query filter makes a foreign row indistinguishable from an absent one) — a 404 on the
/// wire, never a silent no-op.
/// </summary>
public sealed class TenantNotificationTemplateNotFoundException(Guid templateId)
    : DomainException($"Notification template '{templateId}' was not found.");
