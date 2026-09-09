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

    /// <summary>WS-A3 (spec §3.3) — auto-scored total for this version,
    /// set at creation by the scoring path for AutoGraded /
    /// InstantGraded submissions. Null for TeacherGraded and when no
    /// auto-scorable questions are present. Decimal(5, 2) at the EF
    /// layer.</summary>
    public decimal? Score { get; private set; }

    /// <summary>WS-A3 (spec §3.3) — whether this version scored at or
    /// above the assignment's <c>PassScore</c> threshold (when set).
    /// Null when <c>PassScore</c> is absent on the assignment, when the
    /// version is TeacherGraded, or when scoring did not run.</summary>
    public bool? Passed { get; private set; }

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
        string? content,
        /// <summary>WS-A3 (spec §3.3) — auto-scored total. Null for
        /// TeacherGraded submissions and when no scoring ran (set by
        /// the submission handler before calling this factory).</summary>
        decimal? score = null,
        /// <summary>WS-A3 (spec §3.3) — pass/fail flag against the
        /// assignment's <c>PassScore</c> threshold. Null when the
        /// threshold is absent or scoring did not run.</summary>
        bool? passed = null)
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
            Score = score,
            Passed = passed,
            CreatedAt = submittedAt,
            UpdatedAt = submittedAt
        };
    }
}
