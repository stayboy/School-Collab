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
}
