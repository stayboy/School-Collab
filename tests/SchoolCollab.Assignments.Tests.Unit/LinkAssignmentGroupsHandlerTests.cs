using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.LinkAssignmentGroups;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.DTOs;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// R2-7 (D-8.2): <see cref="LinkAssignmentGroupsHandler"/> is a thin adapter over
/// <see cref="Assignment.SetTargets"/> — it preserves non-group targets, swaps the group
/// rows for the posted set, lets the aggregate re-derive <see cref="Assignment.TargetAudienceType"/>
/// and rejects newly-added archived groups, while still allowing a persisted archived id to
/// re-save. The two sequential saves (target rows + legacy link table) are wrapped in one EF
/// Core transaction.
/// </summary>
[TestClass]
public class LinkAssignmentGroupsHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000010");
    private static readonly Guid GradeA = Guid.Parse("22222222-2222-2222-2222-22222222222a");
    private static readonly Guid Group1 = Guid.Parse("77777777-7777-7777-7777-777777777771");
    private static readonly Guid Group2 = Guid.Parse("77777777-7777-7777-7777-777777777772");
    private static readonly Guid Group3 = Guid.Parse("77777777-7777-7777-7777-777777777773");

    private static Assignment NewAssignment() =>
        Assignment.Create("Math", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, GradeA, null, null, TeacherId)
            .WithTenant(TenantId);

    private static Scope BuildScope(string name)
    {
        var services = new ServiceCollection();
        services.AddTenancy();
        services.AddDbContext<AssignmentsDbContext>(o => o
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)));
        services.AddDistributedMemoryCache();
        services.AddHybridCache();
        var sp = services.BuildServiceProvider();

        var db = sp.GetRequiredService<AssignmentsDbContext>();
        db.Database.EnsureCreated();
        var tenants = sp.GetRequiredService<ITenantProvider>();
        ((TenantProvider)tenants).SetTenant(new TenantContext(TenantId, "School", TenantType.School));

        return new Scope(db, new AssignmentRepository(db), new AssignmentActivityGroupRepository(db), sp.GetRequiredService<HybridCache>(), tenants, sp);
    }

    private static LinkAssignmentGroupsHandler Handler(Scope scope, IActivityGroupLookup lookup) =>
        new(scope.Db, scope.Assignments, scope.Links, lookup, new FakeTenantProvider(TenantId), scope.Cache, NullLogger<LinkAssignmentGroupsHandler>.Instance);

    [TestMethod]
    public async Task ReplacesTargetRows_AndDerivesCompatAudience_AndSyncsLinkTable()
    {
        using var scope = BuildScope("d82-replace-" + Guid.NewGuid());
        var assignment = NewAssignment();
        assignment.SetTargets(
            [(TargetKind.Stream, (Guid?)Guid.NewGuid()), (TargetKind.ActivityGroup, (Guid?)Group1)],
            TenantId);
        scope.Db.Assignments.Add(assignment);
        await scope.Db.SaveChangesAsync();
        scope.Db.ChangeTracker.Clear();

        var lookup = new FakeActivityGroupLookup
        {
            Groups = [new ActivityGroupRefDto(Group2, "Beta", IsActive: true), new ActivityGroupRefDto(Group3, "Gamma", IsActive: true)]
        };
        await Handler(scope, lookup).HandleAsync(new LinkAssignmentGroups(assignment.Id, [Group2, Group3]));

        var reloaded = await scope.Assignments.GetAsync(assignment.Id);
        reloaded.Should().NotBeNull();
        reloaded!.Targets.Should().HaveCount(3, "the non-group target is preserved and the group rows are replaced");
        reloaded.Targets.Where(t => t.Kind == TargetKind.ActivityGroup).Select(t => t.RefId)
            .Should().BeEquivalentTo(new[] { Group2, Group3 });
        reloaded.TargetAudienceType.Should().Be(TargetAudienceType.SelectedGroups,
            "D-1: a set containing activity groups derives SelectedGroups");

        var linked = await scope.Links.GetGroupIdsForAssignmentAsync(assignment.Id);
        linked.Should().BeEquivalentTo(new[] { Group2, Group3 },
            "the legacy link table is synchronized to the same validated set");
    }

    [TestMethod]
    public async Task NewlyAddedArchivedGroup_IsRejected()
    {
        using var scope = BuildScope("d82-archived-new-" + Guid.NewGuid());
        var assignment = NewAssignment();
        assignment.SetTargets([(TargetKind.ActivityGroup, (Guid?)Group1)], TenantId);
        scope.Db.Assignments.Add(assignment);
        await scope.Db.SaveChangesAsync();
        scope.Db.ChangeTracker.Clear();

        var lookup = new FakeActivityGroupLookup
        {
            Groups =
            [
                new ActivityGroupRefDto(Group1, "Alpha", IsActive: false),
                new ActivityGroupRefDto(Group2, "Beta", IsActive: false)
            ]
        };

        await FluentActions.Awaiting(() => Handler(scope, lookup)
            .HandleAsync(new LinkAssignmentGroups(assignment.Id, [Group1, Group2])))
            .Should().ThrowAsync<ArgumentException>();

        var reloaded = await scope.Assignments.GetAsync(assignment.Id);
        reloaded!.Targets.Should().ContainSingle(t => t.RefId == Group1);
        (await scope.Links.GetGroupIdsForAssignmentAsync(assignment.Id)).Should().BeEmpty();
    }

    [TestMethod]
    public async Task PersistedArchivedGroup_ReSave_IsAllowed()
    {
        using var scope = BuildScope("d82-archived-persisted-" + Guid.NewGuid());
        var assignment = NewAssignment();
        assignment.SetTargets([(TargetKind.ActivityGroup, (Guid?)Group1)], TenantId);
        scope.Db.Assignments.Add(assignment);
        await scope.Db.SaveChangesAsync();
        scope.Db.ChangeTracker.Clear();

        var lookup = new FakeActivityGroupLookup
        {
            Groups = [new ActivityGroupRefDto(Group1, "Alpha", IsActive: false)]
        };
        await Handler(scope, lookup).HandleAsync(new LinkAssignmentGroups(assignment.Id, [Group1]));

        var reloaded = await scope.Assignments.GetAsync(assignment.Id);
        reloaded!.Targets.Should().ContainSingle(t => t.RefId == Group1);
        reloaded.TargetAudienceType.Should().Be(TargetAudienceType.SelectedGroups);
        (await scope.Links.GetGroupIdsForAssignmentAsync(assignment.Id)).Should().BeEquivalentTo(new[] { Group1 });
    }

    [TestMethod]
    public async Task ReindexesDisplayOrder_AfterReplacement()
    {
        using var scope = BuildScope("d82-order-" + Guid.NewGuid());
        var assignment = NewAssignment();
        assignment.SetTargets(
            [(TargetKind.ActivityGroup, (Guid?)Group1), (TargetKind.ActivityGroup, (Guid?)Group2)],
            TenantId);
        scope.Db.Assignments.Add(assignment);
        await scope.Db.SaveChangesAsync();
        scope.Db.ChangeTracker.Clear();

        var lookup = new FakeActivityGroupLookup
        {
            Groups =
            [
                new ActivityGroupRefDto(Group2, "Beta", IsActive: true),
                new ActivityGroupRefDto(Group3, "Gamma", IsActive: true)
            ]
        };
        await Handler(scope, lookup).HandleAsync(new LinkAssignmentGroups(assignment.Id, [Group3, Group2]));

        var reloaded = await scope.Assignments.GetAsync(assignment.Id);
        reloaded!.Targets.Select((t, i) => (t.RefId, i))
            .Should().BeEquivalentTo(new[] { (Group3, 0), (Group2, 1) },
            "DisplayOrder is re-indexed 0..n-1 by list position");
    }

    private sealed class Scope : IDisposable
    {
        public AssignmentsDbContext Db { get; }
        public IAssignmentRepository Assignments { get; }
        public IAssignmentActivityGroupRepository Links { get; }
        public HybridCache Cache { get; }
        public ITenantProvider Tenants { get; }
        private readonly IServiceProvider _sp;

        public Scope(AssignmentsDbContext db, IAssignmentRepository assignments, IAssignmentActivityGroupRepository links, HybridCache cache, ITenantProvider tenants, IServiceProvider sp)
        {
            Db = db;
            Assignments = assignments;
            Links = links;
            Cache = cache;
            Tenants = tenants;
            _sp = sp;
        }

        public void Dispose()
        {
            Db.Dispose();
            (_sp as IDisposable)?.Dispose();
        }
    }

    private sealed class FakeTenantProvider : ITenantProvider
    {
        private readonly TenantContext _ctx;
        public FakeTenantProvider(Guid tenantId) => _ctx = new TenantContext(tenantId, tenantId.ToString(), TenantType.School);
        public TenantContext GetTenantContext() => _ctx;
    }

    private sealed class FakeActivityGroupLookup : IActivityGroupLookup
    {
        public ActivityGroupRefDto[] Groups { get; set; } = [];
        public Task<ActivityGroupRefDto[]> GetByIdsAsync(IReadOnlyList<Guid> activityGroupIds, CancellationToken ct = default)
            => Task.FromResult(Groups);
        public Task<Guid[]> GetActiveMemberIdsAsync(IReadOnlyList<Guid> activityGroupIds, CancellationToken ct = default)
            => Task.FromResult(Array.Empty<Guid>());
    }
}
