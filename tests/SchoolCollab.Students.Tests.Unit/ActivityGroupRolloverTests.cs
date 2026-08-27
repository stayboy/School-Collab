using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.AddMembership;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.RolloverActivityGroup;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.SetActivityGroupNextWindow;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// Rev. 5 rollover + next-window slot
/// (spec activity-group-enrollment.md FR-49/50/51/53/54).
/// </summary>
[TestClass]
public class ActivityGroupRolloverTests
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static AddMembershipHandler NewAdd(StudentsTestScope s) => new(
        s.ActivityGroups, s.Memberships, s.Students,
        Mock.Of<IStudentEnrollmentRepository>(), Mock.Of<IActivePeriodProvider>(),
        Mock.Of<IPeriodRepository>(),
        s.Cache, s.Tenants, NullLogger<AddMembershipHandler>.Instance);

    private static RolloverActivityGroupHandler NewRollover(StudentsTestScope s) => new(
        s.ActivityGroups, s.Memberships, s.Tenants, Mock.Of<IPeriodRepository>(), s.Cache,
        NullLogger<RolloverActivityGroupHandler>.Instance);

    private static SetActivityGroupNextWindowHandler NewSetWindow(StudentsTestScope s) => new(
        s.ActivityGroups, s.Cache, NullLogger<SetActivityGroupNextWindowHandler>.Instance);

    private static async Task<Guid> SeedGroupAsync(StudentsTestScope s, ActivityGroup group)
    {
        s.Db.ActivityGroups.Add(group);
        await s.Db.SaveChangesAsync();
        return group.Id;
    }

    private static async Task<Guid> SeedStudentAsync(StudentsTestScope s, string number)
    {
        var student = Student.Create(number, "A", "B",
            DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-10)), Guid.NewGuid()).WithTenant(s.Tenants);
        s.Db.Students.Add(student);
        await s.Db.SaveChangesAsync();
        return student.Id;
    }

    // FR-53: next window start before current end → rejected.
    [TestMethod]
    public async Task SetNextWindow_StartBeforeCurrentEnd_Throws()
    {
        using var s = new StudentsTestScope("roll-next-start-" + Guid.NewGuid());
        var gid = await SeedGroupAsync(s, ActivityGroup.Create("Summer Club",
            span: EnrollmentSpan.DateRange,
            enrollmentStartDate: Today.AddDays(-10), enrollmentEndDate: Today.AddDays(30)));
        var g = (await s.ActivityGroups.GetAsync(gid))!;

        var act = () => g.SetNextWindow(Today.AddDays(10), Today.AddDays(40)); // start < end
        act.Should().Throw<ArgumentException>();
    }

    // FR-53: valid next window is accepted.
    [TestMethod]
    public async Task SetNextWindow_Valid_Accepts()
    {
        using var s = new StudentsTestScope("roll-next-ok-" + Guid.NewGuid());
        var gid = await SeedGroupAsync(s, ActivityGroup.Create("Summer Club",
            span: EnrollmentSpan.DateRange,
            enrollmentStartDate: Today.AddDays(-10), enrollmentEndDate: Today.AddDays(30)));

        await NewSetWindow(s).HandleAsync(new SetActivityGroupNextWindow(gid, Today.AddDays(31), Today.AddDays(60)));
        var g = (await s.ActivityGroups.GetAsync(gid))!;
        g.NextEnrollmentStartDate.Should().Be(Today.AddDays(31));
        g.NextEnrollmentEndDate.Should().Be(Today.AddDays(60));
    }

    // FR-50: rollover into next window — exits current, re-enrols AutoRenew, advances window.
    [TestMethod]
    public async Task Rollover_WithNextWindow_ReenrollsAutoRenew()
    {
        using var s = new StudentsTestScope("roll-advance-" + Guid.NewGuid());
        // Open window so members can join; rollover uses the explicit trigger date.
        var gid = await SeedGroupAsync(s, ActivityGroup.Create("Summer Club",
            span: EnrollmentSpan.DateRange,
            enrollmentStartDate: Today.AddDays(-10), enrollmentEndDate: Today.AddDays(30)));
        await NewSetWindow(s).HandleAsync(new SetActivityGroupNextWindow(gid, Today.AddDays(31), Today.AddDays(60)));

        var sid = await SeedStudentAsync(s, "R001");
        var sid2 = await SeedStudentAsync(s, "R002");
        var m1 = await NewAdd(s).HandleAsync(new AddMembership(gid, sid));            // AutoRenew default true
        var m2 = await NewAdd(s).HandleAsync(new AddMembership(gid, sid2, AutoRenew: false)); // opt-out

        var trigger = Today.AddDays(30); // the window end
        await NewRollover(s).HandleAsync(new RolloverActivityGroup(gid, trigger));

        // Both current memberships exited at the trigger.
        (await s.Memberships.GetAsync(m1))!.Status.Should().Be(MembershipStatus.Exited);
        (await s.Memberships.GetAsync(m1))!.ExitedOn.Should().Be(trigger);
        (await s.Memberships.GetAsync(m2))!.Status.Should().Be(MembershipStatus.Exited);

        // AutoRenew member got a new active membership in the next window; opt-out did not.
        var renewed = await s.Memberships.GetActiveAsync(sid, gid);
        renewed.Should().NotBeNull();
        renewed!.WindowStartDate.Should().Be(Today.AddDays(31));
        (await s.Memberships.GetActiveAsync(sid2, gid)).Should().BeNull();

        // Group window advanced and the next slot cleared.
        var g = (await s.ActivityGroups.GetAsync(gid))!;
        g.EnrollmentStartDate.Should().Be(Today.AddDays(31));
        g.EnrollmentEndDate.Should().Be(Today.AddDays(60));
        g.NextEnrollmentStartDate.Should().BeNull();
        g.NextEnrollmentEndDate.Should().BeNull();
    }

    // FR-51/50: no next window → all active members exited, none re-enrolled, window stays put.
    [TestMethod]
    public async Task Rollover_NoNextWindow_ExitsAll()
    {
        using var s = new StudentsTestScope("roll-none-" + Guid.NewGuid());
        var gid = await SeedGroupAsync(s, ActivityGroup.Create("Summer Club",
            span: EnrollmentSpan.DateRange,
            enrollmentStartDate: Today.AddDays(-10), enrollmentEndDate: Today.AddDays(30)));
        var sid = await SeedStudentAsync(s, "R003");
        var m1 = await NewAdd(s).HandleAsync(new AddMembership(gid, sid));

        await NewRollover(s).HandleAsync(new RolloverActivityGroup(gid, Today.AddDays(30)));

        (await s.Memberships.GetAsync(m1))!.Status.Should().Be(MembershipStatus.Exited);
        (await s.Memberships.GetActiveAsync(sid, gid)).Should().BeNull();
        (await s.ActivityGroups.GetAsync(gid))!.EnrollmentEndDate.Should().Be(Today.AddDays(30));
    }

    // FR-44/54: OpenEnded rollover is a no-op.
    [TestMethod]
    public async Task Rollover_OpenEnded_NoOp()
    {
        using var s = new StudentsTestScope("roll-open-" + Guid.NewGuid());
        var gid = await SeedGroupAsync(s, ActivityGroup.Create("Open Club", span: EnrollmentSpan.OpenEnded));
        var sid = await SeedStudentAsync(s, "R004");
        var m1 = await NewAdd(s).HandleAsync(new AddMembership(gid, sid));

        await NewRollover(s).HandleAsync(new RolloverActivityGroup(gid));

        (await s.Memberships.GetAsync(m1))!.Status.Should().Be(MembershipStatus.Active);
    }
}