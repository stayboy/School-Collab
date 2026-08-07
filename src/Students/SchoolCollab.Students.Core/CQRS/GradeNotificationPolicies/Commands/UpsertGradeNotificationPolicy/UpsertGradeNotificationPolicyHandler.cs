using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.CQRS.GradeNotificationPolicies.Commands.UpsertGradeNotificationPolicy;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.GradeNotificationPolicies.Commands.UpsertGradeNotificationPolicy;

/// <summary>
/// Upserts the single override row for a grade. Rejects a non-existent grade level to
/// avoid orphan policy rows. The tenant query filter scopes reads/writes to the caller's
/// tenant.
/// </summary>
public sealed class UpsertGradeNotificationPolicyHandler(
    StudentsDbContext db,
    ITenantProvider tenantProvider) : ICommandHandler<UpsertGradeNotificationPolicy, GradeNotificationPolicyDto>
{
    public async Task<GradeNotificationPolicyDto> HandleAsync(
        UpsertGradeNotificationPolicy command, CancellationToken ct = default)
    {
        // Reject overrides for grades that don't exist in this tenant.
        var gradeExists = await db.GradeLevels.AnyAsync(x => x.Id == command.GradeLevelId, ct);
        if (!gradeExists)
        {
            throw new GradeLevelNotFoundException(command.GradeLevelId);
        }

        var tenantId = tenantProvider.GetTenantContext().TenantId;

        var existing = await db.GradeNotificationPolicies
            .SingleOrDefaultAsync(x => x.GradeLevelId == command.GradeLevelId, ct);

        GradeNotificationPolicy policy;
        if (existing is not null)
        {
            existing.SetOverride(
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
            policy = GradeNotificationPolicy.Create(
                tenantId,
                command.GradeLevelId,
                command.PreferredChannelOrder,
                command.BlockedChannels,
                command.MaxNotifications,
                command.MaxReminders,
                command.ReminderIntervalHours,
                command.LinkValidityDays,
                command.SendoutTimeOfDay,
                command.SendoutIntervalMinutes);
            db.GradeNotificationPolicies.Add(policy);
        }

        await db.SaveChangesAsync(ct);

        return new GradeNotificationPolicyDto(
            policy.GradeLevelId,
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
