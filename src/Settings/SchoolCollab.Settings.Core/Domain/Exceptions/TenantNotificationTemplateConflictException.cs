namespace SchoolCollab.Settings.Core.Domain.Exceptions;

/// <summary>
/// Raised when a create/update would leave a tenant with a <b>second</b> unconstrained template for
/// the same (kind, channel) (round notification-templates-b2c, spec Q15/Q66). An empty constraint set
/// <b>is</b> the tenant default, so exactly one such row may exist; the second write is a 409, not a
/// silent duplicate. Application-level guard by necessity: the database cannot express "at most one
/// row with no child rows", and the index that used to enforce one-template-per-slot is deliberately
/// relaxed to non-unique for candidates.
/// </summary>
public sealed class TenantNotificationTemplateConflictException(string message) : DomainException(message);
