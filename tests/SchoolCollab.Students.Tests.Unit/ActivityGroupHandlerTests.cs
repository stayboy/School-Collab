using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Logging.Abstractions;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.ArchiveActivityGroup;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.CreateActivityGroup;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.DeleteActivityGroup;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.SuspendActivityGroup;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Commands.UpdateActivityGroup;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Queries.GetActivityGroupById;
using SchoolCollab.Students.Core.CQRS.ActivityGroups.Queries.ListActivityGroups;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;
using SchoolCollab.Students.Core.Services;

namespace SchoolCollab.Students.Tests.Unit;

[TestClass]
public class ActivityGroupHandlerTests
{
    internal sealed class FakeQ : IActivityGroupAssignmentQuery
    {
        public bool Throw;
        public Task<SchoolCollab.Students.Core.DTOs.AssignmentReferenceDto[]> GetReferencingAssignmentsAsync(Guid id, CancellationToken ct = default)
            => Throw ? throw new HttpRequestException("x") : Task.FromResult(Array.Empty<SchoolCollab.Students.Core.DTOs.AssignmentReferenceDto>());
    }

    private static (CreateActivityGroupHandler h, StudentsTestScope s) New()
    {
        var s = new StudentsTestScope("ag-" + Guid.NewGuid());
        return (new CreateActivityGroupHandler(s.ActivityGroups, s.Cache, s.Tenants,
            NullLogger<CreateActivityGroupHandler>.Instance), s);
    }

    [TestMethod]
    public async Task Create_PersistsGroup()
    {
        var (h, s) = New();
        var id = await h.HandleAsync(new CreateActivityGroup("Chess Club", "d", "Sp", null, 20));
        var g = await s.ActivityGroups.GetAsync(id);
        g!.Name.Should().Be("Chess Club");
        g.Status.Should().Be(ActivityGroupStatus.Active);
    }

    [TestMethod]
    public async Task Update_ChangesName()
    {
        var (h, s) = New();
        var id = await h.HandleAsync(new CreateActivityGroup("Chess Club"));
        await new UpdateActivityGroupHandler(s.ActivityGroups, s.Cache, NullLogger<UpdateActivityGroupHandler>.Instance)
            .HandleAsync(new UpdateActivityGroup(id, "Debate Club"));
        (await s.ActivityGroups.GetAsync(id))!.Name.Should().Be("Debate Club");
    }

    [TestMethod]
    public async Task Update_NotFound_Throws()
    {
        var s = new StudentsTestScope("ag-nf-" + Guid.NewGuid());
        var uh = new UpdateActivityGroupHandler(s.ActivityGroups, s.Cache, NullLogger<UpdateActivityGroupHandler>.Instance);
        await FluentActions.Awaiting(() => uh.HandleAsync(new UpdateActivityGroup(Guid.NewGuid(), "X")))
            .Should().ThrowAsync<ActivityGroupNotFoundException>();
    }

    [TestMethod]
    public async Task Archive_SetsStatus()
    {
        var (h, s) = New();
        var id = await h.HandleAsync(new CreateActivityGroup("Chess Club"));
        await new ArchiveActivityGroupHandler(s.ActivityGroups, s.Cache, NullLogger<ArchiveActivityGroupHandler>.Instance)
            .HandleAsync(new ArchiveActivityGroup(id));
        (await s.ActivityGroups.GetAsync(id))!.Status.Should().Be(ActivityGroupStatus.Archived);
    }

    [TestMethod]
    public async Task Suspend_SetsStatus()
    {
        var (h, s) = New();
        var id = await h.HandleAsync(new CreateActivityGroup("Chess Club"));
        await new SuspendActivityGroupHandler(s.ActivityGroups, s.Cache, NullLogger<SuspendActivityGroupHandler>.Instance)
            .HandleAsync(new SuspendActivityGroup(id));
        (await s.ActivityGroups.GetAsync(id))!.Status.Should().Be(ActivityGroupStatus.Suspended);
    }

    [TestMethod]
    public async Task Delete_WhenNoReferences_Succeeds()
    {
        var (h, s) = New();
        var id = await h.HandleAsync(new CreateActivityGroup("Chess Club"));
        await new DeleteActivityGroupHandler(s.ActivityGroups, new FakeQ(), s.Cache,
            NullLogger<DeleteActivityGroupHandler>.Instance).HandleAsync(new DeleteActivityGroup(id));
        (await s.ActivityGroups.GetAsync(id)).Should().BeNull();
    }

    [TestMethod]
    public async Task Delete_WhenHasMemberships_Throws()
    {
        var (h, s) = New();
        var gid = await h.HandleAsync(new CreateActivityGroup("Chess Club"));
        var stu = Student.Create("S1", "A", "B", DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-10)), Guid.NewGuid()).WithTenant(s.Tenants);
        s.Db.Students.Add(stu);
        s.Db.ActivityGroupMemberships.Add(
            ActivityGroupMembership.Create(gid, stu.Id).WithTenant(s.Tenants));
        await s.Db.SaveChangesAsync();
        var dh = new DeleteActivityGroupHandler(s.ActivityGroups, new FakeQ(), s.Cache,
            NullLogger<DeleteActivityGroupHandler>.Instance);
        await FluentActions.Awaiting(() => dh.HandleAsync(new DeleteActivityGroup(gid)))
            .Should().ThrowAsync<ActivityGroupReferencedException>();
    }

    [TestMethod]
    public async Task Delete_WhenApiUnreachable_FailsClosed()
    {
        var (h, s) = New();
        var id = await h.HandleAsync(new CreateActivityGroup("Chess Club"));
        var dh = new DeleteActivityGroupHandler(s.ActivityGroups, new FakeQ { Throw = true }, s.Cache,
            NullLogger<DeleteActivityGroupHandler>.Instance);
        await FluentActions.Awaiting(() => dh.HandleAsync(new DeleteActivityGroup(id)))
            .Should().ThrowAsync<ActivityGroupReferencedException>();
    }

    [TestMethod]
    public async Task GetById_ReturnsDtoWithCount()
    {
        var (h, s) = New();
        var id = await h.HandleAsync(new CreateActivityGroup("Chess Club", null, null, null, 5));
        var dto = await new GetActivityGroupByIdHandler(s.Db).HandleAsync(new GetActivityGroupById(id));
        dto!.ActiveMemberCount.Should().Be(0);
    }

    [TestMethod]
    public async Task List_ReturnsAllGroups()
    {
        var (h, s) = New();
        await h.HandleAsync(new CreateActivityGroup("Chess Club"));
        await h.HandleAsync(new CreateActivityGroup("Debate Club"));
        var dtos = await new ListActivityGroupsHandler(s.Db).HandleAsync(new ListActivityGroups());
        dtos.Should().HaveCount(2);
        dtos[0].Name.Should().Be("Chess Club");
    }
}

