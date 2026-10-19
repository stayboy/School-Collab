using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Settings.Core.CQRS.NotificationPolicies.Commands.UpsertTenantNotificationPolicy;
using SchoolCollab.Settings.Core.CQRS.NotificationPolicies.Queries.GetTenantNotificationPolicy;
using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Api.Endpoints;

public static class NotificationPolicyRoutes
{
    /// <summary>
    /// Maps the per-tenant global-default notification policy at
    /// <c>/api/settings/notification-policy</c>.
    /// </summary>
    public static RouteGroupBuilder MapNotificationPolicyRoutes(this RouteGroupBuilder group)
    {
        // ── Get the tenant's global default policy (null when unset) ──
        group.MapGet("/notification-policy", async Task<Results<Ok<TenantNotificationPolicyDto>, NoContent>> (
            [FromServices] IQueryHandler<GetTenantNotificationPolicy, TenantNotificationPolicyDto?> handler,
            CancellationToken ct) =>
        {
            var policy = await handler.HandleAsync(GetTenantNotificationPolicy.Instance, ct);
            return policy is null ? TypedResults.NoContent() : TypedResults.Ok(policy);
        });

        // ── Upsert (create or replace) the tenant's global default policy ──
        group.MapPut("/notification-policy",
            async Task<Results<Ok<TenantNotificationPolicyDto>, BadRequest<MessageResponse>>> (
            [FromBody] UpsertNotificationPolicyRequest req,
            [FromServices] ICommandHandler<UpsertTenantNotificationPolicy, TenantNotificationPolicyDto> handler,
            CancellationToken ct) =>
        {
            try
            {
                var result = await handler.HandleAsync(new UpsertTenantNotificationPolicy(
                    req.PreferredChannelOrder,
                    req.BlockedChannels,
                    req.MaxNotifications,
                    req.MaxReminders,
                    req.ReminderIntervalHours,
                    req.LinkValidityDays,
                    req.SendoutTimeOfDay,
                    req.SendoutIntervalMinutes), ct);
                return TypedResults.Ok(result);
            }
            catch (ArgumentOutOfRangeException ex) { return TypedResults.BadRequest(new MessageResponse(ex.Message)); }
        });

        return group;
    }

    public sealed record UpsertNotificationPolicyRequest(
        NotificationChannel[]? PreferredChannelOrder,
        NotificationChannel[]? BlockedChannels,
        int? MaxNotifications,
        int? MaxReminders,
        int? ReminderIntervalHours,
        int? LinkValidityDays,
        TimeOnly? SendoutTimeOfDay,
        int? SendoutIntervalMinutes);
}
