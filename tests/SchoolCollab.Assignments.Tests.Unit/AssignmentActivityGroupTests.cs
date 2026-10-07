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
using SchoolCollab.Core.AssignmentPolicies;
using SchoolCollab.Core.Data;
using SchoolCollab.Core.Data.Outbox;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Notifications;
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

    private static Assignment NewAssignment(TargetAudienceType audience) =>
        Assignment.Create("Math", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            audience, TopicId, null, null, TeacherId)
            .WithTenant(TenantId);

    /// <summary>
    /// R2 (D-1/D-8.2): publish now reads the AUTHORED TARGET rows and the
    /// <see cref="IAssignmentTargetResolver"/> union, so this helper projects the pre-R2
    /// link/lookup fixtures onto that seam — each linked group becomes one ActivityGroup target
    /// (unless the assignment already carries targets) and the fake lookup's member ids become the
    /// resolver's union. An assignment with no links therefore has NO targets, which is exactly the
    /// TGT-13 class the publish guard must refuse.
    /// </summary>
    private static PublishAssignmentCommandHandler NewPublishHandler(
        Assignment assignment,
        IContactResolver resolver,
        IAssignmentActivityGroupRepository linkRepo,
        IActivityGroupLookup lookup,
        IAssignmentNotificationBroadcaster broadcaster,
        bool topicAssigned = true,
        IAssignmentPolicyResolver? assignmentPolicyResolver = null,
        INotificationPolicyResolver? notificationPolicyResolver = null)
    {
        if (assignment.Targets.Count == 0 && linkRepo is FakeLinkRepository { GroupIds.Length: > 0 } links)
        {
            assignment.WithGroupTargets(links.GroupIds);
        }

        return new PublishAssignmentCommandHandler(
               new FakeAssignmentRepository { Assignment = assignment },
               new FakeSubmissionRepository(),
               resolver,
               new FakeTopicAssignmentLookup { Result = topicAssigned },
               new FakeAssignmentTargetResolver { StudentIds = (lookup as FakeActivityGroupLookup)?.MemberIds ?? [] },
               new FakeTenantProvider(TenantId),
               broadcaster,
               notificationPolicyResolver ?? new FakeNotificationPolicyResolver(),
               assignmentPolicyResolver ?? new FakeAssignmentPolicyResolver(),
               new FakeFeatureFlagService(),
               new FakeDeepLinkTokenMinter(),
               new FakeHybridCache(),
               NullLogger<PublishAssignmentCommandHandler>.Instance);
    }


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

    // ── AC-13 (TGT-13, EC-7) ───────────────────────────────────────────────────
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

    // ── FR-58: SelectedGroups publish rejects when the subject is not assigned to the group ──
    [TestMethod]
    public async Task Publish_SelectedGroups_SubjectNotAssigned_Rejected()
    {
        var assignment = NewAssignment(TargetAudienceType.SelectedGroups);
        var handler = NewPublishHandler(
            assignment,
            new FakeContactResolver([]),
            new FakeLinkRepository { GroupIds = [Group1] },
            new FakeActivityGroupLookup { MemberIds = [StudentId1] },
            new FakeBroadcaster(),
            topicAssigned: false);

        await FluentActions.Awaiting(() => handler.HandleAsync(new PublishAssignmentCommand(assignment.Id)))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    // ── TGT-10 / D-6(c): a target set that resolves to NO students refuses the publish ─────
    [TestMethod]
    public async Task Publish_GroupTargetWithNoResolvedStudents_IsRefused()
    {
        // Pre-R2 this scenario (a group whose members resolve to nobody) published an empty
        // sendout. R2 is fail-closed (D-6c): an empty resolved union blocks the publish instead of
        // silently publishing to nobody. The EC-9 exclusion itself (a student in zero groups is
        // not matched) is asserted on the resolver side — ResolveStudentsByTargetHandlerTests.
        var assignment = NewAssignment(TargetAudienceType.SelectedGroups);
        var broadcaster = new FakeBroadcaster();
        var handler = NewPublishHandler(
            assignment,
            new FakeContactResolver([]),
            new FakeLinkRepository { GroupIds = [Group1] },
            new FakeActivityGroupLookup { MemberIds = [] },
            broadcaster);

        await FluentActions.Awaiting(() => handler.HandleAsync(new PublishAssignmentCommand(assignment.Id)))
            .Should().ThrowAsync<InvalidOperationException>();

        broadcaster.Last.Should().BeNull("a refused publish must not broadcast anything");
        // The in-memory aggregate IS Publish()-ed before resolution (the handler's existing order),
        // but the refusal propagates before any persistence — nothing is written and the route
        // maps InvalidOperationException to 400 (D-6a).
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

    // ── TGT-9: the resolved target cohort rides StudentIds; the grade targets ride GradeLevelIds ──
    [TestMethod]
    public async Task SelectedGrades_Path_ResolvesThroughTheTargetResolver()
    {
        // D-6/D-9 (round drop-primary-grade): the grade target resolves through
        // IAssignmentTargetResolver (never the legacy grade cohort enumeration) and the RESOLVED ids
        // ride StudentIds, while the assignment's distinct grade-target ids ride GradeLevelIds so each
        // targeted grade's teacher-recipient leg survives.
        var assignment = NewAssignment(TargetAudienceType.SelectedGrades)
            .WithGradeTarget(GradeLevelId);
        var resolver = new CapturingContactResolver([]);
        var handler = NewPublishHandler(
            assignment,
            resolver,
            new FakeLinkRepository(),
            new FakeActivityGroupLookup { MemberIds = [StudentId1] },
            new FakeBroadcaster());

        await handler.HandleAsync(new PublishAssignmentCommand(assignment.Id));

        resolver.LastRequest!.GradeLevelIds.Should().Equal(new[] { GradeLevelId },
            "the targeted grade is the teacher-recipient leg (the D-6 documented widening, now "
            + "derived per grade target rather than from an authored primary grade)");
        resolver.LastRequest.StudentIds.Should().Equal(new[] { StudentId1 },
            "the resolver's union is what publish sends to (TGT-9)");
    }

    // ── AC-5 (round drop-primary-grade): every policy leg DERIVES the grade from the targets ──

    [TestMethod]
    public async Task Publish_SingleGradeTarget_ResolvesBothPoliciesForThatGrade()
    {
        var assignment = NewAssignment(TargetAudienceType.SelectedGrades).WithGradeTarget(GradeLevelId);
        var contacts = new CapturingContactResolver([]);
        var assignmentPolicy = new RecordingAssignmentPolicyResolver();
        var notificationPolicy = new RecordingNotificationPolicyResolver();
        var handler = NewPublishHandler(
            assignment,
            contacts,
            new FakeLinkRepository(),
            new FakeActivityGroupLookup { MemberIds = [StudentId1] },
            new FakeBroadcaster(),
            assignmentPolicyResolver: assignmentPolicy,
            notificationPolicyResolver: notificationPolicy);

        await handler.HandleAsync(new PublishAssignmentCommand(assignment.Id));

        contacts.LastRequest!.GradeLevelIds.Should().Equal(new[] { GradeLevelId },
            "AC-5a: the single grade target is the teacher-cohort grade");
        assignmentPolicy.RequestedGradeIds.Should().Equal(new Guid?[] { GradeLevelId },
            "AC-5a: one distinct grade target derives that grade for the assignment policy");
        notificationPolicy.RequestedGradeIds.Should().Equal(new Guid?[] { GradeLevelId },
            "AC-5a: …and for the notification policy — one derivation, two consumers");
    }

    [TestMethod]
    public async Task Publish_TwoGradeTargets_KeepsBothTeacherLegs_ButDerivesTheTenantDefaultPolicy()
    {
        var gradeB = Guid.Parse("22222222-2222-2222-2222-22222222222b");
        var assignment = NewAssignment(TargetAudienceType.SelectedGrades);
        assignment.SetTargets(
            [(TargetKind.GradeLevel, (Guid?)GradeLevelId), (TargetKind.GradeLevel, (Guid?)gradeB)], TenantId);
        var contacts = new CapturingContactResolver([]);
        var assignmentPolicy = new RecordingAssignmentPolicyResolver();
        var notificationPolicy = new RecordingNotificationPolicyResolver();
        var handler = NewPublishHandler(
            assignment,
            contacts,
            new FakeLinkRepository(),
            new FakeActivityGroupLookup { MemberIds = [StudentId1] },
            new FakeBroadcaster(),
            assignmentPolicyResolver: assignmentPolicy,
            notificationPolicyResolver: notificationPolicy);

        await handler.HandleAsync(new PublishAssignmentCommand(assignment.Id));

        contacts.LastRequest!.GradeLevelIds.Should().BeEquivalentTo(new[] { GradeLevelId, gradeB },
            "AC-5b: every targeted grade keeps its teacher-recipient leg");
        assignmentPolicy.RequestedGradeIds.Should().Equal(new Guid?[] { null },
            "AC-5b: two distinct grade targets derive NULL — the tenant-default policy");
        notificationPolicy.RequestedGradeIds.Should().Equal(new Guid?[] { null });
    }

    [TestMethod]
    public async Task Publish_NoGradeTarget_ResolvesTheTenantDefaultAndNoTeacherLeg()
    {
        var assignment = NewAssignment(TargetAudienceType.SelectedGroups).WithGroupTargets(Group1);
        var contacts = new CapturingContactResolver([]);
        var assignmentPolicy = new RecordingAssignmentPolicyResolver();
        var notificationPolicy = new RecordingNotificationPolicyResolver();
        var handler = NewPublishHandler(
            assignment,
            contacts,
            new FakeLinkRepository(),
            new FakeActivityGroupLookup { MemberIds = [StudentId1] },
            new FakeBroadcaster(),
            assignmentPolicyResolver: assignmentPolicy,
            notificationPolicyResolver: notificationPolicy);

        await handler.HandleAsync(new PublishAssignmentCommand(assignment.Id));

        contacts.LastRequest!.GradeLevelIds.Should().BeNullOrEmpty(
            "AC-5c: with no grade target there is no teacher-recipient leg");
        assignmentPolicy.RequestedGradeIds.Should().Equal(new Guid?[] { null },
            "AC-5c: no grade target derives the tenant-default policy");
        notificationPolicy.RequestedGradeIds.Should().Equal(new Guid?[] { null });
    }

    [TestMethod]
    public async Task Publish_EmptyResolvedStudentSet_IsStillRefused()
    {
        var assignment = NewAssignment(TargetAudienceType.SelectedGrades).WithGradeTarget(GradeLevelId);
        var broadcaster = new FakeBroadcaster();
        var handler = NewPublishHandler(
            assignment,
            new FakeContactResolver([]),
            new FakeLinkRepository(),
            new FakeActivityGroupLookup { MemberIds = [] },
            broadcaster);

        await FluentActions.Awaiting(() => handler.HandleAsync(new PublishAssignmentCommand(assignment.Id)))
            .Should().ThrowAsync<InvalidOperationException>(
                "AC-5d / D-6(c): an empty resolved target set is refused — the fail-closed mirror of the "
                + "fail-open policy resolver");

        broadcaster.Last.Should().BeNull("the refusal happens before any broadcast / recipient write");
    }

    // ── TGT-10 / D-6(b): a resolver outage BLOCKS the publish (fail-closed) ────────────────
    [TestMethod]
    public async Task Publish_TargetResolverOutage_IsRefused()
    {
        var assignment = NewAssignment(TargetAudienceType.SelectedGrades)
            .WithGradeTarget(GradeLevelId);
        var broadcaster = new FakeBroadcaster();
        var handler = new PublishAssignmentCommandHandler(
            new FakeAssignmentRepository { Assignment = assignment },
            new FakeSubmissionRepository(),
            new FakeContactResolver([]),
            new FakeTopicAssignmentLookup(),
            new FakeAssignmentTargetResolver { Throw = new HttpRequestException("students-api unreachable") },
            new FakeTenantProvider(TenantId),
            broadcaster,
            new FakeNotificationPolicyResolver(),
            new FakeAssignmentPolicyResolver(),
            new FakeFeatureFlagService(),
            new FakeDeepLinkTokenMinter(),
            new FakeHybridCache(),
            NullLogger<PublishAssignmentCommandHandler>.Instance);

        await FluentActions.Awaiting(() => handler.HandleAsync(new PublishAssignmentCommand(assignment.Id)))
            .Should().ThrowAsync<HttpRequestException>();

        broadcaster.Last.Should().BeNull("a resolver failure must never degrade into a sendout");
    }

    // ── AC-14 (FR-21, NFR-5) / EC-11: group not in tenant → omitted → rejected ──
    [TestMethod]
    public async Task LinkGroup_CrossTenant_Rejected()
    {
        var assignment = NewAssignment(TargetAudienceType.SelectedGroups);
        var handler = new LinkAssignmentGroupsHandler(
            TestDb(),
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
            TestDb(),
            new FakeAssignmentRepository { Assignment = assignment },
            new FakeLinkRepository(),
            new FakeActivityGroupLookup { Groups = [new ActivityGroupRefDto(Group1, "Chess", IsActive: false)] },
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

    // ── R2-7 (D-8.2 / FR-6): a group referenced ONLY via an AssignmentTarget row is still
    // reported by the hard-delete guard — the reverse lookup unions both sources.
    [TestMethod]
    public async Task DeleteGroup_ReferencedOnlyByTargetRow_Blocked()
    {
        using var scope = new LinkScope("target-only-guard-" + Guid.NewGuid());
        var assignment = NewAssignment(TargetAudienceType.SelectedGroups);
        assignment.SetTargets([(TargetKind.ActivityGroup, (Guid?)Group1)], TenantId);
        scope.Db.Assignments.Add(assignment);
        await scope.Db.SaveChangesAsync();

        // Ensure no legacy link-table row exists — the guard must still see the target row.
        scope.Db.AssignmentActivityGroups.RemoveRange(scope.Db.AssignmentActivityGroups.Where(l => l.AssignmentId == assignment.Id));
        await scope.Db.SaveChangesAsync();

        var ids = await scope.Links.GetAssignmentIdsByGroupAsync(Group1);
        ids.Should().ContainSingle().Which.Should().Be(assignment.Id);

        var summaries = await scope.Links.GetAssignmentsByGroupAsync(Group1);
        summaries.Should().ContainSingle();
        summaries[0].Id.Should().Be(assignment.Id);
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

    private static AssignmentsDbContext TestDb()
    {
        var services = new ServiceCollection();
        services.AddTenancy();
        services.AddDbContext<AssignmentsDbContext>(o => o
            .UseInMemoryDatabase("link-handler-" + Guid.NewGuid())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)));
        var sp = services.BuildServiceProvider();
        var db = sp.GetRequiredService<AssignmentsDbContext>();
        db.Database.EnsureCreated();
        var tenants = sp.GetRequiredService<ITenantProvider>();
        ((TenantProvider)tenants).SetTenant(new TenantContext(TenantId, "School", TenantType.School));
        return db;
    }

    private sealed class FakeTenantProvider : ITenantProvider
    {
        private readonly TenantContext _ctx;
        public FakeTenantProvider(Guid tenantId) => _ctx = new TenantContext(tenantId, tenantId.ToString(), TenantType.School);
        public TenantContext GetTenantContext() => _ctx;
    }

    /// <summary>Records the grade each assignment-policy leg resolved for — the observable for the
    /// round drop-primary-grade derivation (AC-5).</summary>
    private sealed class RecordingAssignmentPolicyResolver : IAssignmentPolicyResolver
    {
        public List<Guid?> RequestedGradeIds { get; } = [];

        public Task<EffectiveAssignmentPolicy> ResolveAsync(
            Guid? gradeLevelId, CancellationToken cancellationToken = default)
        {
            RequestedGradeIds.Add(gradeLevelId);
            return Task.FromResult(FakeAssignmentPolicyResolver.BuiltInDefault);
        }
    }

    /// <summary>Records the grade each notification-policy leg resolved for (AC-5).</summary>
    private sealed class RecordingNotificationPolicyResolver : INotificationPolicyResolver
    {
        public List<Guid?> RequestedGradeIds { get; } = [];

        public Task<EffectiveNotificationPolicy> ResolveEffectiveAsync(
            Guid tenantId, Guid? gradeLevelId, CancellationToken cancellationToken = default)
        {
            RequestedGradeIds.Add(gradeLevelId);
            return Task.FromResult(FakeNotificationPolicyResolver.Empty);
        }
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

    private sealed class FakeTopicAssignmentLookup : SchoolCollab.Assignments.Core.Services.ITopicAssignmentLookup
    {
        public bool Result = true;
        public Task<bool> IsTopicAssignedAsync(Guid? gradeLevelId, IReadOnlyList<Guid> activityGroupIds,
            Guid topicId, DateOnly effectiveDate, CancellationToken ct = default)
            => Task.FromResult(Result);
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
        public void DetectChanges() { /* no-op for fake */ }
        public Task<List<AssignmentSweepCandidate>> ListScheduledForAutoPublishAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
            => Task.FromResult(new List<AssignmentSweepCandidate>());
        public Task<List<AssignmentSweepCandidate>> ListDueForArchiveAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
            => Task.FromResult(new List<AssignmentSweepCandidate>());
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
        public void Add(SubmissionAnswer a) { }
        public void Add(SignatureEvent e) { }
        public Task<SignatureEvent?> GetSignatureEventByAssignmentStudentAsync(Guid a, Guid s, CancellationToken ct = default) => Task.FromResult<SignatureEvent?>(null);
        public Task<List<AssignmentSubmission>> ListSubmissionEntitiesByAssignmentAsync(Guid a, CancellationToken ct = default) => Task.FromResult(new List<AssignmentSubmission>());
        public Task<List<AssignmentRecipient>> ListRecipientEntitiesByAssignmentAsync(Guid a, CancellationToken ct = default) => Task.FromResult(new List<AssignmentRecipient>());
        public Task<List<AssignmentSubmissionVersion>> ListVersionsForSubmissionIdsAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default) => Task.FromResult(new List<AssignmentSubmissionVersion>());
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

