using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.CQRS.NotificationPolicies.Commands.UpsertTenantNotificationPolicy;
using SchoolCollab.Settings.Core.Data;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.CQRS.NotificationPolicies.Commands.UpsertTenantNotificationPolicy;

/// <summary>
/// Upserts the single global-default policy row for the current tenant. The tenant
/// query filter scopes reads to the current tenant; the row is created when absent.
/// </summary>
public sealed class UpsertTenantNotificationPolicyHandler(
    SettingsDbContext db,
    ITenantProvider tenantProvider) : ICommandHandler<UpsertTenantNotificationPolicy, TenantNotificationPolicyDto>
{
    public async Task<TenantNotificationPolicyDto> HandleAsync(
        UpsertTenantNotificationPolicy command, CancellationToken ct = default)
    {
        var tenantId = tenantProvider.GetTenantContext().TenantId;

        var existing = await db.TenantNotificationPolicies.SingleOrDefaultAsync(ct);
        TenantNotificationPolicy policy;
        if (existing is not null)
        {
            existing.SetPolicy(
                command.PreferredChannelOrder,
                command.BlockedChannels,
                command.MaxNotifications,
                command.MaxReminders,
                command.ReminderIntervalHours,
                command.LinkValidityDays,
                command.SendoutTimeOfDay,
                command.SendoutIntervalMinutes);
            policy = existing;
        }
        else
        {
            policy = TenantNotificationPolicy.Create(
                tenantId,
                command.PreferredChannelOrder,
                command.BlockedChannels,
                command.MaxNotifications,
                command.MaxReminders,
                command.ReminderIntervalHours,
                command.LinkValidityDays,
                command.SendoutTimeOfDay,
                command.SendoutIntervalMinutes);
            db.TenantNotificationPolicies.Add(policy);
        }

        await db.SaveChangesAsync(ct);

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
