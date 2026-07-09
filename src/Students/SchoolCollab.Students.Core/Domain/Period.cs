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
    public bool AllowSubjectOverrides { get; private set; }
    public Guid? NextPeriodId { get; private set; }
    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public static Period Create(
        string name,
        DateOnly startDate,
        DateOnly endDate,
        bool allowSubjectOverrides = false)
    {
        if (endDate < startDate)
            throw new ArgumentException("End date must be on or after start date.", nameof(endDate));

        var now = DateTimeOffset.UtcNow;
        var period = new Period
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            StartDate = startDate,
            EndDate = endDate,
            Status = PeriodStatus.Draft,
            AllowSubjectOverrides = allowSubjectOverrides,
            CreatedAt = now,
            UpdatedAt = now
        };

        period._domainEvents.Add(new PeriodCreatedEvent(period.Id, period.Name));
        return period;
    }

    public void Update(string name, DateOnly startDate, DateOnly endDate, bool allowSubjectOverrides)
    {
        if (Status != PeriodStatus.Draft)
            throw new InvalidOperationException("Only draft periods can be updated.");

        if (endDate < startDate)
            throw new ArgumentException("End date must be on or after start date.", nameof(endDate));

        Name = name.Trim();
        StartDate = startDate;
        EndDate = endDate;
        AllowSubjectOverrides = allowSubjectOverrides;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new PeriodUpdatedEvent(Id, Name));
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
        NextPeriodId = nextPeriodId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}