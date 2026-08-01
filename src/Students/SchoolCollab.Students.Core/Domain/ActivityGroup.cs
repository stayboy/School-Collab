using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain.Events;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.Domain;

/// <summary>
/// An extracurricular activity group (club, sports team, debate, music, etc.)
/// — a second grouping mechanism alongside <see cref="GradeLevel"/>. Unlike
/// grade enrollment (single-active, bound to a <see cref="Period"/>), an
/// activity group has its own <see cref="ActivityGroupStatus"/> lifecycle that
/// is independent of <see cref="Period"/> (it MAY optionally be associated with
/// a period and MAY outlast it), and a student MAY be an active member of one
/// or more groups simultaneously (multi-membership).
/// Strict tenant entity (spec activity-group-enrollment.md FR-2).
/// </summary>
public sealed class ActivityGroup : ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion
{
    private readonly List<IDomainEvent> _domainEvents = [];

    private ActivityGroup() { }

    public Guid Id { get; private set; }

    // Multi-tenancy: each row belongs to a tenant (global-tenant-filter.md §3.2 Strict).
    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }
    public Guid TenantId { get; private set; }

    // FR-1: unique-within-tenant Name (<= 200). Case-insensitive uniqueness is
    // enforced by a partial unique index on (tenant_id, lower(name)) created via
    // raw SQL in the migration — EF Core cannot express the lower() expression.
    public string Name { get; private set; } = default!;

    // FR-1: optional Description (<= 2000).
    public string? Description { get; private set; }

    // FR-1: optional free-text Category (<= 100).
    public string? Category { get; private set; }

    // FR-4: optional Period association. Null = no period; absence MUST NOT
    // block creation or membership. A group MAY outlast its period (FR-3/FR-10).
    public Guid? PeriodId { get; private set; }

    // FR-1: optional integer Capacity (max members). Null = unlimited (AC-10).
    // CHECK (capacity >= 1) enforced at the entity level and in the DB.
    public int? Capacity { get; private set; }

    // FR-3: lifecycle independent of PeriodStatus. Default Active on creation.
    public ActivityGroupStatus Status { get; private set; }

    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>
    /// Creates a new <see cref="ActivityGroup"/> with <see cref="Status"/> =
    /// <see cref="ActivityGroupStatus.Active"/>. No active <see cref="Period"/>
    /// is required (FR-3, FR-4, AC-1, AC-2).
    /// </summary>
    public static ActivityGroup Create(
        string name,
        string? description = null,
        string? category = null,
        Guid? periodId = null,
        int? capacity = null)
    {
        ValidateName(name);
        ValidateCapacity(capacity);

        var now = DateTimeOffset.UtcNow;
        var group = new ActivityGroup
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim(),
            PeriodId = periodId,
            Capacity = capacity,
            Status = ActivityGroupStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };

        group._domainEvents.Add(new ActivityGroupCreatedEvent(group.Id, group.Name));
        return group;
    }

    /// <summary>
    /// Updates the group's mutable fields (FR-5, AC-25).
    /// </summary>
    public void Update(
        string name,
        string? description = null,
        string? category = null,
        Guid? periodId = null,
        int? capacity = null)
    {
        ValidateName(name);
        ValidateCapacity(capacity);

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        PeriodId = periodId;
        Capacity = capacity;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new ActivityGroupUpdatedEvent(Id, Name));
    }

    /// <summary>
    /// Suspends the group (FR-3). Only an <see cref="ActivityGroupStatus.Active"/>
    /// group can be suspended.
    /// </summary>
    public void Suspend()
    {
        if (Status != ActivityGroupStatus.Active)
            throw new InvalidOperationException($"Only an Active group can be suspended. Current status: {Status}.");

        Status = ActivityGroupStatus.Suspended;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new ActivityGroupSuspendedEvent(Id, Name));
    }

    /// <summary>
    /// Archives the group — the soft-retire path that preserves history and
    /// live assignment links (FR-3, Q3, EC-4). New membership and new assignment
    /// links are blocked for archived groups (FR-12, FR-22). A group can be
    /// archived from <see cref="ActivityGroupStatus.Active"/> or
    /// <see cref="ActivityGroupStatus.Suspended"/>, but not if already archived.
    /// </summary>
    public void Archive()
    {
        if (Status == ActivityGroupStatus.Archived)
            throw new InvalidOperationException("The group is already archived.");

        Status = ActivityGroupStatus.Archived;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new ActivityGroupArchivedEvent(Id, Name));
    }

    /// <summary>
    /// Reactivates a <see cref="ActivityGroupStatus.Suspended"/> group back to
    /// <see cref="ActivityGroupStatus.Active"/>. An archived group cannot be
    /// reactivated (archive is terminal — use a new group instead).
    /// </summary>
    public void Reactivate()
    {
        if (Status != ActivityGroupStatus.Suspended)
            throw new InvalidOperationException($"Only a Suspended group can be reactivated. Current status: {Status}.");

        Status = ActivityGroupStatus.Active;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new ActivityGroupReactivatedEvent(Id, Name));
    }

    /// <summary>
    /// Marks the group for hard deletion. Call the delete-guard check first
    /// (any membership row OR a Draft/Published assignment referencing the
    /// group → <see cref="ActivityGroupReferencedException"/>). The
    /// referential guard is enforced in the delete handler / repository, not
    /// here — mirroring <see cref="GradeLevel.Delete"/> (FR-6, AC-17, AC-18).
    /// </summary>
    public void Delete()
    {
        _domainEvents.Add(new ActivityGroupDeletedEvent(Id, Name));
    }

    public void ClearDomainEvents() => _domainEvents.Clear();

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Activity group name is required.", nameof(name));
        if (name.Trim().Length > 200)
            throw new ArgumentException("Activity group name must be 200 characters or fewer.", nameof(name));
    }

    private static void ValidateCapacity(int? capacity)
    {
        if (capacity is < 1)
            throw new ArgumentException("Activity group capacity must be at least 1.", nameof(capacity));
    }
}
