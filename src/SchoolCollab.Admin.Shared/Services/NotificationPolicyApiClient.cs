using System.Net.Http.Json;
using SchoolCollab.Core.Notifications;

namespace SchoolCollab.Admin.Shared.Services;

// Mirrors SchoolCollab.Settings.Core.DTOs.TenantNotificationPolicyDto + the PUT
// request shape from the settings notification-policy endpoint. Re-declared here
// (rather than referencing Settings.Core) to keep Admin.Shared free of a Settings
// reference, matching the CodedValues/Tenants/EntityCodeRules clients.

/// <summary>Tenant-global default notification policy (null when unset).</summary>
public sealed record TenantNotificationPolicyDto(
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

/// <summary>Upsert request (all-nullable: a null field clears to "not set").</summary>
public sealed record UpsertNotificationPolicyRequest(
    NotificationChannel[]? PreferredChannelOrder,
    NotificationChannel[]? BlockedChannels,
    int? MaxNotifications,
    int? MaxReminders,
    int? ReminderIntervalHours,
    int? LinkValidityDays,
    TimeOnly? SendoutTimeOfDay,
    int? SendoutIntervalMinutes);

/// <summary>
/// Admin client for the per-tenant global-default notification policy
/// (notification-delivery-plan.md §3/§4) at <c>/api/settings/notification-policy</c>.
/// Base address <c>https+http://settings-api</c> is configured in DI.
/// </summary>
public sealed class NotificationPolicyApiClient(HttpClient http)
{
    public async Task<TenantNotificationPolicyDto?> GetAsync(CancellationToken ct = default)
    {
        var response = await http.GetAsync("/api/settings/notification-policy", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TenantNotificationPolicyDto>(cancellationToken: ct);
    }

    public async Task UpsertAsync(UpsertNotificationPolicyRequest req, CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync("/api/settings/notification-policy", req, ct);
        response.EnsureSuccessStatusCode();
    }
}
