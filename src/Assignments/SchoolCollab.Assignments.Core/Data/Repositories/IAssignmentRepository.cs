using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.DTOs;

namespace SchoolCollab.Assignments.Core.Data.Repositories;

public interface IAssignmentRepository
{
    Task<Assignment?> GetAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(Assignment assignment, CancellationToken ct = default);
    Task UpdateAsync(Assignment assignment, CancellationToken ct = default);
    Task DeleteAsync(Assignment assignment, CancellationToken ct = default);
    Task<List<AssignmentSummary>> ListAsync(AssignmentStatus? status, CancellationToken ct = default);
    /// <summary>Force the EF change tracker to detect mutations on field-backed
    /// owned-type collections before SaveChanges. Required by the update
    /// handler after a full-replacement of questions/attachments (the
    /// AssignmentConfiguration uses PropertyAccessMode.Field on those
    /// navigations, and neither the InMemory provider nor post-replacement
    /// reference checks pick up the field-level list mutations automatically).</summary>
    void DetectChanges();
    /// <summary>Sanctioned cross-tenant read for the scheduled-publish
    /// sweep (WS-A2 / spec §3.5 step 8). Returns Scheduled assignments
    /// whose <c>AvailableFromUtc</c> has arrived. The dispatch wraps
    /// each candidate in an explicit-tenant context; the read itself
    /// performs no writes.</summary>
    Task<List<AssignmentSweepCandidate>> ListScheduledForAutoPublishAsync(DateTimeOffset nowUtc, CancellationToken ct = default);
    /// <summary>Sanctioned cross-tenant read for the archive sweep
    /// (WS-A2 / spec §7 Q6). Returns Published/Closed assignments whose
    /// <c>DueDate + ArchiveGraceDays</c> has passed. Same posture as the
    /// scheduled-publish query.</summary>
    Task<List<AssignmentSweepCandidate>> ListDueForArchiveAsync(DateTimeOffset nowUtc, CancellationToken ct = default);

    /// <summary>E3 (ar-19) — sanctioned cross-tenant candidate read for the reminder
    /// sweep: published, non-archived assignments joined to their subscribed,
    /// broadcast-enabled recipients whose ward is incomplete (no passing / not
    /// signed-off) or unsigned (<c>AwaitingSignature</c>). Completed wards are excluded
    /// in the read. Returns per-recipient candidates carrying the ids + policy/
    /// recipient payload for per-tenant dispatch. Default empty (existing fake
    /// repositories in tests remain valid).</summary>
    Task<List<AssignmentReminderSweepCandidate>> ListReminderSweepCandidatesAsync(CancellationToken ct = default)
        => Task.FromResult(new List<AssignmentReminderSweepCandidate>());

    /// <summary>E3 (ar-19) — sanctioned cross-tenant candidate read for the
    /// completion-to-guardian queueing: submissions sitting in
    /// <see cref="SignOffState.AwaitingSignature"/> joined to the guardian recipient
    /// for the ward. Default empty (existing fake repositories remain valid).</summary>
    Task<List<AssignmentCompletionSweepCandidate>> ListCompletionSweepCandidatesAsync(CancellationToken ct = default)
        => Task.FromResult(new List<AssignmentCompletionSweepCandidate>());

    /// <summary>E3 (ar-19) — sanctioned cross-tenant candidate read for the overdue
    /// sweep: publications closed/past due joined to their incomplete-ward recipients
    /// (completed wards excluded in the read). Default empty (existing fake
    /// repositories remain valid).</summary>
    Task<List<AssignmentOverdueSweepCandidate>> ListOverdueSweepCandidatesAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
        => Task.FromResult(new List<AssignmentOverdueSweepCandidate>());

    /// <summary>E3 (ar-19) — sanctioned cross-tenant aggregate of per-recipient reminder
    /// log state (last non-terminal <c>Queued</c>/<c>Sent</c>/<c>Skipped</c> reminder +
    /// non-terminal reminder count) for the <c>ReminderSweeper</c> cadence +
    /// <c>MaxReminders</c> cap. <c>Skipped</c> counts as coverage (P1-2). Default empty.
    /// </summary>
    Task<List<RecipientReminderLogState>> ListReminderLogStateAsync(CancellationToken ct = default)
        => Task.FromResult(new List<RecipientReminderLogState>());
}
