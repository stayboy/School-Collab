using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain.Events;

namespace SchoolCollab.Students.Core.Domain;

/// <summary>
/// The student↔group membership link (spec activity-group-enrollment.md
/// FR-7..16). Unlike <see cref="StudentEnrollment"/> (single-active,
/// period-bound), a student MAY hold multiple active memberships
/// simultaneously (multi-membership, FR-9). At most one <em>active</em>
/// membership per (tenant, student, group) is enforced by a partial unique
/// index filtered to <c>status = 0</c> (FR-10). Memberships use status
/// transitions (<see cref="MembershipStatus.Active"/> →
/// <see cref="MembershipStatus.Exited"/>/<see cref="MembershipStatus.Removed"/>)
/// plus <see cref="ExitedOn"/>; rows are not hard-deleted in normal operation,
/// preserving history (NFR-8).
/// Strict tenant entity (FR-15).
/// </summary>
public sealed class ActivityGroupMembership : ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion
{
    private readonly List<IDomainEvent> _domainEvents = [];

    private ActivityGroupMembership() { }

    public Guid Id { get; private set; }

    // Multi-tenancy: inherits the student's/group's tenant (global-tenant-filter.md §3.2 Strict).
    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }
    public Guid TenantId { get; private set; }

    // FK → activity_groups.id, ON DELETE RESTRICT (NFR-8/FR-6 — a group with any
    // membership row cannot be hard-deleted, only deactivated/removed).
    public Guid ActivityGroupId { get; private set; }

    // FK → students.id.
    public Guid StudentId { get; private set; }

    // Rev. 2 FR-7/10: optional period scope. Null for OpenEnded/DateRange
    // (window-scoped) spans; set to the matching typed period for
    // period-aligned spans (WholeAcademicYear/Termly/Semester — Phase 8/10).
    public Guid? PeriodId { get; private set; }

    // Rev. 4/5 FR-49: consent to auto-roll into the next window at rollover.
    // Defaults to the group's AutoRenewDefault (true). Admin-set.
    public bool AutoRenew { get; private set; }

    // Rev. 4 FR-47/48: window bounds recorded on the membership for window-scoped
    // spans. DateRange sets both to the group's current window; OpenEnded leaves
    // both null (continuous). Period-aligned spans leave these null.
    public DateOnly? WindowStartDate { get; private set; }
    public DateOnly? WindowEndDate { get; private set; }

    public DateOnly JoinedOn { get; private set; }

    // Set when the member exits or is removed (FR-14).
    public DateOnly? ExitedOn { get; private set; }

    // FR-8: Active=0, Exited=1, Removed=2. Default Active on creation.
    public MembershipStatus Status { get; private set; }

    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>
    /// Creates a new active membership (FR-7). Multi-membership is allowed
    /// (FR-9) — the entity does not constrain how many groups a student
    /// belongs to. Duplicate-active prevention (FR-10), inactive-group
    /// rejection (FR-12), deleted-student rejection (FR-11), capacity and
    /// span/window enforcement (FR-13, FR-46, FR-52) are handler/repository
    /// concerns that require cross-entity lookups; the partial unique indexes
    /// back FR-10 at the DB level. Age/gender specs are NOT applied (FR-16, AC-11).
    /// </summary>
    public static ActivityGroupMembership Create(
        Guid activityGroupId,
        Guid studentId,
        Guid? periodId = null,
        bool autoRenew = true,
        DateOnly? windowStartDate = null,
        DateOnly? windowEndDate = null,
        DateOnly? joinedOn = null)
    {
        if (activityGroupId == Guid.Empty)
            throw new ArgumentException("Activity group id is required.", nameof(activityGroupId));
        if (studentId == Guid.Empty)
            throw new ArgumentException("Student id is required.", nameof(studentId));

        var now = DateTimeOffset.UtcNow;
        var membership = new ActivityGroupMembership
        {
            Id = Guid.NewGuid(),
            ActivityGroupId = activityGroupId,
            StudentId = studentId,
            PeriodId = periodId,
            AutoRenew = autoRenew,
            WindowStartDate = windowStartDate,
            WindowEndDate = windowEndDate,
            JoinedOn = joinedOn ?? DateOnly.FromDateTime(DateTime.UtcNow),
            Status = MembershipStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };

        membership._domainEvents.Add(new ActivityGroupMemberAddedEvent(membership.Id, activityGroupId, studentId));
        return membership;
    }

    /// <summary>
    /// Records a member voluntarily exiting the group (FR-14). Sets
    /// <see cref="ExitedOn"/> and moves <see cref="Status"/> to
    /// <see cref="MembershipStatus.Exited"/>. Only an active member can exit.
    /// </summary>
    public void Exit(DateOnly? exitedOn = null)
    {
        if (Status != MembershipStatus.Active)
            throw new InvalidOperationException($"Only an active member can exit. Current status: {Status}.");

        Status = MembershipStatus.Exited;
        ExitedOn = exitedOn ?? DateOnly.FromDateTime(DateTime.UtcNow);
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new ActivityGroupMemberExitedEvent(Id, ActivityGroupId, StudentId));
    }

    /// <summary>
    /// Records an admin removing a member from the group (FR-14). Sets
    /// <see cref="ExitedOn"/> and moves <see cref="Status"/> to
    /// <see cref="MembershipStatus.Removed"/>. Only an active member can be
    /// removed.
    /// </summary>
    public void Remove(DateOnly? removedOn = null)
    {
        if (Status != MembershipStatus.Active)
            throw new InvalidOperationException($"Only an active member can be removed. Current status: {Status}.");

        Status = MembershipStatus.Removed;
        ExitedOn = removedOn ?? DateOnly.FromDateTime(DateTime.UtcNow);
        UpdatedAt = DateTimeOffset.UtcNow;
        _domainEvents.Add(new ActivityGroupMemberRemovedEvent(Id, ActivityGroupId, StudentId));
    }

    public void ClearDomainEvents() => _domainEvents.Clear();

    /// <summary>
    /// Sets the member's <see cref="AutoRenew"/> consent (Rev. 5 FR-49).
    /// Admin-set at any time while the membership is active; read at rollover.
    /// </summary>
    public void SetAutoRenew(bool autoRenew)
    {
        if (Status != MembershipStatus.Active)
            throw new InvalidOperationException($"Only an active member's AutoRenew can be changed. Current status: {Status}.");

        AutoRenew = autoRenew;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
