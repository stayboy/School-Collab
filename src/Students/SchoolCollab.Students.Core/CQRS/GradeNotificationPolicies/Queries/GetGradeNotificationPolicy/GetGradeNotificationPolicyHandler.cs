using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Students.Core.CQRS.GradeNotificationPolicies.Queries.GetGradeNotificationPolicy;
using SchoolCollab.Students.Core.Data;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Students.Core.CQRS.GradeNotificationPolicies.Queries.GetGradeNotificationPolicy;

/// <summary>
/// Loads the grade's override row via the tenant query filter, returning the DTO or
/// <see langword="null"/> when the grade has no override configured.
/// </summary>
public sealed class GetGradeNotificationPolicyHandler(StudentsDbContext db)
    : IQueryHandler<GetGradeNotificationPolicy, GradeNotificationPolicyDto?>
{
    public async Task<GradeNotificationPolicyDto?> HandleAsync(
        GetGradeNotificationPolicy query, CancellationToken ct = default)
    {
        var policy = await db.GradeNotificationPolicies
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.GradeLevelId == query.GradeLevelId, ct);

        if (policy is null)
        {
            return null;
        }

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
