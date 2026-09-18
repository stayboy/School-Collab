using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.DTOs;
using SchoolCollab.Assignments.Core.Services.Delivery;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Worker.Services;

/// <summary>
/// E3 (ar-19) pure dispatch core for the overdue sweep. Queues one
/// <see cref="NotificationKind.Overdue"/> row per not-yet-notified recipient contact of a
/// published/closed assignment past due, suppressed by an existing non-terminal
/// (<c>Queued</c>/<c>Sent</c>/<c>Skipped</c> — a skip is terminal coverage)
/// <c>Overdue</c> row. Uses the sanctioned cross-tenant candidate-read + per-candidate
/// explicit-tenant dispatch pattern.
/// </summary>
public static class OverdueSweeper
{
    /// <summary>Queues <see cref="NotificationKind.Overdue"/> rows for the given
    /// candidates, returning how many were actually queued.</summary>
    public static async Task<int> QueueOverdueAsync(
        IReadOnlyList<AssignmentOverdueSweepCandidate> candidates,
        AssignmentsDbContext db,
        ITenantContextAccessor tenantAccessor,
        IContactAddressResolver addressResolver,
        TimeProvider timeProvider,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var queued = 0;
        foreach (var candidate in candidates)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await tenantAccessor.RunWithExplicitTenantAsync<object?>(
                    candidate.TenantId,
                    async ct =>
                    {
                        var covered = await db.NotificationLogs.AnyAsync(
                            x => x.TenantId == candidate.TenantId
                                && x.AssignmentId == candidate.AssignmentId
                                && x.RecipientId == candidate.RecipientId
                                && x.Kind == NotificationKind.Overdue
                                && (x.DeliveryStatus == NotificationDeliveryStatus.Queued
                                    || x.DeliveryStatus == NotificationDeliveryStatus.Sent
                                    || x.DeliveryStatus == NotificationDeliveryStatus.Skipped),
                            ct);
                        if (covered)
                        {
                            return null;
                        }

                        var ok = await SweepNotificationQueuer.QueueAsync(
                            db, addressResolver, logger, timeProvider,
                            candidate.TenantId, candidate.AssignmentId, candidate.Title,
                            candidate.RecipientId, candidate.ContactId,
                            candidate.OwnerType, candidate.OwnerId, candidate.Channel,
                            candidate.DeepLinkToken, candidate.DeepLinkExpiresAt,
                            NotificationKind.Overdue, ct);
                        if (ok)
                        {
                            queued++;
                        }
                        return null;
                    },
                    cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Overdue queue failed for recipient {RecipientId}", candidate.RecipientId);
            }
        }

        return queued;
    }
}
