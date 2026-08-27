using FluentAssertions;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Events;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// Entity-invariant tests for <see cref="ActivityGroup"/> lifecycle
/// (spec activity-group-enrollment.md Rev. 2 §5 AC-* / §6 EC-*).
/// Tests that require a repository/handler (duplicate-name DB constraint,
/// delete-referenced-by-assignment cross-context check, capacity count) are
/// deferred to Phase 2 (membership commands/queries + APIs).
/// </summary>
[TestClass]
public class ActivityGroupTests
{
    // AC-1 (FR-1..4): create group sets all properties, IsActive=true, event raised
    [TestMethod]
    public void Create_SetsAllProperties()
    {
        var group = ActivityGroup.Create(
            "Chess Club", "After-school chess", "Sports", capacity: 20);

        group.Name.Should().Be("Chess Club");
        group.Description.Should().Be("After-school chess");
        group.Category.Should().Be("Sports");
        group.Capacity.Should().Be(20);
        group.IsActive.Should().BeTrue();
        group.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        group.UpdatedAt.Should().Be(group.CreatedAt);

        group.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<ActivityGroupCreatedEvent>()
            .Which.Name.Should().Be("Chess Club");
    }

    // AC-2 (Rev. 2 FR-4/5): create succeeds with no period (group is period-independent).
    [TestMethod]
    public void Create_DefaultsToActive()
    {
        var group = ActivityGroup.Create("Chess Club");
        group.IsActive.Should().BeTrue();
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

    // Rev. 2 FR-3: deactivate from Active succeeds
    [TestMethod]
    public void Deactivate_FromActive_TurnsOff()
    {
        var group = ActivityGroup.Create("Chess Club");
        group.Deactivate();

        group.IsActive.Should().BeFalse();
        group.DomainEvents.Should().Contain(e => e is ActivityGroupDeactivatedEvent);
    }

    // Rev. 2 FR-3: deactivate twice is a no-op (idempotent, no throw).
    [TestMethod]
    public void Deactivate_WhenAlreadyInactive_NoOp()
    {
        var group = ActivityGroup.Create("Chess Club");
        group.Deactivate();
        group.ClearDomainEvents();

        group.Deactivate();

        group.IsActive.Should().BeFalse();
        group.DomainEvents.Should().BeEmpty("deactivating an already-inactive group is a no-op");
    }

    // Rev. 2 FR-3: activate from inactive turns on and raises the event.
    [TestMethod]
    public void Activate_FromInactive_TurnsOn()
    {
        var group = ActivityGroup.Create("Chess Club");
        group.Deactivate();

        group.Activate();

        group.IsActive.Should().BeTrue();
        group.DomainEvents.Should().Contain(e => e is ActivityGroupActivatedEvent);
    }

    // Rev. 2 FR-3: activate twice is a no-op.
    [TestMethod]
    public void Activate_WhenAlreadyActive_NoOp()
    {
        var group = ActivityGroup.Create("Chess Club");
        group.Activate();

        group.IsActive.Should().BeTrue();
        group.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<ActivityGroupCreatedEvent>();
    }

    // Rev. 2 FR-3: a group is period-independent — its on/off state is decoupled
    // from any PeriodStatus. There is no PeriodId on the group.
    [TestMethod]
    public void IsActive_IndependentOfPeriod()
    {
        var group = ActivityGroup.Create("Chess Club");
        group.IsActive.Should().BeTrue();
        // Rev. 2 removed PeriodId from the group; membership is period/window-scoped.
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
