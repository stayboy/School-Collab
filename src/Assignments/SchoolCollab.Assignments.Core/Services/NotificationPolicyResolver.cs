using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Students.Core.DTOs;

namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// HTTP-backed <see cref="INotificationPolicyResolver"/> (notification-delivery-plan.md
/// §3). Reads the tenant-global default from the Settings API and the optional per-grade
/// override from the Students API, then merges them with the shared
/// <see cref="EffectiveNotificationPolicyResolver"/> into the effective policy. Named
/// clients <c>settings-api</c> + <c>students-api</c> resolve through Aspire service
/// discovery (AppHost wires assignments-api / assignments-worker → settings-api +
/// students-api). Any fetch failure degrades gracefully to the built-in default (empty
/// policy), matching the "best-effort notification" posture — the publish is never
/// blocked by policy.
///
/// <para>E3 (ar-19): relocated from Assignments.Api into Assignments.Core so the same
/// resolver drives both the publish path and the Assignments.Worker reminder sweeps.
/// Assignments.Core does not reference Settings.Core (repo rule: no expanded
/// cross-context references), so the tenant-default payload is deserialized into the
/// local <see cref="TenantNotificationPolicyPayload"/> wire mirror rather than the
/// Settings DTO.</para>
///
/// <para><b>Q49 hardening (round notification-templates-b2a).</b> Both fetches carry an explicit
/// 5-second wall clock and widen to the template resolver's fail-open contract (timeout, non-JSON
/// 200, 5xx): this resolver feeds the publish filter's own <c>BlockedChannels</c>, so a Settings
/// timeout used to abort the publish instead of degrading to "per policy, nothing blocked". Genuine
/// cancellation still propagates.</para>
/// </summary>
public sealed class NotificationPolicyResolver(
    IHttpClientFactory httpClientFactory,
    ILogger<NotificationPolicyResolver> logger) : INotificationPolicyResolver
{
    private static readonly EffectiveNotificationPolicyResolver _merger = new();

    /// <summary>The cross-context read wall clock — a hung Settings/Students call must not stall the sweep.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

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
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(Timeout);

            var payload = await settings.GetFromJsonAsync<TenantNotificationPolicyPayload>(
                "/api/settings/notification-policy", timeoutCts.Token);
            return payload is null ? null : ToFields(payload);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Timed out resolving tenant notification policy; using built-in default");
            return null;
        }
        catch (OperationCanceledException)
        {
            throw; // genuine caller cancellation propagates
        }
        catch (Exception ex) when (IsDegradable(ex))
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
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(Timeout);

            var dto = await students.GetFromJsonAsync<GradeNotificationPolicyDto>(
                $"students/grade-levels/{gradeLevelId}/notification-policy", timeoutCts.Token);
            return dto is null ? null : ToFields(dto);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Timed out resolving grade notification policy for {GradeLevelId}", gradeLevelId);
            return null;
        }
        catch (OperationCanceledException)
        {
            throw; // genuine caller cancellation propagates
        }
        catch (Exception ex) when (IsDegradable(ex))
        {
            logger.LogWarning(ex, "Failed to resolve grade notification policy for {GradeLevelId}", gradeLevelId);
            return null;
        }
    }

    /// <summary>
    /// The fail-open exception set (Q49, the template resolver's contract): a transport failure, a
    /// 200 carrying a non-JSON body (<see cref="System.Text.Json.JsonException"/> or
    /// <see cref="NotSupportedException"/>) and an unset base address
    /// (<see cref="InvalidOperationException"/>) all degrade to the built-in default rather than
    /// escaping. Genuine cancellation is handled before this filter, never here.
    /// </summary>
    private static bool IsDegradable(Exception ex) => ex is HttpRequestException
        or InvalidOperationException
        or System.Text.Json.JsonException
        or NotSupportedException;

    private static NotificationPolicyFields ToFields(TenantNotificationPolicyPayload payload) => new()
    {
        PreferredChannelOrder = payload.PreferredChannelOrder,
        BlockedChannels = payload.BlockedChannels,
        MaxNotifications = payload.MaxNotifications,
        MaxReminders = payload.MaxReminders,
        ReminderIntervalHours = payload.ReminderIntervalHours,
        LinkValidityDays = payload.LinkValidityDays,
        SendoutTimeOfDay = payload.SendoutTimeOfDay,
        SendoutIntervalMinutes = payload.SendoutIntervalMinutes,
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

/// <summary>E3 (ar-19) — local wire mirror of the Settings API's tenant notification
/// policy payload. Kept inside Assignments.Core so the relocated
/// <see cref="NotificationPolicyResolver"/> does not depend on Settings.Core (no new
/// cross-context reference). Property names match the Settings DTO's JSON shape.</summary>
public sealed record TenantNotificationPolicyPayload(
    Guid Id,
    NotificationChannel[] PreferredChannelOrder,
    NotificationChannel[] BlockedChannels,
    int? MaxNotifications,
    int? MaxReminders,
    int? ReminderIntervalHours,
    int? LinkValidityDays,
    TimeOnly? SendoutTimeOfDay,
    int? SendoutIntervalMinutes,
    DateTimeOffset UpdatedAt);
