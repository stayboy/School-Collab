using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Assignments.Core.DTOs;

/// <summary>
/// A recipient-level candidate for the E3 reminder sweep. The repository reads
/// published, non-archived assignments joined to their subscribed, broadcast-enabled
/// recipients whose ward is <b>incomplete</b> (no passing submission) or <b>unsigned</b>
/// (<see cref="SignOffState.AwaitingSignature"/>) — completed/signed-off wards are
/// excluded (cross-tenant, <c>IgnoreQueryFilters(["Tenant"])</c>) and projects the
/// id + tenant id + policy/recipient payload so the sweep can dispatch each candidate
/// inside its own explicit tenant context. The payload carries everything the pure
/// <c>ReminderSweeper</c> core needs to resolve policy + cadence and (via the
/// broadcaster-style render path) queue a <see cref="NotificationKind.Reminder"/> row.
/// </summary>
public sealed record AssignmentReminderSweepCandidate(
    Guid AssignmentId,
    string Title,
    Guid TenantId,
    Guid? GradeLevelId,
    Guid RecipientId,
    Guid ContactId,
    ContactOwnerType OwnerType,
    Guid OwnerId,
    NotificationChannel Channel,
    string? DeepLinkToken,
    DateTimeOffset? DeepLinkExpiresAt,
    DateTimeOffset PublishedAt,
    DateTimeOffset? DueDate,
    /// <summary>UTC moment the ward's submission entered
    /// <see cref="SignOffState.AwaitingSignature"/>; null when the ward has never
    /// submitted for signature (drives the later-of publish / awaiting-signature
    /// reminder anchor).</summary>
    DateTimeOffset? AwaitingSignatureSince);

/// <summary>
/// A candidate for the E3 completion-to-guardian queueing, driven by a submission
/// sitting in <see cref="SignOffState.AwaitingSignature"/>. Produced by joining the
/// awaiting-signature submission to the guardian recipient for its ward.
/// </summary>
public sealed record AssignmentCompletionSweepCandidate(
    Guid AssignmentId,
    string Title,
    Guid TenantId,
    Guid? GradeLevelId,
    Guid RecipientId,
    Guid ContactId,
    ContactOwnerType OwnerType,
    Guid OwnerId,
    NotificationChannel Channel,
    string? DeepLinkToken,
    DateTimeOffset? DeepLinkExpiresAt,
    DateTimeOffset AwaitingSignatureSince);

/// <summary>
/// A recipient-level candidate for the E3 overdue sweep: a published/closed assignment
/// past <c>DueDate</c> joined to its incomplete-ward recipients.
/// </summary>
public sealed record AssignmentOverdueSweepCandidate(
    Guid AssignmentId,
    string Title,
    Guid TenantId,
    Guid? GradeLevelId,
    Guid RecipientId,
    Guid ContactId,
    ContactOwnerType OwnerType,
    Guid OwnerId,
    NotificationChannel Channel,
    string? DeepLinkToken,
    DateTimeOffset? DeepLinkExpiresAt,
    DateTimeOffset DueDate);

/// <summary>
/// Aggregate reminder-log state for one recipient, fed to the <c>ReminderSweeper</c>
/// core to enforce the cadence + <c>MaxReminders</c> cap and the non-terminal-row
/// suppression (a recipient is only re-reminded once the current cycle has settled).
/// </summary>
public sealed record RecipientReminderLogState(
    Guid RecipientId,
    DateTimeOffset? LastQueuedOrSentReminderAt,
    int SentOrQueuedReminderCount);
