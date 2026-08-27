using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.AddMembership;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// Rev. 3/4 enrollment-span semantics for membership addition
/// (spec activity-group-enrollment.md FR-42/43/46/47/48/52).
/// </summary>
[TestClass]
public class ActivityGroupEnrollmentSpanTests
{
    private static AddMembershipHandler NewAdd(StudentsTestScope s) => new(
        s.ActivityGroups, s.Memberships, s.Students,
        Mock.Of<IStudentEnrollmentRepository>(),
        Mock.Of<IActivePeriodProvider>(),
        Mock.Of<IPeriodRepository>(),
        s.Cache, s.Tenants,
        NullLogger<AddMembershipHandler>.Instance);

    private static async Task<(Guid groupId, Guid studentId)> SeedAsync(StudentsTestScope s, ActivityGroup group)
    {
        s.Db.ActivityGroups.Add(group);
        var student = Student.Create("S001", "Alice", "Smith",
            DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-10)), Guid.NewGuid()).WithTenant(s.Tenants);
        s.Db.Students.Add(student);
        await s.Db.SaveChangesAsync();
        return (group.Id, student.Id);
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    // FR-47/48: OpenEnded group → membership has null PeriodId and null window.
    [TestMethod]
    public async Task Add_OpenEnded_PeriodAndWindowNull()
    {
        using var s = new StudentsTestScope("span-open-" + Guid.NewGuid());
        var (gid, sid) = await SeedAsync(s, ActivityGroup.Create("Open Club", span: EnrollmentSpan.OpenEnded));

        var id = await NewAdd(s).HandleAsync(new AddMembership(gid, sid));
        var m = await s.Memberships.GetAsync(id);

        m!.PeriodId.Should().BeNull();
        m.WindowStartDate.Should().BeNull();
        m.WindowEndDate.Should().BeNull();
        m.AutoRenew.Should().BeTrue();
    }

    // FR-44: OpenEnded with a PeriodId is a span mismatch → rejected.
    [TestMethod]
    public async Task Add_OpenEnded_WithPeriodId_Throws()
    {
        using var s = new StudentsTestScope("span-open-period-" + Guid.NewGuid());
        var (gid, sid) = await SeedAsync(s, ActivityGroup.Create("Open Club", span: EnrollmentSpan.OpenEnded));

        await FluentActions.Awaiting(() => NewAdd(s).HandleAsync(new AddMembership(gid, sid, PeriodId: Guid.NewGuid())))
            .Should().ThrowAsync<EnrollmentSpanMismatchException>();
    }

    // FR-47/52: DateRange group with an open window → membership gets the window, PeriodId null.
    [TestMethod]
    public async Task Add_DateRange_OpenWindow_SetsWindowDates()
    {
        using var s = new StudentsTestScope("span-range-open-" + Guid.NewGuid());
        var (gid, sid) = await SeedAsync(s, ActivityGroup.Create(
            "Summer Club", span: EnrollmentSpan.DateRange,
            enrollmentStartDate: Today.AddDays(-10), enrollmentEndDate: Today.AddDays(30)));

        var id = await NewAdd(s).HandleAsync(new AddMembership(gid, sid));
        var m = await s.Memberships.GetAsync(id);

        m!.PeriodId.Should().BeNull();
        m.WindowStartDate.Should().Be(Today.AddDays(-10));
        m.WindowEndDate.Should().Be(Today.AddDays(30));
    }

    // FR-52: DateRange group whose window has closed → no new enrollments.
    [TestMethod]
    public async Task Add_DateRange_ClosedWindow_Throws()
    {
        using var s = new StudentsTestScope("span-range-closed-" + Guid.NewGuid());
        var (gid, sid) = await SeedAsync(s, ActivityGroup.Create(
            "Summer Club", span: EnrollmentSpan.DateRange,
            enrollmentStartDate: Today.AddDays(-30), enrollmentEndDate: Today.AddDays(-1)));

        await FluentActions.Awaiting(() => NewAdd(s).HandleAsync(new AddMembership(gid, sid)))
            .Should().ThrowAsync<EnrollmentWindowClosedException>();
    }

    // FR-47: DateRange membership must not carry a PeriodId.
    [TestMethod]
    public async Task Add_DateRange_WithPeriodId_Throws()
    {
        using var s = new StudentsTestScope("span-range-period-" + Guid.NewGuid());
        var (gid, sid) = await SeedAsync(s, ActivityGroup.Create(
            "Summer Club", span: EnrollmentSpan.DateRange,
            enrollmentStartDate: Today.AddDays(-10), enrollmentEndDate: Today.AddDays(30)));

        await FluentActions.Awaiting(() => NewAdd(s).HandleAsync(new AddMembership(gid, sid, PeriodId: Guid.NewGuid())))
            .Should().ThrowAsync<EnrollmentSpanMismatchException>();
    }

    // FR-42: DateRange requires both window bounds.
    [TestMethod]
    public void Create_DateRange_MissingWindow_Throws()
    {
        var act = () => ActivityGroup.Create("Summer Club", span: EnrollmentSpan.DateRange);
        act.Should().Throw<ArgumentException>();
    }

    // FR-42: DateRange window must be well-ordered.
    [TestMethod]
    public void Create_DateRange_EndBeforeStart_Throws()
    {
        var act = () => ActivityGroup.Create("Summer Club",
            span: EnrollmentSpan.DateRange,
            enrollmentStartDate: Today.AddDays(10), enrollmentEndDate: Today);
        act.Should().Throw<ArgumentException>();
    }

    // FR-46: DateRange/OpenEnded capacity counts all active members of the group.
    [TestMethod]
    public async Task Add_DateRange_AtCapacity_Throws()
    {
        using var s = new StudentsTestScope("span-range-cap-" + Guid.NewGuid());
        var group = ActivityGroup.Create("Summer Club", capacity: 1,
            span: EnrollmentSpan.DateRange,
            enrollmentStartDate: Today.AddDays(-10), enrollmentEndDate: Today.AddDays(30));
        var (gid, sid) = await SeedAsync(s, group);
        await NewAdd(s).HandleAsync(new AddMembership(gid, sid));

        var s2 = Student.Create("S002", "Bob", "Jones",
            DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-10)), Guid.NewGuid()).WithTenant(s.Tenants);
        s.Db.Students.Add(s2);
        await s.Db.SaveChangesAsync();

        await FluentActions.Awaiting(() => NewAdd(s).HandleAsync(new AddMembership(gid, s2.Id)))
            .Should().ThrowAsync<GroupAtCapacityException>();
    }
}