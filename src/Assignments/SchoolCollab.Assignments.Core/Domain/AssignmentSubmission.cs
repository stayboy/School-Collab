using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

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
}
