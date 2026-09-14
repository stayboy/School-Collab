using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Assignments.Core.Domain.Exceptions;

namespace SchoolCollab.Assignments.Core.Domain;

/// <summary>
/// Current submission for an (assignment, student) pair (spec §4.11). One
/// current row; the full version history lives in
/// <see cref="AssignmentSubmissionVersion"/>.
/// </summary>
public sealed class AssignmentSubmission : ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion
{
    private AssignmentSubmission() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }

    public Guid AssignmentId { get; private set; }
    public Guid StudentId { get; private set; }
    public int CurrentVersionNumber { get; private set; }
    public SubmissionSource CurrentSource { get; private set; }
    public Guid? SubmittedByGuardianId { get; private set; }
    public DateTimeOffset LastSubmittedAt { get; private set; }
    public Guid? SubmissionGateId { get; private set; }
    public ReviewState ReviewState { get; private set; }

    /// <summary>WS-A3 (spec §7 Q4) — UTC moment a teacher overrode the
    /// assignment's <c>MaxAttempts</c> cap for THIS submission. Null
    /// means no override has been granted. The cap check treats a
    /// non-null value as clearing the cap (re-stamping is idempotent —
    /// the override is permanent for the submission; raising
    /// <c>MaxAttempts</c> on a draft also helps via the standard edit
    /// path).</summary>
    public DateTimeOffset? AttemptLimitOverriddenAt { get; private set; }

    /// <summary>WS-A3 (spec §7 Q4) — the id of the user who granted the
    /// attempt-cap override. Null until <see cref="OverrideAttemptLimit"/>
    /// runs. The placeholder posture mirrors
    /// <c>ReviewAssignmentRequest.TeacherId</c> (D-6).</summary>
    public Guid? AttemptLimitOverriddenBy { get; private set; }

    /// <summary>WS-C1 / spec §3.2 line 51 — the guardian sign-off stage for this
    /// (assignment, ward) submission. <c>AwaitingSignature</c> is set by the submit
    /// handler on the first recorded submission when the assignment
    /// <c>RequiresSignature</c>; <c>Signed</c> is terminal for content and blocks
    /// further submissions (C4 locking). The full DropSign chain is a projection —
    /// never stored.</summary>
    public SignOffState SignOffState { get; private set; }

    /// <summary>WS-C1 — the UTC moment the guardian signed (set by
    /// <see cref="MarkSigned"/>; sign-off completes).</summary>
    public DateTimeOffset? SignedAt { get; private set; }

    /// <summary>WS-C1/C4 — the UTC moment the sign-off was finalized (teacher
    /// action; requires <c>Signed</c>). Non-null marks the terminal locked state;
    /// further submissions are blocked on this flag too (C4).</summary>
    public DateTimeOffset? FinalizedAt { get; private set; }

    /// <summary>WS-C1 — the advisory guardian expected to sign (Q5 delegation;
    /// set by <see cref="ReassignSigner"/>). Null = any linked guardian may sign;
    /// advisory only — Primary-priority enforcement is recorded backlog.</summary>
    public Guid? ExpectedSignerGuardianId { get; private set; }

    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static AssignmentSubmission Create(
        Guid tenantId,
        Guid assignmentId,
        Guid studentId,
        Guid? submissionGateId)
    {
        var now = DateTimeOffset.UtcNow;
        return new AssignmentSubmission
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AssignmentId = assignmentId,
            StudentId = studentId,
            CurrentVersionNumber = 0,
            CurrentSource = SubmissionSource.Student,
            LastSubmittedAt = now,
            SubmissionGateId = submissionGateId,
            ReviewState = ReviewState.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    /// <summary>Record a new submission / resubmission (called by the submission engine, Phase 6).</summary>
    internal void RecordSubmission(int versionNumber, SubmissionSource source, Guid? submittedByGuardianId, DateTimeOffset submittedAt)
    {
        // WS-C1/C4 (spec §3.2 line 51 — Signed/Finalized is terminal for content):
        // a guardian-signed or finalized submission cannot be resubmitted. The
        // sign-off lock is a hard guard, not a soft flag — a later grade reopen
        // (ReviewState) does not reopen the sign-off lock.
        if (SignOffState == SignOffState.Signed || FinalizedAt is not null)
        {
            throw new SubmissionLockedException(StudentId);
        }

        CurrentVersionNumber = versionNumber;
        CurrentSource = source;
        SubmittedByGuardianId = submittedByGuardianId;
        LastSubmittedAt = submittedAt;
        if (ReviewState == ReviewState.Graded)
        {
            // Resubmission reopens the review.
            ReviewState = ReviewState.Pending;
        }
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Apply a teacher review outcome (called by the review engine, Phase 6).</summary>
    internal void ApplyReview(ReviewState reviewState)
    {
        ReviewState = reviewState;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>WS-A3 (spec §7 Q4) — a teacher cleared the
    /// assignment's <c>MaxAttempts</c> cap for this submission. The
    /// override is permanent for the submission (re-stamping is
    /// idempotent — the override sets <see cref="AttemptLimitOverriddenAt"/>
    /// once and the cap check reads <c>is null</c>; no further guard is
    /// needed). Called by <c>OverrideStudentSubmissionAttemptsCommandHandler</c>
    /// — the enable-submission command's surface area (the assignment
    /// itself remains draft-editable to raise <c>MaxAttempts</c> as
    /// well).</summary>
    internal void OverrideAttemptLimit(Guid teacherId)
    {
        if (teacherId == Guid.Empty)
            throw new ArgumentException("Teacher id is required.", nameof(teacherId));

        AttemptLimitOverriddenAt = DateTimeOffset.UtcNow;
        AttemptLimitOverriddenBy = teacherId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>WS-C1 — the ward completed the assignment; the guardian signature
    /// is now awaited. Called by the submit handler when the assignment
    /// <c>RequiresSignature</c>. Only from <c>None</c> (setting it on a submission
    /// already awaiting or signed is a state violation, not a no-op — a signed
    /// submission must never silently re-enter the awaiting stage).</summary>
    internal void MarkAwaitingSignature()
    {
        if (SignOffState is not SignOffState.None)
        {
            throw new SubmissionSignOffStateException(
                $"Cannot await a guardian signature from sign-off state {SignOffState}.");
        }

        SignOffState = SignOffState.AwaitingSignature;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>WS-C1 — the guardian signed. Records the UTC moment and moves the
    /// stage to <c>Signed</c>. Only from <c>AwaitingSignature</c>; a second sign is
    /// rejected here (state machine) AND the handler checks
    /// <see cref="SubmissionAlreadySignedException"/> before writing any event row
    /// (spec §6 NFR line 115).</summary>
    internal void MarkSigned()
    {
        if (SignOffState is not SignOffState.AwaitingSignature)
        {
            throw new SubmissionSignOffStateException(
                $"A guardian signature can only be recorded from sign-off state AwaitingSignature, was {SignOffState}.");
        }

        SignOffState = SignOffState.Signed;
        SignedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>WS-C1/C4 — a teacher finalized the sign-off. Requires
    /// <c>Signed</c>; stamps <c>FinalizedAt</c> (the terminal locked marker). The
    /// SubmissionReview grade-bridge is a v1 no-op (ar-6 owns the review flow).</summary>
    internal void FinalizeSignOff()
    {
        if (SignOffState is not SignOffState.Signed)
        {
            throw new SubmissionSignOffStateException(
                $"Only signed submissions can be finalized, sign-off state was {SignOffState}.");
        }

        FinalizedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>WS-C1 (Q5) — reassign the advisory expected signer. Requires
    /// <c>AwaitingSignature</c>; validates the guardian link at the handler
    /// (cross-context), this method only records the assignment. Advisory only —
    /// Primary-priority signer enforcement is recorded backlog.</summary>
    internal void ReassignSigner(Guid guardianId)
    {
        if (guardianId == Guid.Empty)
            throw new ArgumentException("Guardian id is required.", nameof(guardianId));
        if (SignOffState is not SignOffState.AwaitingSignature)
        {
            throw new SubmissionSignOffStateException(
                $"A signer can only be reassigned while awaiting signature, sign-off state was {SignOffState}.");
        }

        ExpectedSignerGuardianId = guardianId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
