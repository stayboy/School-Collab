using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Core.Domain;

/// <summary>
/// Teacher post-submission review / grade (spec §4.13). Created by the teacher
/// who owns the assignment (<see cref="TeacherId"/> == <c>Assignment.CreatedByTeacherId</c>)
/// and flips <see cref="AssignmentSubmission.ReviewState"/> to Reviewed / Graded.
/// Distinct from the existing <c>AssignmentReview</c> (which is keyed by
/// AssignmentId and owned by the Assignment aggregate).
/// </summary>
public sealed class SubmissionReview : ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion
{
    private SubmissionReview() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }

    public Guid SubmissionId { get; private set; }
    public Guid AssignmentId { get; private set; }
    public Guid StudentId { get; private set; }
    public Guid TeacherId { get; private set; }
    public decimal? Score { get; private set; }
    public string? Grade { get; private set; }
    public string? Comments { get; private set; }
    public DateTimeOffset ReviewedAt { get; private set; }

    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static SubmissionReview Create(
        Guid tenantId,
        Guid submissionId,
        Guid assignmentId,
        Guid studentId,
        Guid teacherId,
        decimal? score,
        string? grade,
        string? comments)
    {
        var now = DateTimeOffset.UtcNow;
        return new SubmissionReview
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SubmissionId = submissionId,
            AssignmentId = assignmentId,
            StudentId = studentId,
            TeacherId = teacherId,
            Score = score,
            Grade = grade,
            Comments = comments,
            ReviewedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Update(decimal? score, string? grade, string? comments)
    {
        Score = score;
        Grade = grade;
        Comments = comments;
        ReviewedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
