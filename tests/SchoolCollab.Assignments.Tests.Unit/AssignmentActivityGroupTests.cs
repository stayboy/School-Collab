using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.LinkAssignmentGroups;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.PublishAssignmentCommand;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.DTOs;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.Data;
using SchoolCollab.Core.Data.Outbox;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// Assignment ↔ ActivityGroup link + SelectedGroups publish (spec
/// activity-group-enrollment.md §3.3 FR-17..23, §5 AC-12..16, §6 EC-4/7/9/11/12).
/// </summary>
[TestClass]
public class AssignmentActivityGroupTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000010");
    private static readonly Guid GradeLevelId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid StudentId1 = Guid.Parse("33333333-3333-3333-3333-333333333331");
    private static readonly Guid StudentId2 = Guid.Parse("33333333-3333-3333-3333-333333333332");
    private static readonly Guid Contact1 = Guid.Parse("66666666-6666-6666-6666-666666666661");
    private static readonly Guid Contact2 = Guid.Parse("66666666-6666-6666-6666-666666666662");
    private static readonly Guid Group1 = Guid.Parse("77777777-7777-7777-7777-777777777771");
    private static readonly Guid Group2 = Guid.Parse("77777777-7777-7777-7777-777777777772");
    private static readonly Guid Group3 = Guid.Parse("77777777-7777-7777-7777-777777777773");

    private static Assignment NewAssignment(TargetAudienceType audience, Guid? gradeLevelId = null) =>
        Assignment.Create("Math", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            audience, TopicId, gradeLevelId, null, null, TeacherId)
            .WithTenant(TenantId);

    private static PublishAssignmentCommandHandler NewPublishHandler(
        Assignment assignment,
        IContactResolver resolver,
        IAssignmentActivityGroupRepository linkRepo,
        IActivityGroupLookup lookup,
        IAssignmentNotificationBroadcaster broadcaster)
        => new(new FakeAssignmentRepository { Assignment = assignment },
               new FakeSubmissionRepository(),
               resolver,
               linkRepo,
               lookup,
               new FakeTenantProvider(TenantId),
               broadcaster,
               new FakeNotificationPolicyResolver(),
               new FakeHybridCache(),
               NullLogger<PublishAssignmentCommandHandler>.Instance);


    // ── AC-12 (FR-17, FR-18, FR-20) ────────────────────────────────────────────
    [TestMethod]
    public async Task Publish_SelectedGroups_CreatesRecipientsForMembers()
    {
        var assignment = NewAssignment(TargetAudienceType.SelectedGroups);
        var subscribers = new List<SubscriberInfo>
        {
            new(Contact1, ContactOwnerType.Student, StudentId1, StudentId1, ContactChannel.Email, null),
            new(Contact2, ContactOwnerType.Student, StudentId2, StudentId2, ContactChannel.Email, null),
        };
        var broadcaster = new FakeBroadcaster();
        var handler = NewPublishHandler(
            assignment,
            new FakeContactResolver(subscribers),
            new FakeLinkRepository { GroupIds = [Group1, Group2] },
            new FakeActivityGroupLookup { MemberIds = [StudentId1, StudentId2] },
            broadcaster);

        await handler.HandleAsync(new PublishAssignmentCommand(assignment.Id));

        assignment.Status.Should().Be(AssignmentStatus.Published);
        broadcaster.Last!.Recipients.Should().HaveCount(2);
    }

    // ── AC-13 (FR-23, EC-7) ────────────────────────────────────────────────────
    [TestMethod]
    public async Task Publish_SelectedGroups_ZeroGroups_Rejected()
    {
        var assignment = NewAssignment(TargetAudienceType.SelectedGroups);
        var handler = NewPublishHandler(
            assignment,
            new FakeContactResolver([]),
            new FakeLinkRepository { GroupIds = [] },
            new FakeActivityGroupLookup(),
            new FakeBroadcaster());

        await FluentActions.Awaiting(() => handler.HandleAsync(new PublishAssignmentCommand(assignment.Id)))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    // ── EC-9: student in zero groups not targeted ──────────────────────────────
    [TestMethod]
    public async Task StudentInZeroGroups_NotTargeted()
    {
        var assignment = NewAssignment(TargetAudienceType.SelectedGroups);
        var broadcaster = new FakeBroadcaster();
        var handler = NewPublishHandler(
            assignment,
            new FakeContactResolver([]),
            new FakeLinkRepository { GroupIds = [Group1] },
            new FakeActivityGroupLookup { MemberIds = [] },
            broadcaster);

        await handler.HandleAsync(new PublishAssignmentCommand(assignment.Id));

        broadcaster.Last!.Recipients.Should().BeEmpty();
    }

    // ── EC-4: archived group excluded from resolution ──────────────────────────
    [TestMethod]
    public async Task RePublish_ArchivedGroup_ExcludedFromResolution()
    {
        var assignment = NewAssignment(TargetAudienceType.SelectedGroups);
        var subscribers = new List<SubscriberInfo>
        {
            new(Contact1, ContactOwnerType.Student, StudentId1, StudentId1, ContactChannel.Email, null),
        };
        var broadcaster = new FakeBroadcaster();
        // The lookup simulates the HTTP impl: archived groups contribute no
        // members, so only StudentId1 (active group) is resolved.
        var handler = NewPublishHandler(
            assignment,
            new FakeContactResolver(subscribers),
            new FakeLinkRepository { GroupIds = [Group1, Group2] },
            new FakeActivityGroupLookup { MemberIds = [StudentId1] },
            broadcaster);

        await handler.HandleAsync(new PublishAssignmentCommand(assignment.Id));

        broadcaster.Last!.Recipients.Should().ContainSingle();
    }

    // ── AC-16 (FR-19, NFR-6) ───────────────────────────────────────────────────
    [TestMethod]
    public async Task SelectedGrades_Path_Unchanged()
    {
        var assignment = NewAssignment(TargetAudienceType.SelectedGrades, GradeLevelId);
        var resolver = new CapturingContactResolver([]);
        var handler = NewPublishHandler(
            assignment,
            resolver,
            new FakeLinkRepository(),
            new FakeActivityGroupLookup(),
            new FakeBroadcaster());

        await handler.HandleAsync(new PublishAssignmentCommand(assignment.Id));

        resolver.LastRequest!.GradeLevelId.Should().Be(GradeLevelId);
        resolver.LastRequest.StudentIds.Should().BeNull();
    }

    // ── AC-14 (FR-21, NFR-5) / EC-11: group not in tenant → omitted → rejected ──
    [TestMethod]
    public async Task LinkGroup_CrossTenant_Rejected()
    {
        var assignment = NewAssignment(TargetAudienceType.SelectedGroups);
        var handler = new LinkAssignmentGroupsHandler(
            new FakeAssignmentRepository { Assignment = assignment },
            new FakeLinkRepository(),
            new FakeActivityGroupLookup { Groups = [] },
            new FakeTenantProvider(TenantId),
            new FakeHybridCache(),
            NullLogger<LinkAssignmentGroupsHandler>.Instance);

        await FluentActions.Awaiting(() => handler.HandleAsync(new LinkAssignmentGroups(assignment.Id, [Group1])))
            .Should().ThrowAsync<ArgumentException>();
    }

    // ── AC-15 (FR-22) ──────────────────────────────────────────────────────────
    [TestMethod]
    public async Task LinkGroup_Archived_Rejected()
    {
        var assignment = NewAssignment(TargetAudienceType.SelectedGroups);
        var handler = new LinkAssignmentGroupsHandler(
            new FakeAssignmentRepository { Assignment = assignment },
            new FakeLinkRepository(),
            new FakeActivityGroupLookup { Groups = [new ActivityGroupRefDto(Group1, "Chess", "Archived")] },
            new FakeTenantProvider(TenantId),
            new FakeHybridCache(),
            NullLogger<LinkAssignmentGroupsHandler>.Instance);

        await FluentActions.Awaiting(() => handler.HandleAsync(new LinkAssignmentGroups(assignment.Id, [Group1])))
            .Should().ThrowAsync<ArgumentException>();
    }

    // ── FR-17: link set replace (real in-memory link repository) ───────────────
    [TestMethod]
    public async Task LinkSet_Replace_Succeeds()
    {
        using var scope = new LinkScope("link-replace-" + Guid.NewGuid());
        var assignment = NewAssignment(TargetAudienceType.SelectedGroups);
        scope.Db.Assignments.Add(assignment);
        await scope.Db.SaveChangesAsync();

        await scope.Links.ReplaceForAssignmentAsync(assignment.Id, TenantId, [Group1, Group2]);
        (await scope.Links.GetGroupIdsForAssignmentAsync(assignment.Id)).Should().BeEquivalentTo(new[] { Group1, Group2 });

        await scope.Links.ReplaceForAssignmentAsync(assignment.Id, TenantId, [Group2, Group3]);
        (await scope.Links.GetGroupIdsForAssignmentAsync(assignment.Id)).Should().BeEquivalentTo(new[] { Group2, Group3 });
    }

    // ── EC-12: group with live links surfaced by reverse lookup (FR-6 guard) ───
    [TestMethod]
    public async Task DeleteGroup_WithLiveLinks_Blocked()
    {
        using var scope = new LinkScope("link-guard-" + Guid.NewGuid());
        var assignment = NewAssignment(TargetAudienceType.SelectedGroups);
        scope.Db.Assignments.Add(assignment);
        await scope.Db.SaveChangesAsync();
        await scope.Links.ReplaceForAssignmentAsync(assignment.Id, TenantId, [Group1]);

        var summaries = await scope.Links.GetAssignmentsByGroupAsync(Group1);

        summaries.Should().ContainSingle();
        summaries[0].Title.Should().Be("Math");
        summaries[0].Status.Should().Be("Draft");
    }

    // ── NFR-9: model guard ─────────────────────────────────────────────────────
    [TestMethod]
    public void NoUncommittedModelChanges()
    {
        var tenantProvider = new DesignTimeTenantProvider();
        OutboxMapping.SetFlagsFor<AssignmentsDbContext>(
            OutboxConfigurationFlags.FromConfiguration(b => b
                .UsePartialIndexOnOccurredAt()));

        using var context = new AssignmentsDbContext(
            new DbContextOptionsBuilder<AssignmentsDbContext>()
                .UseNpgsql("Host=localhost;Database=guard")
                .UseSnakeCaseNamingConvention()
                .Options,
            tenantProvider);

        Assert.IsFalse(
            context.Database.HasPendingModelChanges(),
            "Model has changes not reflected in a migration. " +
            "Run 'dotnet ef migrations add <Name> --project src/Assignments/SchoolCollab.Assignments.Core'");
    }


    // ── Fakes ──────────────────────────────────────────────────────────────────

    private sealed class LinkScope : IDisposable
    {
        public AssignmentsDbContext Db { get; }
        public AssignmentActivityGroupRepository Links { get; }

        public LinkScope(string name)
        {
            var services = new ServiceCollection();
            services.AddTenancy();
            services.AddDbContext<AssignmentsDbContext>(o => o.UseInMemoryDatabase(name));
            var sp = services.BuildServiceProvider();
            Db = sp.GetRequiredService<AssignmentsDbContext>();
            Db.Database.EnsureCreated();
            var tenants = sp.GetRequiredService<ITenantProvider>();
            ((TenantProvider)tenants).SetTenant(new TenantContext(TenantId, "School", TenantType.School));
            Links = new AssignmentActivityGroupRepository(Db);
        }

        public void Dispose() => Db.Dispose();
    }

    private sealed class FakeTenantProvider : ITenantProvider
    {
        private readonly TenantContext _ctx;
        public FakeTenantProvider(Guid tenantId) => _ctx = new TenantContext(tenantId, tenantId.ToString(), TenantType.School);
        public TenantContext GetTenantContext() => _ctx;
    }

    private sealed class FakeContactResolver : IContactResolver
    {
        private readonly IReadOnlyList<SubscriberInfo> _subscribers;
        public FakeContactResolver(IReadOnlyList<SubscriberInfo> subscribers) => _subscribers = subscribers;
        public Task<IReadOnlyList<SubscriberInfo>> ResolveSubscribersAsync(ResolveSubscribersRequest request, CancellationToken ct = default)
            => Task.FromResult(_subscribers);
    }

    private sealed class CapturingContactResolver : IContactResolver
    {
        private readonly IReadOnlyList<SubscriberInfo> _subscribers;
        public ResolveSubscribersRequest? LastRequest { get; private set; }
        public CapturingContactResolver(IReadOnlyList<SubscriberInfo> subscribers) => _subscribers = subscribers;
        public Task<IReadOnlyList<SubscriberInfo>> ResolveSubscribersAsync(ResolveSubscribersRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            return Task.FromResult(_subscribers);
        }
    }

    private sealed class FakeBroadcaster : IAssignmentNotificationBroadcaster
    {
        public AssignmentPublishedContext? Last { get; private set; }
        public Task BroadcastPublishedAsync(AssignmentPublishedContext context, CancellationToken ct = default)
        { Last = context; return Task.CompletedTask; }
    }

    private sealed class FakeActivityGroupLookup : IActivityGroupLookup
    {
        public ActivityGroupRefDto[] Groups { get; set; } = [];
        public Guid[] MemberIds { get; set; } = [];

        public Task<ActivityGroupRefDto[]> GetByIdsAsync(IReadOnlyList<Guid> activityGroupIds, CancellationToken ct = default)
            => Task.FromResult(Groups);

        public Task<Guid[]> GetActiveMemberIdsAsync(IReadOnlyList<Guid> activityGroupIds, CancellationToken ct = default)
            => Task.FromResult(MemberIds);
    }

    private sealed class FakeLinkRepository : IAssignmentActivityGroupRepository
    {
        public Guid[] GroupIds { get; set; } = [];

        public Task<Guid[]> GetGroupIdsForAssignmentAsync(Guid assignmentId, CancellationToken ct = default)
            => Task.FromResult(GroupIds);

        public Task ReplaceForAssignmentAsync(Guid assignmentId, Guid tenantId, IReadOnlyList<Guid> activityGroupIds, CancellationToken ct = default)
        { GroupIds = activityGroupIds.ToArray(); return Task.CompletedTask; }

        public Task<Guid[]> GetAssignmentIdsByGroupAsync(Guid activityGroupId, CancellationToken ct = default)
            => Task.FromResult(Array.Empty<Guid>());

        public Task<AssignmentGroupSummaryDto[]> GetAssignmentsByGroupAsync(Guid activityGroupId, CancellationToken ct = default)
            => Task.FromResult(Array.Empty<AssignmentGroupSummaryDto>());
    }


    private sealed class FakeAssignmentRepository : IAssignmentRepository
    {
        public Assignment? Assignment;
        public Task<Assignment?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Assignment);
        public Task AddAsync(Assignment a, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(Assignment a, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Assignment a, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<AssignmentSummary>> ListAsync(AssignmentStatus? s, CancellationToken ct = default)
            => Task.FromResult(new List<AssignmentSummary>());
    }

    private sealed class FakeSubmissionRepository : ISubmissionRepository
    {
        public Task<AssignmentRecipient?> GetRecipientAsync(Guid a, Guid c, CancellationToken ct = default) => Task.FromResult<AssignmentRecipient?>(null);
        public void Add(AssignmentRecipient r) { }
        public void Update(AssignmentRecipient r) { }
        public Task<int> DeleteRecipientsForAssignmentAsync(Guid assignmentId, CancellationToken ct = default) => Task.FromResult(0);
        public Task<GuardianSubmissionGate?> GetGateAsync(Guid id, CancellationToken ct = default) => Task.FromResult<GuardianSubmissionGate?>(null);
        public Task<GuardianSubmissionGate?> GetGateByAssignmentStudentAsync(Guid a, Guid s, CancellationToken ct = default) => Task.FromResult<GuardianSubmissionGate?>(null);
        public void Add(GuardianSubmissionGate g) { }
        public void Update(GuardianSubmissionGate g) { }
        public Task<List<GuardianSubmissionGate>> ListGatesForAssignmentAsync(Guid assignmentId, CancellationToken ct = default) => Task.FromResult(new List<GuardianSubmissionGate>());
        public Task<AssignmentSubmission?> GetSubmissionAsync(Guid id, CancellationToken ct = default) => Task.FromResult<AssignmentSubmission?>(null);
        public Task<AssignmentSubmission?> GetSubmissionByAssignmentStudentAsync(Guid a, Guid s, CancellationToken ct = default) => Task.FromResult<AssignmentSubmission?>(null);
        public void Add(AssignmentSubmission s) { }
        public void Update(AssignmentSubmission s) { }
        public void Add(AssignmentSubmissionVersion v) { }
        public void Add(SubmissionReview r) { }
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
        public Task<SubmissionForReviewDto[]> ListSubmissionsForReviewAsync(Guid t, CancellationToken ct = default) => Task.FromResult(Array.Empty<SubmissionForReviewDto>());
        public Task<SubmissionForReviewDto[]> ListSubmissionsByAssignmentAsync(Guid a, CancellationToken ct = default) => Task.FromResult(Array.Empty<SubmissionForReviewDto>());
        public Task<AssignmentRecipientDto[]> ListRecipientsForAssignmentAsync(Guid a, CancellationToken ct = default) => Task.FromResult(Array.Empty<AssignmentRecipientDto>());
        public Task<SubmissionDetailDto?> GetSubmissionDetailAsync(Guid a, Guid s, CancellationToken ct = default) => Task.FromResult<SubmissionDetailDto?>(null);
        public Task<GuardianGateDto?> GetGuardianGateAsync(Guid a, Guid s, CancellationToken ct = default) => Task.FromResult<GuardianGateDto?>(null);
    }

    private sealed class FakeHybridCache : HybridCache
    {
        public override ValueTask<T> GetOrCreateAsync<TState, T>(string key, TState state, Func<TState, CancellationToken, ValueTask<T>> factory, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
            => factory(state, cancellationToken);
        public override ValueTask SetAsync<T>(string key, T value, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
        public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
        public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }
}

