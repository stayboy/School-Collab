using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.AddMembership;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.ExitMembership;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.RemoveMembership;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Queries.GetGroupMembers;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Queries.GetStudentGroups;
using SchoolCollab.Students.Core.Data.Repositories;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// Handler-level tests for membership CQRS commands/queries
/// (spec activity-group-enrollment.md §3.2 FR-7..16, §7.2).
/// NOTE: the StudentsTestScope is created IN the test method (not an async
/// helper) so the AsyncLocal tenant set in its constructor flows into the
/// handler calls — mirroring PeriodOverlapInvariantTests.
/// </summary>
[TestClass]
public class MembershipHandlerTests
{
    private static AddMembershipHandler NewAdd(StudentsTestScope s) => new(
        s.ActivityGroups, s.Memberships, s.Students,
        Mock.Of<IStudentEnrollmentRepository>(),
        Mock.Of<IActivePeriodProvider>(),
        Mock.Of<IPeriodRepository>(),
        s.Cache, s.Tenants,
        NullLogger<AddMembershipHandler>.Instance);

    private static async Task<(Guid groupId, Guid studentId)> SeedAsync(StudentsTestScope s)
    {
        var group = ActivityGroup.Create("Chess Club", capacity: 2).WithTenant(s.Tenants);
        s.Db.ActivityGroups.Add(group);
        var student = Student.Create("S001", "Alice", "Smith",
            DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-10)), Guid.NewGuid()).WithTenant(s.Tenants);
        s.Db.Students.Add(student);
        await s.Db.SaveChangesAsync();
        return (group.Id, student.Id);
    }

    [TestMethod]
    public async Task Add_PersistsActiveMembership()
    {
        using var s = new StudentsTestScope("mem-" + Guid.NewGuid());
        var (gid, sid) = await SeedAsync(s);
        var id = await NewAdd(s).HandleAsync(new AddMembership(gid, sid));
        var m = await s.Memberships.GetAsync(id);
        m.Should().NotBeNull();
        m!.Status.Should().Be(MembershipStatus.Active);
    }

    [TestMethod]
    public async Task Add_InactiveGroup_Throws()
    {
        using var s = new StudentsTestScope("mem-" + Guid.NewGuid());
        var (gid, sid) = await SeedAsync(s);
        (await s.ActivityGroups.GetAsync(gid))!.Deactivate();
        await s.Db.SaveChangesAsync();
        await FluentActions.Awaiting(() => NewAdd(s).HandleAsync(new AddMembership(gid, sid)))
            .Should().ThrowAsync<InactiveGroupException>();
    }

    [TestMethod]
    public async Task Add_StudentNotFound_Throws()
    {
        using var s = new StudentsTestScope("mem-" + Guid.NewGuid());
        var (gid, _) = await SeedAsync(s);
        await FluentActions.Awaiting(() => NewAdd(s).HandleAsync(new AddMembership(gid, Guid.NewGuid())))
            .Should().ThrowAsync<StudentNotFoundException>();
    }

    [TestMethod]
    public async Task Add_DuplicateActive_Throws()
    {
        using var s = new StudentsTestScope("mem-" + Guid.NewGuid());
        var (gid, sid) = await SeedAsync(s);
        await NewAdd(s).HandleAsync(new AddMembership(gid, sid));
        await FluentActions.Awaiting(() => NewAdd(s).HandleAsync(new AddMembership(gid, sid)))
            .Should().ThrowAsync<DuplicateActiveMembershipException>();
    }

    [TestMethod]
    public async Task Add_AtCapacity_Throws()
    {
        using var s = new StudentsTestScope("mem-" + Guid.NewGuid());
        var (gid, sid) = await SeedAsync(s);
        await NewAdd(s).HandleAsync(new AddMembership(gid, sid));
        var s2 = Student.Create("S002", "Bob", "Jones",
            DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-10)), Guid.NewGuid()).WithTenant(s.Tenants);
        s.Db.Students.Add(s2);
        await s.Db.SaveChangesAsync();
        await NewAdd(s).HandleAsync(new AddMembership(gid, s2.Id));
        var s3 = Student.Create("S003", "Carol", "Lee",
            DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-10)), Guid.NewGuid()).WithTenant(s.Tenants);
        s.Db.Students.Add(s3);
        await s.Db.SaveChangesAsync();
        await FluentActions.Awaiting(() => NewAdd(s).HandleAsync(new AddMembership(gid, s3.Id)))
            .Should().ThrowAsync<GroupAtCapacityException>();
    }

    [TestMethod]
    public async Task Remove_SetsStatusRemoved()
    {
        using var s = new StudentsTestScope("mem-" + Guid.NewGuid());
        var (gid, sid) = await SeedAsync(s);
        var id = await NewAdd(s).HandleAsync(new AddMembership(gid, sid));
        var rh = new RemoveMembershipHandler(s.Memberships, s.Cache, NullLogger<RemoveMembershipHandler>.Instance);
        await rh.HandleAsync(new RemoveMembership(gid, sid));
        (await s.Memberships.GetAsync(id))!.Status.Should().Be(MembershipStatus.Removed);
    }

    [TestMethod]
    public async Task Exit_SetsStatusExited()
    {
        using var s = new StudentsTestScope("mem-" + Guid.NewGuid());
        var (gid, sid) = await SeedAsync(s);
        var id = await NewAdd(s).HandleAsync(new AddMembership(gid, sid));
        var eh = new ExitMembershipHandler(s.Memberships, s.Cache, NullLogger<ExitMembershipHandler>.Instance);
        await eh.HandleAsync(new ExitMembership(gid, sid));
        (await s.Memberships.GetAsync(id))!.Status.Should().Be(MembershipStatus.Exited);
    }

    [TestMethod]
    public async Task Remove_NotActive_Throws()
    {
        using var s = new StudentsTestScope("mem-" + Guid.NewGuid());
        var (gid, sid) = await SeedAsync(s);
        var rh = new RemoveMembershipHandler(s.Memberships, s.Cache, NullLogger<RemoveMembershipHandler>.Instance);
        await FluentActions.Awaiting(() => rh.HandleAsync(new RemoveMembership(gid, sid)))
            .Should().ThrowAsync<MembershipNotFoundException>();
    }

    [TestMethod]
    public async Task GetGroupMembers_ReturnsMembers()
    {
        using var s = new StudentsTestScope("mem-" + Guid.NewGuid());
        var (gid, sid) = await SeedAsync(s);
        await NewAdd(s).HandleAsync(new AddMembership(gid, sid));
        var dtos = await new GetGroupMembersHandler(s.Memberships).HandleAsync(new GetGroupMembers(gid));
        dtos.Should().ContainSingle();
        dtos[0].StudentName.Should().Be("Alice Smith");
    }

    [TestMethod]
    public async Task GetStudentGroups_ReturnsActiveOnly()
    {
        using var s = new StudentsTestScope("mem-" + Guid.NewGuid());
        var (gid, sid) = await SeedAsync(s);
        await NewAdd(s).HandleAsync(new AddMembership(gid, sid));
        var dtos = await new GetStudentGroupsHandler(s.Db).HandleAsync(new GetStudentGroups(sid));
        dtos.Should().ContainSingle();
        dtos[0].Name.Should().Be("Chess Club");
    }
}

