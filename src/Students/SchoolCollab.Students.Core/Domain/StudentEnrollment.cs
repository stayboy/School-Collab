using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain.Events;

namespace SchoolCollab.Students.Core.Domain;

public sealed class StudentEnrollment : ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion
{
    private readonly List<IDomainEvent> _domainEvents = [];

    private StudentEnrollment() { }

    public Guid Id { get; private set; }

    // Multi-tenancy: inherits the student's tenant (global-tenant-filter.md §3.2 Strict).
    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }
    public Guid TenantId { get; private set; }

    public Guid StudentId { get; private set; }
    public Guid PeriodId { get; private set; }
    public Guid GradeLevelId { get; private set; }
    public Guid? StreamCodedValueId { get; private set; }
    public DateOnly EnrolledOn { get; private set; }
    public DateOnly? ExitDate { get; private set; }
    public EnrollmentStatus Status { get; private set; }
    public string? TransferReason { get; private set; }
    public string? WithdrawReason { get; private set; }
    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public static StudentEnrollment Create(
        Guid studentId,
        Guid periodId,
        Guid gradeLevelId,
        DateOnly? enrolledOn = null,
        Guid? streamCodedValueId = null)
    {
        var now = DateTimeOffset.UtcNow;
        var enrollment = new StudentEnrollment
        {
            Id = Guid.NewGuid(),
            StudentId = studentId,
            PeriodId = periodId,
            GradeLevelId = gradeLevelId,
            StreamCodedValueId = streamCodedValueId,
            EnrolledOn = enrolledOn ?? DateOnly.FromDateTime(DateTime.UtcNow),
            Status = EnrollmentStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };

        enrollment._domainEvents.Add(new StudentEnrolledEvent(enrollment.Id, studentId, periodId, gradeLevelId, streamCodedValueId));
        return enrollment;
    }

    public void Transfer(Guid newGradeLevelId, DateOnly? transferDate = null, string? reason = null, Guid? newStreamCodedValueId = null)
    {
        if (Status != EnrollmentStatus.Active)
            throw new InvalidOperationException("Only active enrollments can be transferred.");

        GradeLevelId = newGradeLevelId;
        StreamCodedValueId = newStreamCodedValueId;
        Status = EnrollmentStatus.Transferred;
        ExitDate = transferDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        TransferReason = reason;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new StudentTransferredEvent(Id, StudentId, PeriodId, newGradeLevelId, newStreamCodedValueId));
    }

    /// <summary>
    /// Updates an ACTIVE enrollment's grade/stream in place, keeping the
    /// enrollment Active. This is the domain half of the Enroll-dialog upsert:
    /// re-submitting the enroll command for a student already enrolled in the
    /// active period is a same-period grade/stream correction, NOT a transfer
    /// (<see cref="Transfer"/> would flip Status to Transferred and stamp an
    /// ExitDate, which is wrong for this flow) and NOT a second enrollment row
    /// (the unique index ix_student_enrollments_tenant_student_period forbids
    /// it). Raises <see cref="StudentEnrollmentUpdatedEvent"/> so handlers can
    /// audit + publish the correction.
    /// </summary>
    public void UpdateGrade(Guid newGradeLevelId, Guid? newStreamCodedValueId = null)
    {
        if (Status != EnrollmentStatus.Active)
            throw new InvalidOperationException("Only active enrollments can be updated.");

        var previousGradeLevelId = GradeLevelId;
        GradeLevelId = newGradeLevelId;
        StreamCodedValueId = newStreamCodedValueId;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new StudentEnrollmentUpdatedEvent(
            Id, StudentId, PeriodId, previousGradeLevelId, newGradeLevelId, newStreamCodedValueId));
    }

    public void Withdraw(DateOnly? exitDate = null, string? reason = null)
    {
        if (Status != EnrollmentStatus.Active)
            throw new InvalidOperationException("Only active enrollments can be withdrawn.");

        Status = EnrollmentStatus.Withdrawn;
        ExitDate = exitDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        WithdrawReason = reason;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new StudentWithdrawnEvent(Id, StudentId, PeriodId));
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}