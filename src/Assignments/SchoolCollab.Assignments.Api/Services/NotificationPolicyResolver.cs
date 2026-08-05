using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Settings.Core.DTOs;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Assignments.Api.Services;

/// <summary>
/// HTTP-backed <see cref="INotificationPolicyResolver"/> (notification-delivery-plan.md
/// §3). Reads the tenant-global default from the Settings API and the optional per-grade
/// override from the Students API, then merges them with the shared
/// <see cref="EffectiveNotificationPolicyResolver"/> into the effective policy. Named
/// clients <c>settings-api</c> + <c>students-api</c> resolve through Aspire service
/// discovery (AppHost wires assignments-api → settings-api + students-api).
/// Any fetch failure degrades gracefully to the built-in default (empty policy), matching
/// the "best-effort notification" posture — the publish is never blocked by policy.
/// </summary>
public sealed class NotificationPolicyResolver(
    IHttpClientFactory httpClientFactory,
    ILogger<NotificationPolicyResolver> logger) : INotificationPolicyResolver
{
    private static readonly EffectiveNotificationPolicyResolver _merger = new();

    public async Task<EffectiveNotificationPolicy> ResolveEffectiveAsync(
        Guid tenantId, Guid? gradeLevelId, CancellationToken ct = default)
    {
        var tenantDefault = await FetchTenantDefaultAsync(ct);
        NotificationPolicyFields? gradeOverride = null;
        if (gradeLevelId.HasValue)
            gradeOverride = await FetchGradeOverrideAsync(gradeLevelId.Value, ct);

        return _merger.Resolve(tenantDefault, gradeOverride);
    }

    private async Task<NotificationPolicyFields?> FetchTenantDefaultAsync(CancellationToken ct)
    {
        var settings = httpClientFactory.CreateClient("settings-api");
        try
        {
            var dto = await settings.GetFromJsonAsync<TenantNotificationPolicyDto>(
                "/api/settings/notification-policy", ct);
            return dto is null ? null : ToFields(dto);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Failed to resolve tenant notification policy; using built-in default");
            return null;
        }
    }

    private async Task<NotificationPolicyFields?> FetchGradeOverrideAsync(Guid gradeLevelId, CancellationToken ct)
    {
        var students = httpClientFactory.CreateClient("students-api");
        try
        {
            var dto = await students.GetFromJsonAsync<GradeNotificationPolicyDto>(
                $"students/grade-levels/{gradeLevelId}/notification-policy", ct);
            return dto is null ? null : ToFields(dto);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Failed to resolve grade notification policy for {GradeLevelId}", gradeLevelId);
            return null;
        }
    }

    private static NotificationPolicyFields ToFields(TenantNotificationPolicyDto dto) => new()
    {
        PreferredChannelOrder = dto.PreferredChannelOrder,
        BlockedChannels = dto.BlockedChannels,
        MaxNotifications = dto.MaxNotifications,
        MaxReminders = dto.MaxReminders,
        ReminderIntervalHours = dto.ReminderIntervalHours,
        LinkValidityDays = dto.LinkValidityDays,
        SendoutTimeOfDay = dto.SendoutTimeOfDay,
        SendoutIntervalMinutes = dto.SendoutIntervalMinutes,
    };

    private static NotificationPolicyFields ToFields(GradeNotificationPolicyDto dto) => new()
    {
        PreferredChannelOrder = dto.PreferredChannelOrder,
        BlockedChannels = dto.BlockedChannels,
        MaxNotifications = dto.MaxNotifications,
        MaxReminders = dto.MaxReminders,
        ReminderIntervalHours = dto.ReminderIntervalHours,
        LinkValidityDays = dto.LinkValidityDays,
        SendoutTimeOfDay = dto.SendoutTimeOfDay,
        SendoutIntervalMinutes = dto.SendoutIntervalMinutes,
    };
}
