using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Core.Domain;

/// <summary>
/// One immutable row per submission / resubmission (spec §4.11). The full
/// submission version history; <see cref="AssignmentSubmission"/> points at the
/// current version number.
/// </summary>
public sealed class AssignmentSubmissionVersion : ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion
{
    private AssignmentSubmissionVersion() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }

    public Guid SubmissionId { get; private set; }
    public Guid AssignmentId { get; private set; }
    public Guid StudentId { get; private set; }
    public int VersionNumber { get; private set; }
    public SubmissionSource Source { get; private set; }
    public Guid? SubmittedByGuardianId { get; private set; }
    public DateTimeOffset SubmittedAt { get; private set; }
    public string? Content { get; private set; }

    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static AssignmentSubmissionVersion Create(
        Guid tenantId,
        Guid submissionId,
        Guid assignmentId,
        Guid studentId,
        int versionNumber,
        SubmissionSource source,
        Guid? submittedByGuardianId,
        DateTimeOffset submittedAt,
        string? content)
    {
        return new AssignmentSubmissionVersion
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SubmissionId = submissionId,
            AssignmentId = assignmentId,
            StudentId = studentId,
            VersionNumber = versionNumber,
            Source = source,
            SubmittedByGuardianId = submittedByGuardianId,
            SubmittedAt = submittedAt,
            Content = content,
            CreatedAt = submittedAt,
            UpdatedAt = submittedAt
        };
    }
}
