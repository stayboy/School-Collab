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
    public DateOnly EnrolledOn { get; private set; }
    public DateOnly? ExitDate { get; private set; }
    public EnrollmentStatus Status { get; private set; }
    public PromotionOutcome? PromotionOutcome { get; private set; }
    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public static StudentEnrollment Create(
        Guid studentId,
        Guid periodId,
        Guid gradeLevelId,
        DateOnly? enrolledOn = null,
        PromotionOutcome? promotionOutcome = null)
    {
        var now = DateTimeOffset.UtcNow;
        var enrollment = new StudentEnrollment
        {
            Id = Guid.NewGuid(),
            StudentId = studentId,
            PeriodId = periodId,
            GradeLevelId = gradeLevelId,
            EnrolledOn = enrolledOn ?? DateOnly.FromDateTime(DateTime.UtcNow),
            Status = EnrollmentStatus.Active,
            PromotionOutcome = promotionOutcome,
            CreatedAt = now,
            UpdatedAt = now
        };

        enrollment._domainEvents.Add(new StudentEnrolledEvent(enrollment.Id, studentId, periodId, gradeLevelId));
        return enrollment;
    }

    public void Transfer(Guid newGradeLevelId, DateOnly? transferDate = null)
    {
        if (Status != EnrollmentStatus.Active)
            throw new InvalidOperationException("Only active enrollments can be transferred.");

        GradeLevelId = newGradeLevelId;
        Status = EnrollmentStatus.Transferred;
        ExitDate = transferDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new StudentTransferredEvent(Id, StudentId, PeriodId, newGradeLevelId));
    }

    public void Withdraw(DateOnly? exitDate = null)
    {
        if (Status != EnrollmentStatus.Active)
            throw new InvalidOperationException("Only active enrollments can be withdrawn.");

        Status = EnrollmentStatus.Withdrawn;
        ExitDate = exitDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new StudentWithdrawnEvent(Id, StudentId, PeriodId));
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}