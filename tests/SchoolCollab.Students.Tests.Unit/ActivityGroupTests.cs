using FluentAssertions;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Events;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// Entity-invariant tests for <see cref="ActivityGroup"/> lifecycle
/// (spec activity-group-enrollment.md §5 AC-* / §6 EC-*).
/// Tests that require a repository/handler (duplicate-name DB constraint,
/// delete-referenced-by-assignment cross-context check, capacity count) are
/// deferred to Phase 2 (membership commands/queries + APIs).
/// </summary>
[TestClass]
public class ActivityGroupTests
{
    // AC-1 (FR-1..4): create group sets all properties, Status=Active, event raised
    [TestMethod]
    public void Create_SetsAllProperties()
    {
        var periodId = Guid.NewGuid();

        var group = ActivityGroup.Create(
            "Chess Club", "After-school chess", "Sports",
            periodId, capacity: 20);

        group.Name.Should().Be("Chess Club");
        group.Description.Should().Be("After-school chess");
        group.Category.Should().Be("Sports");
        group.PeriodId.Should().Be(periodId);
        group.Capacity.Should().Be(20);
        group.Status.Should().Be(ActivityGroupStatus.Active);
        group.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        group.UpdatedAt.Should().Be(group.CreatedAt);

        group.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<ActivityGroupCreatedEvent>()
            .Which.Name.Should().Be("Chess Club");
    }

    // AC-2 (FR-3, FR-4): create without a period succeeds
    [TestMethod]
    public void Create_WithoutPeriod_Succeeds()
    {
        var group = ActivityGroup.Create("Chess Club");
        group.PeriodId.Should().BeNull();
        group.Status.Should().Be(ActivityGroupStatus.Active);
    }

    // FR-1: empty/whitespace name rejected
    [TestMethod]
    public void Create_EmptyName_Throws()
    {
        var act = () => ActivityGroup.Create("   ");
        act.Should().Throw<ArgumentException>();
    }

    // FR-1: capacity < 1 rejected
    [TestMethod]
    public void Create_CapacityLessThanOne_Throws()
    {
        var act = () => ActivityGroup.Create("Chess Club", capacity: 0);
        act.Should().Throw<ArgumentException>();
    }

    // AC-10 (FR-13): null capacity = unlimited (no exception)
    [TestMethod]
    public void Create_NullCapacity_Allowed()
    {
        var group = ActivityGroup.Create("Chess Club", capacity: null);
        group.Capacity.Should().BeNull();
    }

    // AC-25 (FR-5): update changes name and capacity, bumps UpdatedAt, raises event
    [TestMethod]
    public void Update_ChangesNameAndCapacity()
    {
        var group = ActivityGroup.Create("Chess Club", capacity: 20);
        group.ClearDomainEvents();
        var originalUpdatedAt = group.UpdatedAt;

        group.Update("Chess Club Advanced", capacity: 30);

        group.Name.Should().Be("Chess Club Advanced");
        group.Capacity.Should().Be(30);
        group.UpdatedAt.Should().BeAfter(originalUpdatedAt);
        group.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<ActivityGroupUpdatedEvent>();
    }

    // FR-3: suspend from Active succeeds
    [TestMethod]
    public void Suspend_FromActive_Succeeds()
    {
        var group = ActivityGroup.Create("Chess Club");
        group.Suspend();

        group.Status.Should().Be(ActivityGroupStatus.Suspended);
        group.DomainEvents.Should().Contain(e => e is ActivityGroupSuspendedEvent);
    }

    // FR-3: suspend from Archived throws
    [TestMethod]
    public void Suspend_FromArchived_Throws()
    {
        var group = ActivityGroup.Create("Chess Club");
        group.Archive();

        var act = () => group.Suspend();
        act.Should().Throw<InvalidOperationException>();
    }
    // FR-3: archive from Active succeeds
    [TestMethod]
    public void Archive_FromActive_Succeeds()
    {
        var group = ActivityGroup.Create("Chess Club");
        group.Archive();

        group.Status.Should().Be(ActivityGroupStatus.Archived);
        group.DomainEvents.Should().Contain(e => e is ActivityGroupArchivedEvent);
    }

    // EC-4 (FR-3): archive is the soft-retire path — at the entity level it
    // always succeeds from Active/Suspended regardless of assignment links
    // (the cross-context assignment check is a Phase 2 handler concern).
    [TestMethod]
    public void Archive_CanBeCalledOnSuspendedGroup()
    {
        var group = ActivityGroup.Create("Chess Club");
        group.Suspend();
        group.Archive();

        group.Status.Should().Be(ActivityGroupStatus.Archived);
    }

    // FR-3: archive twice throws
    [TestMethod]
    public void Archive_AlreadyArchived_Throws()
    {
        var group = ActivityGroup.Create("Chess Club");
        group.Archive();

        var act = () => group.Archive();
        act.Should().Throw<InvalidOperationException>();
    }

    // FR-3: reactivate from Suspended succeeds
    [TestMethod]
    public void Reactivate_FromSuspended_Succeeds()
    {
        var group = ActivityGroup.Create("Chess Club");
        group.Suspend();
        group.Reactivate();

        group.Status.Should().Be(ActivityGroupStatus.Active);
        group.DomainEvents.Should().Contain(e => e is ActivityGroupReactivatedEvent);
    }

    // FR-3: reactivate from Archived throws (archive is terminal)
    [TestMethod]
    public void Reactivate_FromArchived_Throws()
    {
        var group = ActivityGroup.Create("Chess Club");
        group.Archive();

        var act = () => group.Reactivate();
        act.Should().Throw<InvalidOperationException>();
    }

    // AC-19 (FR-3, FR-4, FR-10): group status is independent of period — the
    // entity has no coupling to PeriodStatus; it remains Active regardless.
    [TestMethod]
    public void Status_RemainsActive_IndependentOfPeriod()
    {
        var periodId = Guid.NewGuid();
        var group = ActivityGroup.Create("Chess Club", periodId: periodId);

        // The group does not react to period status changes at the entity level.
        // Periods may complete/archive; the group stays Active and membership
        // operations continue to succeed (the group outlasts the period).
        group.Status.Should().Be(ActivityGroupStatus.Active);
        group.PeriodId.Should().Be(periodId);
    }

    // FR-6: Delete raises the deleted event (referential guard is handler-level)
    [TestMethod]
    public void Delete_RaisesDeletedEvent()
    {
        var group = ActivityGroup.Create("Chess Club");
        group.Delete();

        group.DomainEvents.Should().Contain(e => e is ActivityGroupDeletedEvent);
    }

    // ── Phase 2 deferred tests (require repository/handler/cross-context) ──
    // AC-3 (duplicate name) — enforced by the DB unique index (lower(name));
    //   needs a real PostgreSQL instance or a repository-level guard.
    // AC-17 (delete referenced by assignment) — needs the cross-context
    //   IActivityGroupAssignmentQuery port (Phase 2, step 2.2).
    // AC-18 (delete with membership history) — needs the repository
    //   HasAnyMembershipAsync check in the delete handler (Phase 2).
}

