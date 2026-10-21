namespace SchoolCollab.Settings.Core.Domain.Exceptions;

/// <summary>
/// Raised when a by-id template mutation targets a row outside the caller's write scope (redesign spec
/// 3.7 / section 6): a tenant session may <b>read</b> a system-blueprint row but never write it, and the
/// system session never writes a tenant row. A 403 on the wire - deliberately not a silent stamp and not
/// a 404, because the caller can already see the row it is being refused.
/// </summary>
public sealed class TenantNotificationTemplateScopeException(string message) : DomainException(message);