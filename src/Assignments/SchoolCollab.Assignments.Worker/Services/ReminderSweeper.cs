using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.DTOs;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Assignments.Core.Services.Delivery;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Worker.Services;

/// <summary>
/// E3 (ar-19) pure dispatch core for the reminder + completion-to-guardian sweeps.
/// The <see cref="IsReminderDue"/> cadence is pure and unit-testable; the dispatch
/// methods follow the sanctioned cross-tenant pattern (the Api
/// <c>ArchiveSweeper</c>/<c>NotificationDispatchService</c> mirror): candidates are
/// read cross-tenant by the repository and each one is dispatched inside its own
/// explicit tenant context with per-candidate error isolation. Reminder cadence comes
/// entirely from the stored policy fields via <see cref="INotificationPolicyResolver"/>
/// with graceful built-in defaults; a recipient is only re-reminded when the current
/// cycle has settled (no non-terminal cap reached / interval elapsed). Completion rows
/// go to the signing guardian contact and are suppressed by an existing non-terminal
/// <see cref="NotificationKind.Completion"/> row.
/// </summary>
public static class ReminderSweeper
{
    /// <summary>Built-in reminder interval fallback when no policy value is resolved (hours).</summary>
    public const int DefaultIntervalHours = 24;

    /// <summary>Built-in reminder cap fallback when no policy value is resolved.</summary>
    public const int DefaultMaxReminders = 3;

    /// <summary>
    /// Pure cadence check: a reminder is due once the interval has elapsed since the
    /// later of publish / awaiting-signature, and again each subsequent interval, until
    /// <paramref name="maxReminders"/> non-terminal reminders exist.
    /// </summary>
    public static bool IsReminderDue(
        DateTimeOffset now,
        DateTimeOffset publishedAt,
        DateTimeOffset? awaitingSignatureSince,
        DateTimeOffset? lastReminderAt,
        int sentOrQueuedReminderCount,
        int? intervalHours,
        int? maxReminders)
    {
        var interval = TimeSpan.FromHours(intervalHours.HasValue && intervalHours.Value > 0 ? intervalHours.Value : DefaultIntervalHours);
        var cap = maxReminders ?? DefaultMaxReminders;
        if (sentOrQueuedReminderCount >= cap)
        {
            return false;
        }

        // Plan §6: the anchor is the LATER of publish / awaiting-signature — the first
        // reminder is due one interval after whichever happened last.
        var anchor = awaitingSignatureSince is { } since && since > publishedAt ? since : publishedAt;
        if (now < anchor + interval)
        {
            return false;
        }

        return lastReminderAt is null || lastReminderAt.Value + interval <= now;
    }

    /// <summary>Queues due <see cref="NotificationKind.Reminder"/> rows for the given
    /// recipients, returning how many were actually queued (sent-ready). Skipped rows
    /// still persist but are not counted.</summary>
    public static async Task<int> QueueRemindersAsync(
        IReadOnlyList<AssignmentReminderSweepCandidate> candidates,
        IReadOnlyDictionary<Guid, RecipientReminderLogState> logState,
        AssignmentsDbContext db,
        ITenantContextAccessor tenantAccessor,
        INotificationPolicyResolver policyResolver,
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
                        logState.TryGetValue(candidate.RecipientId, out var state);
                        var policy = await policyResolver.ResolveEffectiveAsync(
                            candidate.TenantId, candidate.GradeLevelId, ct);
                        var now = timeProvider.GetUtcNow();

                        if (IsReminderDue(
                            now,
                            candidate.PublishedAt,
                            awaitingSignatureSince: candidate.AwaitingSignatureSince,
                            state?.LastQueuedOrSentReminderAt,
                            state?.SentOrQueuedReminderCount ?? 0,
                            policy.ReminderIntervalHours,
                            policy.MaxReminders))
                        {
                            var ok = await SweepNotificationQueuer.QueueAsync(
                                db, addressResolver, logger, timeProvider,
                                candidate.TenantId, candidate.AssignmentId, candidate.Title,
                                candidate.RecipientId, candidate.ContactId,
                                candidate.OwnerType, candidate.OwnerId, candidate.Channel,
                                candidate.DeepLinkToken, candidate.DeepLinkExpiresAt,
                                NotificationKind.Reminder, ct);
                            if (ok)
                            {
                                queued++;
                            }
                        }
                        return null;
                    },
                    cancellationToken);
            }
            catch (Exception ex)
            {
                // Per-candidate isolation: one bad recipient must not abort the sweep.
                logger.LogError(ex, "Reminder queue failed for recipient {RecipientId}", candidate.RecipientId);
            }
        }

        return queued;
    }

    /// <summary>Queues one <see cref="NotificationKind.Completion"/> row per awaiting-signature
    /// submission to the signing guardian contact, suppressed by an existing
    /// non-terminal (<c>Queued</c>/<c>Sent</c>/<c>Skipped</c> — a skip is terminal coverage)
    /// <c>Completion</c> row. Runs inside the reminder sweep pass (decision 4).</summary>
    public static async Task<int> QueueCompletionsAsync(
        IReadOnlyList<AssignmentCompletionSweepCandidate> candidates,
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
                                && x.Kind == NotificationKind.Completion
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
                            NotificationKind.Completion, ct);
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
                logger.LogError(ex, "Completion queue failed for recipient {RecipientId}", candidate.RecipientId);
            }
        }

        return queued;
    }
}
