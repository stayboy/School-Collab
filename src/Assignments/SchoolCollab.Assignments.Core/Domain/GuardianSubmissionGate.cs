using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Core.Domain;

/// <summary>
/// Per-(assignment, student) guardian review / submission gate (spec §4.10).
/// A Primary guardian review enables the student to self-submit, or the
/// guardian submits on the student's behalf. When
/// <c>Assignment.MandatoryReview == true</c>, student self-submit requires
/// <see cref="SubmissionEnabledForStudent"/> == true.
/// </summary>
public sealed class GuardianSubmissionGate : ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion
{
    private GuardianSubmissionGate() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }

    public Guid AssignmentId { get; private set; }
    public Guid StudentId { get; private set; }
    public Guid? ReviewedByGuardianId { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    public string? ReviewComment { get; private set; }
    public bool SubmissionEnabledForStudent { get; private set; }
    public Guid? SubmittedByGuardianId { get; private set; }
    public DateTimeOffset? SubmittedByGuardianAt { get; private set; }

    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static GuardianSubmissionGate Create(Guid tenantId, Guid assignmentId, Guid studentId)
    {
        var now = DateTimeOffset.UtcNow;
        return new GuardianSubmissionGate
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AssignmentId = assignmentId,
            StudentId = studentId,
            SubmissionEnabledForStudent = false,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    /// <summary>Primary guardian reviewed the gate. <paramref name="approve"/> enables
    /// the student to self-submit (or the guardian may instead submit on behalf).</summary>
    public void Review(Guid reviewerGuardianId, bool approve, string? reviewComment)
    {
        ReviewedByGuardianId = reviewerGuardianId;
        ReviewedAt = DateTimeOffset.UtcNow;
        ReviewComment = reviewComment;
        SubmissionEnabledForStudent = approve;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Guardian submits the assignment on behalf of the student.</summary>
    public void SubmitOnBehalf(Guid submittedByGuardianId, string? reviewComment)
    {
        SubmittedByGuardianId = submittedByGuardianId;
        SubmittedByGuardianAt = DateTimeOffset.UtcNow;
        ReviewComment = reviewComment;
        SubmissionEnabledForStudent = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
