using FluentAssertions;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Events;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// Entity-invariant tests for <see cref="ActivityGroupMembership"/>
/// (spec activity-group-enrollment.md §5 AC-* / §6 EC-*).
/// Tests that require a repository/handler (duplicate-active DB constraint,
/// deleted-student/archived-group lookups, capacity count, concurrency) are
/// deferred to Phase 2 (membership commands/queries + APIs).
/// </summary>
[TestClass]
public class ActivityGroupMembershipTests
{
    // FR-7: create sets properties, Status=Active, event raised
    [TestMethod]
    public void Create_SetsProperties()
    {
        var groupId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var joinedOn = new DateOnly(2026, 8, 1);

        var membership = ActivityGroupMembership.Create(groupId, studentId, joinedOn);

        membership.ActivityGroupId.Should().Be(groupId);
        membership.StudentId.Should().Be(studentId);
        membership.JoinedOn.Should().Be(joinedOn);
        membership.ExitedOn.Should().BeNull();
        membership.Status.Should().Be(MembershipStatus.Active);
        membership.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));

        membership.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<ActivityGroupMemberAddedEvent>();
    }

    // FR-7: defaults JoinedOn to today when not supplied
    [TestMethod]
    public void Create_DefaultsJoinedOnToToday()
    {
        var membership = ActivityGroupMembership.Create(Guid.NewGuid(), Guid.NewGuid());
        membership.JoinedOn.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow));
    }

    // FR-7: empty group id rejected
    [TestMethod]
    public void Create_EmptyGroupId_Throws()
    {
        var act = () => ActivityGroupMembership.Create(Guid.Empty, Guid.NewGuid());
        act.Should().Throw<ArgumentException>();
    }

    // FR-7: empty student id rejected
    [TestMethod]
    public void Create_EmptyStudentId_Throws()
    {
        var act = () => ActivityGroupMembership.Create(Guid.NewGuid(), Guid.Empty);
        act.Should().Throw<ArgumentException>();
    }

    // AC-4 (FR-7, FR-9): multi-membership allowed — the entity imposes no
    // single-active rule. A student can hold active memberships in multiple
    // groups simultaneously (the opposite of grade enrollment).
    [TestMethod]
    public void StudentInMultipleGroups_Allowed()
    {
        var studentId = Guid.NewGuid();
        var g1 = Guid.NewGuid();
        var g2 = Guid.NewGuid();

        var m1 = ActivityGroupMembership.Create(g1, studentId);
        var m2 = ActivityGroupMembership.Create(g2, studentId);

        m1.Status.Should().Be(MembershipStatus.Active);
        m2.Status.Should().Be(MembershipStatus.Active);
    }

    // AC-6 (FR-8, FR-10, FR-14): rejoin after exit — a new Active membership
    // can be created after the prior one is Exited (the entity allows it; the
    // DB partial unique index filtered to status=0 backs this at the DB level).
    [TestMethod]
    public void RejoinAfterExit_Succeeds()
    {
        var groupId = Guid.NewGuid();
        var studentId = Guid.NewGuid();

        var oldMembership = ActivityGroupMembership.Create(groupId, studentId);
        oldMembership.Exit();

        var newMembership = ActivityGroupMembership.Create(groupId, studentId);

        oldMembership.Status.Should().Be(MembershipStatus.Exited);
        newMembership.Status.Should().Be(MembershipStatus.Active);
    }
    // FR-14: exit from Active succeeds, sets ExitedOn, raises event
    [TestMethod]
    public void Exit_FromActive_Succeeds()
    {
        var membership = ActivityGroupMembership.Create(Guid.NewGuid(), Guid.NewGuid());
        membership.ClearDomainEvents();
        var exitDate = new DateOnly(2026, 12, 1);

        membership.Exit(exitDate);

        membership.Status.Should().Be(MembershipStatus.Exited);
        membership.ExitedOn.Should().Be(exitDate);
        membership.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<ActivityGroupMemberExitedEvent>();
    }

    // FR-14: exit from non-Active throws
    [TestMethod]
    public void Exit_FromExited_Throws()
    {
        var membership = ActivityGroupMembership.Create(Guid.NewGuid(), Guid.NewGuid());
        membership.Exit();

        var act = () => membership.Exit();
        act.Should().Throw<InvalidOperationException>();
    }

    // FR-14: remove from Active succeeds, sets ExitedOn, raises event
    [TestMethod]
    public void Remove_FromActive_Succeeds()
    {
        var membership = ActivityGroupMembership.Create(Guid.NewGuid(), Guid.NewGuid());
        membership.ClearDomainEvents();

        membership.Remove();

        membership.Status.Should().Be(MembershipStatus.Removed);
        membership.ExitedOn.Should().NotBeNull();
        membership.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<ActivityGroupMemberRemovedEvent>();
    }

    // FR-14: remove from non-Active throws
    [TestMethod]
    public void Remove_FromExited_Throws()
    {
        var membership = ActivityGroupMembership.Create(Guid.NewGuid(), Guid.NewGuid());
        membership.Exit();

        var act = () => membership.Remove();
        act.Should().Throw<InvalidOperationException>();
    }

    // AC-11 (FR-16): the Create factory accepts no age/gender parameters —
    // those specs are grade-enrollment-only and structurally cannot apply.
    [TestMethod]
    public void NoAgeGenderCheck_FactoryAcceptsNoDemographicParams()
    {
        // The only parameters are group id, student id, and optional joined date.
        // There is no age/gender validation path — confirmed by the fact that
        // Create succeeds for any valid Guid pair without demographic input.
        var membership = ActivityGroupMembership.Create(Guid.NewGuid(), Guid.NewGuid());
        membership.Status.Should().Be(MembershipStatus.Active);
    }

    // ── Phase 2 deferred tests (require repository/handler/DB) ──
    // AC-5  (duplicate active)       — DB partial unique index (HasFilter status=0).
    // AC-7  (deleted student)        — handler looks up Student.IsDeleted.
    // AC-8  (archived group)         — handler looks up ActivityGroup.Status.
    // AC-9  (capacity exceeded)      — handler counts active members via repo.
    // AC-10 (null capacity unlimited)— handler capacity check with null = skip.
    // AC-23 (concurrency / row ver)  — needs PostgreSQL xmin (in-memory can't).
    // EC-2/3/5 (concurrency races)   — need real DB + transactional handler.
}

