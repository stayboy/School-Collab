using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain.Events;

namespace SchoolCollab.Students.Core.Domain;

public sealed class Period : ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion
{
    private readonly List<IDomainEvent> _domainEvents = [];

    private Period() { }

    public Guid Id { get; private set; }

    // Multi-tenancy: each row belongs to a tenant (global-tenant-filter.md §3.2 Strict).
    // The "at most one current period" invariant is per-tenant (§3.5/§5.6).
    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }
    public Guid TenantId { get; private set; }

    public string Name { get; private set; } = default!;
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }
    public PeriodStatus Status { get; private set; }

    // Period hierarchy (period-hierarchy-terms-semesters.md FR-H1/H2).
    // An AcademicYear has null ParentPeriodId; a Term/Semester points at its
    // AcademicYear. Back-filled to AcademicYear for existing rows (additive).
    public PeriodType PeriodType { get; private set; } = PeriodType.AcademicYear;
    public Guid? ParentPeriodId { get; private set; }

    public Guid? NextPeriodId { get; private set; }
    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public static Period Create(
        string name,
        DateOnly startDate,
        DateOnly endDate,
        PeriodType periodType = PeriodType.AcademicYear,
        Guid? parentPeriodId = null)
    {
        if (endDate < startDate)
            throw new ArgumentException("End date must be on or after start date.", nameof(endDate));

        ValidateHierarchy(periodType, parentPeriodId);

        var now = DateTimeOffset.UtcNow;
        var period = new Period
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            StartDate = startDate,
            EndDate = endDate,
            Status = PeriodStatus.Draft,
            PeriodType = periodType,
            ParentPeriodId = parentPeriodId,
            CreatedAt = now,
            UpdatedAt = now
        };

        period._domainEvents.Add(new PeriodCreatedEvent(period.Id, period.Name));
        return period;
    }

    public void Update(
        string name,
        DateOnly startDate,
        DateOnly endDate,
        PeriodType periodType = PeriodType.AcademicYear,
        Guid? parentPeriodId = null)
    {
        if (Status != PeriodStatus.Draft)
            throw new InvalidOperationException("Only draft periods can be updated.");

        if (endDate < startDate)
            throw new ArgumentException("End date must be on or after start date.", nameof(endDate));

        ValidateHierarchy(periodType, parentPeriodId);

        Name = name.Trim();
        StartDate = startDate;
        EndDate = endDate;
        PeriodType = periodType;
        ParentPeriodId = parentPeriodId;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new PeriodUpdatedEvent(Id, Name));
    }

    /// <summary>
    /// Enforces the hierarchy shape (FR-H2): an AcademicYear must have a null
    /// parent; a Term/Semester must have a parent. The referenced parent being an
    /// existing AcademicYear is validated by the handler (it requires a repo lookup).
    /// </summary>
    private static void ValidateHierarchy(PeriodType periodType, Guid? parentPeriodId)
    {
        if (periodType == PeriodType.AcademicYear)
        {
            if (parentPeriodId.HasValue)
                throw new ArgumentException(
                    "An AcademicYear period must not have a ParentPeriodId.", nameof(parentPeriodId));
        }
        else if (!parentPeriodId.HasValue)
        {
            throw new ArgumentException(
                $"A {periodType} period must have a ParentPeriodId (its AcademicYear).", nameof(parentPeriodId));
        }
    }

    public void Activate()
    {
        if (Status == PeriodStatus.Active) return;
        if (Status != PeriodStatus.Draft)
            throw new InvalidOperationException("Only draft periods can be activated.");

        Status = PeriodStatus.Active;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new PeriodActivatedEvent(Id, Name));
    }

    public void Complete()
    {
        if (Status == PeriodStatus.Completed) return;
        if (Status != PeriodStatus.Active)
            throw new InvalidOperationException("Only active periods can be completed.");

        Status = PeriodStatus.Completed;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new PeriodCompletedEvent(Id, Name));
    }

    public void Archive()
    {
        if (Status == PeriodStatus.Archived) return;

        Status = PeriodStatus.Archived;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetNextPeriod(Guid nextPeriodId)
    {
        if (PeriodType != PeriodType.AcademicYear)
            throw new InvalidOperationException(
                "Only AcademicYear periods can have a NextPeriodId; sub-periods are date-ordered within their year (FR-H11).");
        NextPeriodId = nextPeriodId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}