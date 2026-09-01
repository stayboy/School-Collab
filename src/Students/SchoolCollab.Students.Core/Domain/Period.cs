using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain.Events;
using SchoolCollab.Students.Core.Domain.Exceptions;

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

    // Activation-window tolerance (period-activation-window-auto-activation.md FR-W3):
    // null = inherit the global default (Students:PeriodActivationToleranceDays); a
    // non-null value overrides it for this period's activation window.
    public int? ActivationToleranceDays { get; private set; }

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
        Guid? parentPeriodId = null,
        int? activationToleranceDays = null)
    {
        if (endDate < startDate)
            throw new ArgumentException("End date must be on or after start date.", nameof(endDate));

        ValidateActivationTolerance(activationToleranceDays);
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
            ActivationToleranceDays = activationToleranceDays,
            CreatedAt = now,
            UpdatedAt = now
        };

        period._domainEvents.Add(new PeriodCreatedEvent(period.Id, period.Name));
        return period;
    }

    /// <summary>
    /// Updates the mutable fields only. <see cref="Division"/> is immutable — set at
    /// creation (period-edit-parity-deactivate.md FR-E1) — and is never mutated here,
    /// because changing a period's framework after the fact would orphan its hierarchy.
    /// The identity/None-as-sub-period shape is enforced by the Update handler, which has
    /// repository access; the entity only enforces the date ordering here.
    /// </summary>
    public void Update(
        string name,
        DateOnly startDate,
        DateOnly endDate,
        Guid? parentPeriodId = null,
        int? activationToleranceDays = null)
    {
        if (Status != PeriodStatus.Draft)
            throw new InvalidOperationException("Only draft periods can be updated.");

        if (endDate < startDate)
            throw new ArgumentException("End date must be on or after start date.", nameof(endDate));

        ValidateActivationTolerance(activationToleranceDays);

        Name = name.Trim();
        StartDate = startDate;
        EndDate = endDate;
        ParentPeriodId = parentPeriodId;
        ActivationToleranceDays = activationToleranceDays;
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

    private static void ValidateActivationTolerance(int? activationToleranceDays)
    {
        if (activationToleranceDays is < 0)
            throw new ArgumentException(
                "Activation tolerance must be null or a non-negative number of days.",
                nameof(activationToleranceDays));
    }

    /// <summary>
    /// True when <paramref name="today"/> falls inside this period's activation window
    /// <c>[StartDate − tol, EndDate + tol]</c>, where tol = <see cref="ActivationToleranceDays"/>
    /// (per-period override) or <paramref name="defaultToleranceDays"/> (global default).
    /// Boundaries are inclusive (period-activation-window-auto-activation.md FR-W1/W2).
    /// </summary>
    public bool IsWithinActivationWindow(DateOnly today, int defaultToleranceDays)
    {
        var tolerance = ActivationToleranceDays ?? defaultToleranceDays;
        return today >= StartDate.AddDays(-tolerance) && today <= EndDate.AddDays(tolerance);
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

    /// <summary>Archives any non-Archived status (Active/Completed, or Deactivated).
    /// Deactivated → Archived is the cleanup path for a deactivated period whose record
    /// should be retired (period-edit-parity-deactivate.md FR-X5).</summary>
    public void Archive()
    {
        if (Status == PeriodStatus.Archived) return;

        Status = PeriodStatus.Archived;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Transitions Active → Deactivated (period-edit-parity-deactivate.md FR-X1).
    /// Only Active periods can be deactivated; any other status throws
    /// <see cref="PeriodNotDeactivatableException"/> (mapped to 422 in the API).
    /// Unlike <see cref="Complete"/> there is no idempotent early-return: deactivating
    /// an already-Deactivated period is a 422, not a no-op (AC-E8). Raises
    /// <see cref="PeriodDeactivatedEvent"/> (FR-X6) for observability parity.
    /// </summary>
    public void Deactivate()
    {
        if (Status != PeriodStatus.Active)
            throw new PeriodNotDeactivatableException(
                $"Period '{Name}' cannot be deactivated while its status is {Status}. " +
                "Only Active periods can be deactivated.");

        Status = PeriodStatus.Deactivated;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new PeriodDeactivatedEvent(Id, Name));
    }

    /// <summary>
    /// Guards this period as deletable (Draft-only, period-draft-delete.md FR-D2):
    /// Active/Completed/Archived periods are referenced by operational data and
    /// follow Complete -> Archive instead. Raises <see cref="PeriodDeletedEvent"/>
    /// (FR-D7) for observability parity with PeriodCompletedEvent. Unlike
    /// Activate/Complete there is no idempotent early-return: a deleted row is gone.
    /// </summary>
    public void Delete()
    {
        if (Status != PeriodStatus.Draft)
            throw new PeriodNotDeletableException(
                $"Period '{Name}' cannot be deleted while its status is {Status}. " +
                "Only Draft periods can be deleted.");

        _domainEvents.Add(new PeriodDeletedEvent(Id, Name));
    }

    /// <summary>Defensive housekeeping (FR-D6): clears a dangling NextPeriodId link
    /// left behind when the linked period was hard-deleted. No domain event — silent
    /// hygiene; the link is future-proofing only (no handler sets it today).</summary>
    public void ClearNextPeriod()
    {
        if (NextPeriodId is null) return;
        NextPeriodId = null;
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
