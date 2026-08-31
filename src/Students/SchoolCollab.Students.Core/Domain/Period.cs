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

    // Period hierarchy (plan-drop-periodtype.md). The single kind field is
    // AcademicYearDivision: None = a plain top-level academic year (no
    // sub-periods); Terms/Semesters on a top-level year (ParentPeriodId == null)
    // means the year may contain only that sub-period kind; Terms/Semesters on a
    // sub-period (ParentPeriodId != null) is the sub-period's own kind.
    public AcademicYearDivision Division { get; private set; }
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
        AcademicYearDivision division,
        Guid? parentPeriodId = null)
    {
        if (endDate < startDate)
            throw new ArgumentException("End date must be on or after start date.", nameof(endDate));

        ValidateHierarchy(division, parentPeriodId);

        var now = DateTimeOffset.UtcNow;
        var period = new Period
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            StartDate = startDate,
            EndDate = endDate,
            Status = PeriodStatus.Draft,
            Division = division,
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
        AcademicYearDivision division,
        Guid? parentPeriodId = null)
    {
        if (Status != PeriodStatus.Draft)
            throw new InvalidOperationException("Only draft periods can be updated.");

        if (endDate < startDate)
            throw new ArgumentException("End date must be on or after start date.", nameof(endDate));

        ValidateHierarchy(division, parentPeriodId);

        Name = name.Trim();
        StartDate = startDate;
        EndDate = endDate;
        Division = division;
        ParentPeriodId = parentPeriodId;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new PeriodUpdatedEvent(Id, Name));
    }

    /// <summary>
    /// Enforces the hierarchy shape (plan-drop-periodtype.md): a sub-period
    /// (ParentPeriodId set) must carry a Terms/Semesters division — a None
    /// division is reserved for top-level academic years. The referenced parent
    /// being an existing top-level year with the same division is validated by
    /// the handler (it requires a repo lookup).
    /// </summary>
    private static void ValidateHierarchy(AcademicYearDivision division, Guid? parentPeriodId)
    {
        if (parentPeriodId.HasValue && division == AcademicYearDivision.None)
            throw new ArgumentException(
                "A sub-period must have a Terms or Semesters division; None is reserved for top-level academic years.",
                nameof(division));
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
        if (ParentPeriodId is not null)
            throw new InvalidOperationException(
                "Only top-level academic-year periods can have a NextPeriodId; sub-periods are date-ordered within their year (FR-H11).");
        if (nextPeriodId == Id)
            throw new InvalidOperationException("A period cannot be its own next period.");
        NextPeriodId = nextPeriodId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}
