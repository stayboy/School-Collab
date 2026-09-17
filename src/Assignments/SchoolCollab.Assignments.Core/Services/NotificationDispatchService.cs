using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Services.Delivery;

namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// WS-E2 (ar-16) — store-driven delivery drain. Loads a bounded batch of due
/// (<see cref="NotificationDeliveryStatus.Queued"/>, or retryable
/// <see cref="NotificationDeliveryStatus.Failed"/> with <c>NextRetryAt &lt;= now</c>)
/// rows across tenants, then sends each one inside its own tenant context (the
/// sanctioned sweep pattern — mirror of <c>ArchiveSweeper</c>), stamping
/// <c>Sent</c> or <c>Failed</c> + <c>NextRetryAt</c> from the pure
/// <see cref="NotificationRetrySchedule"/>. Reaching the attempt cap makes the failure
/// terminal (no further retry), which is what the ar-17 failure surface lists.
/// </summary>
public sealed class NotificationDispatchService(
    AssignmentsDbContext db,
    ITenantContextAccessor tenantContextAccessor,
    IEmailSender emailSender,
    ISmsSender smsSender,
    TimeProvider timeProvider,
    ILogger<NotificationDispatchService> logger)
{
    /// <summary>Maximum rows dispatched per drain pass (bounded batch).</summary>
    public const int MaxBatchSize = 50;

    /// <summary>Dispatches every due notification in the current batch; returns the sent count.</summary>
    public async Task<int> DispatchPendingAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();

        // Candidates are read across tenants (the drain runs in the API host, outside any
        // tenant context) and each one is dispatched under its own tenant — the same
        // sanctioned cross-tenant candidate read the archive/scheduled-publish sweeps use.
        var candidates = await db.NotificationLogs
            .IgnoreQueryFilters(["Tenant"])
            .AsNoTracking()
            .Where(x => x.NextRetryAt != null
                && x.NextRetryAt <= now
                && (x.DeliveryStatus == NotificationDeliveryStatus.Queued
                    || x.DeliveryStatus == NotificationDeliveryStatus.Failed))
            .OrderBy(x => x.NextRetryAt)
            .Take(MaxBatchSize)
            .Select(x => new NotificationDispatchCandidate(x.Id, x.TenantId))
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return 0;
        }

        logger.LogInformation("Notification drain: {Count} due row(s)", candidates.Count);

        var sent = 0;
        foreach (var candidate in candidates)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                var delivered = await tenantContextAccessor.RunWithExplicitTenantAsync(
                    candidate.TenantId,
                    ct => DispatchOneAsync(candidate.NotificationLogId, ct),
                    cancellationToken);

                if (delivered)
                {
                    sent++;
                }
            }
            catch (Exception ex)
            {
                // Per-candidate isolation: one bad row must not abort the batch.
                logger.LogError(ex, "Notification dispatch failed for log {NotificationLogId}", candidate.NotificationLogId);
            }
        }

        return sent;
    }

    private async Task<bool> DispatchOneAsync(Guid notificationLogId, CancellationToken cancellationToken)
    {
        var log = await db.NotificationLogs.FirstOrDefaultAsync(x => x.Id == notificationLogId, cancellationToken);
        if (log is null)
        {
            return false;
        }

        var now = timeProvider.GetUtcNow();
        try
        {
            if (log.Channel == NotificationChannel.Email)
            {
                await emailSender.SendAsync(new EmailMessage(log.ToAddress, log.Subject, log.BodyHtml), cancellationToken);
            }
            else
            {
                await smsSender.SendAsync(new SmsMessage(log.ToAddress, log.BodyHtml), cancellationToken);
            }

            log.MarkSent(now);
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // attemptsMade = the attempt being recorded by MarkFailed below.
            var attemptsMade = log.Attempt + 1;
            var nextRetryAt = NotificationRetrySchedule.NextRetryAt(attemptsMade, now);
            log.MarkFailed(ex.Message, nextRetryAt, now);
            await db.SaveChangesAsync(cancellationToken);

            logger.LogWarning(
                "Notification {NotificationLogId} failed on attempt {Attempt} (next retry: {NextRetryAt})",
                log.Id, attemptsMade, nextRetryAt);

            return false;
        }
    }
}

/// <summary>A due notification row identified for dispatch (id + owning tenant).</summary>
public sealed record NotificationDispatchCandidate(Guid NotificationLogId, Guid TenantId);
