using SchoolCollab.Core.CQRS;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.CQRS.NotificationPolicies.Queries.GetTenantNotificationPolicy;

/// <summary>
/// Returns the current tenant's global default notification policy, or
/// <see langword="null"/> if none has been configured yet. A null result means the
/// caller falls back to built-in defaults.
/// </summary>
public sealed record GetTenantNotificationPolicy : IQuery<TenantNotificationPolicyDto?>
{
    public static readonly GetTenantNotificationPolicy Instance = new();
}
