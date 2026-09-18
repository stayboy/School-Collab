using Microsoft.EntityFrameworkCore;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.DTOs;
using SchoolCollab.Core.Data.Repositories;
using SchoolCollab.Core.Notifications;

namespace SchoolCollab.Assignments.Core.Data.Repositories;

internal sealed class AssignmentRepository(AssignmentsDbContext db)
    : RepositoryBase<Assignment, AssignmentsDbContext>(db), IAssignmentRepository
{
    // E3 (ar-19): the tenant query-filter tag. Kept as a static array (not an inline
    // collection expression) so `IgnoreQueryFilters(...)` works inside query-syntax
    // joins — a collection expression cannot appear in an EF expression tree.
    private static readonly string[] TenantFilterTag = ["Tenant"];

    public async Task<List<AssignmentSummary>> ListAsync(AssignmentStatus? status, CancellationToken ct = default)
    {
        var query = Db.Assignments.AsNoTracking();

        if (status.HasValue)
            query = query.Where(a => a.Status == status.Value);

        return await query
            .OrderByDescending(a => a.UpdatedAt)
            .Select(a => new AssignmentSummary(
                a.Id, a.Title, a.Description, a.AssignmentType, a.GradingFormat, a.TargetAudienceType,
                a.TopicId, a.GradeLevelId, a.Status, a.DueDate, a.MaxScore, a.MandatoryReview,
                a.CreatedByTeacherId, a.CreatedAt, a.UpdatedAt,
                a.AvailableFromUtc, a.ArchiveGraceDays, a.ApprovalStatus, a.ApprovedBy, a.ApprovedAt,
                // WS-A3 (spec §3.3 + §7 Q4): pass/fail threshold + attempt
                // cap projected alongside the existing lifecycle fields
                // so the contract DTOs (and the wire) see the latest draft
                // values without a separate read.
                a.PassScore, a.MaxAttempts,
                // WS-C1 (spec §7 Q1): guardian-signature snapshot.
                a.RequiresSignature,
                // WS-B2 (spec §3.4 line 70): per-difficulty counts.
                a.DifficultyEasyCount, a.DifficultyMediumCount, a.DifficultyHardCount,
                // WS-E2b / ar-17: must be projected or every published assignment reports
                // never-published and silently loses its failure surface.
                a.PublishedAt))
            .ToListAsync(ct);
    }

    /// <summary>Sanctioned cross-tenant read for the scheduled-publish
    /// sweep (WS-A2 / spec §3.5 step 8). <c>IgnoreQueryFilters(["Tenant"])</c>
    /// is the same opt-out the staged-file sweep uses — the sweep
    /// performs NO writes here, only projects id + tenant id for the
    /// per-candidate explicit-tenant dispatch.</summary>
    public Task<List<AssignmentSweepCandidate>> ListScheduledForAutoPublishAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        return Db.Assignments
            .IgnoreQueryFilters(["Tenant"])
            .AsNoTracking()
            .Where(a => a.Status == AssignmentStatus.Scheduled
                && a.AvailableFromUtc != null
                && a.AvailableFromUtc <= nowUtc)
            .Select(a => new AssignmentSweepCandidate(a.Id, a.TenantId))
            .ToListAsync(ct);
    }

    /// <summary>Sanctioned cross-tenant read for the archive sweep
    /// (WS-A2 / spec §7 Q6 — <c>DueDate + ArchiveGraceDays &lt;= now</c>).
    /// Same opt-out posture as the scheduled-publish query above.</summary>
    public Task<List<AssignmentSweepCandidate>> ListDueForArchiveAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        return Db.Assignments
            .IgnoreQueryFilters(["Tenant"])
            .AsNoTracking()
            .Where(a => (a.Status == AssignmentStatus.Published || a.Status == AssignmentStatus.Closed)
                && a.DueDate != null
                && a.DueDate!.Value.AddDays(a.ArchiveGraceDays) <= nowUtc)
            .Select(a => new AssignmentSweepCandidate(a.Id, a.TenantId))
            .ToListAsync(ct);
    }

    public void DetectChanges() => Db.ChangeTracker.DetectChanges();

    /// <summary>E3 (ar-19) — published, non-archived assignments joined to their
    /// subscribed, broadcast-enabled recipients whose ward is <b>unsigned</b>
    /// (<see cref="SignOffState.AwaitingSignature"/>) or <b>incomplete</b> (no passing
    /// submission). Completed/signed-off wards are excluded here in the read.
    /// Same <c>IgnoreQueryFilters(["Tenant"])</c> posture as the lifecycle sweeps.
    /// Each candidate carries <c>AwaitingSignatureSince</c> (the submission's
    /// awaiting-signature moment, null when never) so the sweeper applies the
    /// later-of publish / awaiting-signature anchor.</summary>
    public Task<List<AssignmentReminderSweepCandidate>> ListReminderSweepCandidatesAsync(CancellationToken ct = default)
    {
        return (
            from a in Db.Assignments.IgnoreQueryFilters(TenantFilterTag).AsNoTracking()
            from r in Db.AssignmentRecipients.IgnoreQueryFilters(TenantFilterTag).AsNoTracking()
                .Where(r => r.AssignmentId == a.Id
                    && r.SubscriptionActive
                    && r.NotifyOnBroadcast
                    // P2-2 (re-verify): ward-state gating is ward-scoped — a null-ward
                    // recipient has no ward to be incomplete and is not a reminder target.
                    && r.WardStudentId != null)
            where a.PublishedAt != null
                && a.Status != AssignmentStatus.Archived
                // P1-1: ward-state gating in the read — a ward is a reminder target iff NOT
                // complete. Complete = a guardian-signed submission OR a passing current
                // version. No submission at all = incomplete (target); awaiting-signature
                // or non-passing = incomplete (target). Inlined (no helper call) so the EF
                // expression tree translates on Npgsql.
                && !Db.AssignmentSubmissions.IgnoreQueryFilters(TenantFilterTag).Any(s =>
                    s.AssignmentId == a.Id
                    && s.StudentId == r.WardStudentId
                    && (s.SignOffState == SignOffState.Signed
                        || Db.AssignmentSubmissionVersions.IgnoreQueryFilters(TenantFilterTag).Any(v =>
                            v.SubmissionId == s.Id
                            && v.VersionNumber == s.CurrentVersionNumber
                            && v.Passed == true)))
            select new AssignmentReminderSweepCandidate(
                a.Id, a.Title, a.TenantId, a.GradeLevelId, r.Id, r.ContactId,
                r.OwnerType, r.OwnerId,
                (NotificationChannel)(int)r.Channel, r.DeepLinkToken, r.DeepLinkExpiresAt,
                a.PublishedAt!.Value, a.DueDate,
                AwaitingSignatureSince: db.AssignmentSubmissions
                    .IgnoreQueryFilters(TenantFilterTag)
                    .Where(s => s.AssignmentId == a.Id
                        && s.StudentId == r.WardStudentId
                        && s.SignOffState == SignOffState.AwaitingSignature)
                    .Select(s => (DateTimeOffset?)s.UpdatedAt)
                    .FirstOrDefault()))
            .ToListAsync(ct);
    }

    /// <summary>E3 (ar-19) — submissions of <b>published, non-archived</b> assignments
    /// awaiting a guardian signature joined to the guardian recipient for the ward
    /// (completion-to-guardian candidates).</summary>
    public Task<List<AssignmentCompletionSweepCandidate>> ListCompletionSweepCandidatesAsync(CancellationToken ct = default)
    {
        return (
            from a in Db.Assignments.IgnoreQueryFilters(TenantFilterTag).AsNoTracking()
            from s in Db.AssignmentSubmissions.IgnoreQueryFilters(TenantFilterTag).AsNoTracking()
                .Where(s => s.AssignmentId == a.Id
                    && s.SignOffState == SignOffState.AwaitingSignature)
            from r in Db.AssignmentRecipients.IgnoreQueryFilters(TenantFilterTag).AsNoTracking()
                .Where(r => r.AssignmentId == a.Id
                    && r.WardStudentId == s.StudentId
                    && r.SubscriptionActive
                    && r.NotifyOnBroadcast)
            // P2-1 (re-verify): completion nudges stay inside the published /
            // non-archived window — an archived assignment must never re-nudge a signer.
            where a.PublishedAt != null
                && a.Status != AssignmentStatus.Archived
            select new AssignmentCompletionSweepCandidate(
                a.Id, a.Title, r.TenantId, null, r.Id, r.ContactId,
                r.OwnerType, r.OwnerId,
                (NotificationChannel)(int)r.Channel, r.DeepLinkToken, r.DeepLinkExpiresAt,
                s.UpdatedAt))
            .Distinct()
            .ToListAsync(ct);
    }

    /// <summary>E3 (ar-19) — published/closed assignments past due joined to their
    /// recipients whose ward is <b>incomplete</b> (no passing / not signed-off — a ward
    /// with no submission at all is incomplete). Completed wards are excluded here in
    /// the read via the same ward-completeness gate the reminder sweep uses. Returns
    /// each candidate's recipient payload for
    /// per-candidate explicit-tenant dispatch.</summary>
    public Task<List<AssignmentOverdueSweepCandidate>> ListOverdueSweepCandidatesAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        return (
            from a in Db.Assignments.IgnoreQueryFilters(TenantFilterTag).AsNoTracking()
            from r in Db.AssignmentRecipients.IgnoreQueryFilters(TenantFilterTag).AsNoTracking()
                .Where(r => r.AssignmentId == a.Id
                    && r.SubscriptionActive
                    && r.NotifyOnBroadcast
                    // P2-2 (re-verify): ward-state gating is ward-scoped — a null-ward
                    // recipient has no ward to be incomplete and is not an overdue target.
                    && r.WardStudentId != null)
            where a.DueDate != null
                && a.DueDate <= nowUtc
                && (a.Status == AssignmentStatus.Published || a.Status == AssignmentStatus.Closed)
                // P1-1: incomplete-ward gating in the read — same NOT-complete predicate as
                // the reminder sweep: a ward with no submission at all is incomplete.
                && !Db.AssignmentSubmissions.IgnoreQueryFilters(TenantFilterTag).Any(s =>
                    s.AssignmentId == a.Id
                    && s.StudentId == r.WardStudentId
                    && (s.SignOffState == SignOffState.Signed
                        || Db.AssignmentSubmissionVersions.IgnoreQueryFilters(TenantFilterTag).Any(v =>
                            v.SubmissionId == s.Id
                            && v.VersionNumber == s.CurrentVersionNumber
                            && v.Passed == true)))
            select new AssignmentOverdueSweepCandidate(
                a.Id, a.Title, a.TenantId, a.GradeLevelId, r.Id, r.ContactId,
                r.OwnerType, r.OwnerId,
                (NotificationChannel)(int)r.Channel, r.DeepLinkToken, r.DeepLinkExpiresAt,
                a.DueDate!.Value))
            .ToListAsync(ct);
    }

    /// <summary>E3 (ar-19) — per-recipient reminder log state for the cadence + cap:
    /// the most recent non-terminal (<c>Queued</c>/<c>Sent</c>/<c>Skipped</c>)
    /// <see cref="NotificationKind.Reminder"/> timestamp and the non-terminal reminder
    /// count per recipient. <c>Skipped</c> rows count as coverage (P1-2): once a
    /// recipient's reminder is skipped it is treated as settled so the sweep does not
    /// manufacture a fresh row every pass.</summary>
    public async Task<List<RecipientReminderLogState>> ListReminderLogStateAsync(CancellationToken ct = default)
    {
        var rows = await Db.NotificationLogs
            .IgnoreQueryFilters(["Tenant"])
            .AsNoTracking()
            .Where(x => x.Kind == NotificationKind.Reminder
                && (x.DeliveryStatus == NotificationDeliveryStatus.Queued
                    || x.DeliveryStatus == NotificationDeliveryStatus.Sent
                    || x.DeliveryStatus == NotificationDeliveryStatus.Skipped))
            .GroupBy(x => x.RecipientId)
            .Select(g => new
            {
                RecipientId = g.Key,
                LastAt = g.Max(x => x.UpdatedAt),
                Count = g.Count(),
            })
            .ToListAsync(ct);

        return rows.Select(r => new RecipientReminderLogState(r.RecipientId, r.LastAt, r.Count)).ToList();
    }
}
