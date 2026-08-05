using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.CQRS.NotificationPolicies.Queries.GetTenantNotificationPolicy;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.CQRS.NotificationPolicies.Queries.GetTenantNotificationPolicy;

/// <summary>
/// Loads the current tenant's policy row via the tenant query filter, returning the
/// DTO or <see langword="null"/> when the tenant has not configured one yet.
/// </summary>
public sealed class GetTenantNotificationPolicyHandler(SettingsDbContext db)
    : IQueryHandler<GetTenantNotificationPolicy, TenantNotificationPolicyDto?>
{
    public async Task<TenantNotificationPolicyDto?> HandleAsync(
        GetTenantNotificationPolicy query, CancellationToken ct = default)
    {
        var policy = await db.TenantNotificationPolicies
            .AsNoTracking()
            .SingleOrDefaultAsync(ct);

        if (policy is null)
        {
            return null;
        }

        return new TenantNotificationPolicyDto(
            policy.Id,
            policy.PreferredChannelOrder,
            policy.BlockedChannels,
            policy.MaxNotifications,
            policy.MaxReminders,
            policy.ReminderIntervalHours,
            policy.LinkValidityDays,
            policy.SendoutTimeOfDay,
            policy.SendoutIntervalMinutes,
            policy.UpdatedAt);
    }
}
