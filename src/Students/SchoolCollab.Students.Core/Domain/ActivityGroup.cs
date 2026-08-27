using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain.Events;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Core.Domain;

/// <summary>
/// An extracurricular activity group (club, sports team, debate, music, etc.)
/// — a second grouping mechanism alongside <see cref="GradeLevel"/>. Unlike
/// grade enrollment (single-active, bound to a <see cref="Period"/>), an
/// activity group's lifecycle is independent of <see cref="Period"/> (Rev. 2):
/// it has no <c>PeriodId</c> and uses a simple on/off <see cref="IsActive"/>
/// flag, and a student MAY be an active member of one or more groups
/// simultaneously (multi-membership). Membership/enrollment is period- or
/// window-scoped on the <see cref="ActivityGroupMembership"/> side (Rev. 2+).
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

    // FR-1: optional integer Capacity (max members). Null = unlimited (AC-10).
    // CHECK (capacity >= 1) enforced at the entity level and in the DB.
    public int? Capacity { get; private set; }

    // Rev. 2 FR-3/4/12: on/off switch replaces the ActivityGroupStatus enum.
    // Defaults to true on creation. Archived/Suspended collapse to IsActive=false.
    public bool IsActive { get; private set; }

    // Rev. 3/4 FR-42: enrollment span. Immutable after creation. Default OpenEnded.
    public EnrollmentSpan Span { get; private set; }

    // Rev. 4 FR-42/47/48: window bounds. Required (both) for DateRange; always
    // null for OpenEnded and period-aligned spans (derived from the linked Period).
    public DateOnly? EnrollmentStartDate { get; private set; }
    public DateOnly? EnrollmentEndDate { get; private set; }

    // Rev. 5 FR-51/53: a single advance slot for the next DateRange window.
    public DateOnly? NextEnrollmentStartDate { get; private set; }
    public DateOnly? NextEnrollmentEndDate { get; private set; }

    // Rev. 4/5 FR-49: default consent for new memberships' AutoRenew flag. Default true.
    public bool AutoRenewDefault { get; private set; }

    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>
    /// Creates a new <see cref="ActivityGroup"/> as <see cref="IsActive"/> = true.
    /// No active <see cref="Period"/> is required (Rev. 2 FR-4/5, AC-1, AC-2).
    /// The <see cref="Span"/> is immutable after creation (FR-42).
    /// </summary>
    public static ActivityGroup Create(
        string name,
        string? description = null,
        string? category = null,
        int? capacity = null,
        EnrollmentSpan span = EnrollmentSpan.OpenEnded,
        DateOnly? enrollmentStartDate = null,
        DateOnly? enrollmentEndDate = null,
        bool autoRenewDefault = true)
    {
        ValidateName(name);
        ValidateCapacity(capacity);
        ValidateSpan(span, enrollmentStartDate, enrollmentEndDate);

        var now = DateTimeOffset.UtcNow;
        var group = new ActivityGroup
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim(),
            Capacity = capacity,
            IsActive = true,
            Span = span,
            EnrollmentStartDate = enrollmentStartDate,
            EnrollmentEndDate = enrollmentEndDate,
            AutoRenewDefault = autoRenewDefault,
            CreatedAt = now,
            UpdatedAt = now
        };

        group._domainEvents.Add(new ActivityGroupCreatedEvent(group.Id, group.Name));
        return group;
    }

    /// <summary>
    /// Updates the group's mutable fields (FR-5, AC-25). The <see cref="Span"/> is
    /// immutable; for <see cref="EnrollmentSpan.DateRange"/> the admin advances the
    /// window by updating <see cref="EnrollmentStartDate"/>/<see cref="EnrollmentEndDate"/>
    /// (FR-51).
    /// </summary>
    public void Update(
        string name,
        string? description = null,
        string? category = null,
        int? capacity = null,
        DateOnly? enrollmentStartDate = null,
        DateOnly? enrollmentEndDate = null,
        bool? autoRenewDefault = null)
    {
        ValidateName(name);
        ValidateCapacity(capacity);
        ValidateSpan(Span, enrollmentStartDate, enrollmentEndDate);

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        Capacity = capacity;
        EnrollmentStartDate = enrollmentStartDate;
        EnrollmentEndDate = enrollmentEndDate;
        if (autoRenewDefault is not null)
            AutoRenewDefault = autoRenewDefault.Value;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new ActivityGroupUpdatedEvent(Id, Name));
    }

    /// <summary>
    /// Turns the group on (Rev. 2 FR-3). No-op if already active. An inactive
    /// group cannot accept new memberships (Rev. 2 FR-12).
    /// </summary>
    public void Activate()
    {
        if (IsActive)
            return;

        IsActive = true;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new ActivityGroupActivatedEvent(Id, Name));
    }

    /// <summary>
    /// Turns the group off (Rev. 2 FR-3/12). No-op if already inactive. New
    /// membership is blocked while inactive; existing memberships are
    /// preserved (a group is deactivated, not archived — history is retained).
    /// </summary>
    public void Deactivate()
    {
        if (!IsActive)
            return;

        IsActive = false;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new ActivityGroupDeactivatedEvent(Id, Name));
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

    /// <summary>
    /// Sets the next DateRange window in advance (Rev. 5 FR-51/53). At most one
    /// next window is held. Rejected if the next window's start is before the
    /// current window's end.
    /// </summary>
    public void SetNextWindow(DateOnly nextStart, DateOnly nextEnd)
    {
        if (Span != EnrollmentSpan.DateRange)
            throw new InvalidOperationException("Only a DateRange group has a next enrollment window.");
        if (nextEnd < nextStart)
            throw new ArgumentException("Next window end must be on or after next window start.");
        if (nextStart < (EnrollmentEndDate ?? DateOnly.MinValue))
            throw new ArgumentException("Next window start must be on or after the current window's end.");

        NextEnrollmentStartDate = nextStart;
        NextEnrollmentEndDate = nextEnd;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new ActivityGroupNextWindowSetEvent(Id, Name, nextStart, nextEnd));
    }

    /// <summary>
    /// Advances the group's current window to the next window and clears the
    /// advance slot (Rev. 5 FR-51). Called by rollover after members are moved.
    /// </summary>
    public void AdvanceToNextWindow()
    {
        if (!NextEnrollmentStartDate.HasValue || !NextEnrollmentEndDate.HasValue)
            throw new InvalidOperationException("No next window is defined.");

        EnrollmentStartDate = NextEnrollmentStartDate;
        EnrollmentEndDate = NextEnrollmentEndDate;
        NextEnrollmentStartDate = null;
        NextEnrollmentEndDate = null;
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new ActivityGroupRolledOverEvent(Id, Name, EnrollmentStartDate.Value, EnrollmentEndDate.Value));
    }

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

    private static void ValidateSpan(EnrollmentSpan span, DateOnly? start, DateOnly? end)
    {
        switch (span)
        {
            case EnrollmentSpan.DateRange:
                if (!start.HasValue || !end.HasValue)
                    throw new ArgumentException("A DateRange group requires EnrollmentStartDate and EnrollmentEndDate.", nameof(span));
                if (end < start)
                    throw new ArgumentException("EnrollmentEndDate must be on or after EnrollmentStartDate.", nameof(span));
                break;
            default:
                // OpenEnded and period-aligned spans carry no explicit window bounds.
                if (start.HasValue || end.HasValue)
                    throw new ArgumentException($"A {span} group must not set EnrollmentStartDate/EnrollmentEndDate.", nameof(span));
                break;
        }
    }
}