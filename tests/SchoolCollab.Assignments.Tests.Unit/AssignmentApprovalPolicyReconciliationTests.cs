using FluentAssertions;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.PublishAssignmentCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.ScheduleAssignmentCommand;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.DTOs;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.AssignmentPolicies;
using SchoolCollab.Core.Features;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// Round A (<c>documents/solution/assignment-policy-fields.md</c> D3, §5) — the approval
/// <b>reconciliation</b> at both gate seams: the effective policy's
/// <see cref="EffectiveAssignmentPolicy.RequiresApprovalBeforePublish"/> is OR'd with
/// <c>FEATURE:RequireAssignmentApproval</c> for one release, so either one alone gates
/// <see cref="PublishAssignmentCommandHandler"/> and
/// <see cref="ScheduleAssignmentCommandHandler"/>.
///
/// <para>The discriminating cases: (a) policy ON + flag OFF gates — the new field is an
/// independent trigger; (b) policy OFF + flag ON gates — the pre-existing flag is unchanged;
/// (c) both off does not gate; (d) the resolver's <b>fail-open</b> result (the built-in default a
/// failed fetch produces) + flag OFF does <b>not</b> gate — a policy outage must never silently
/// turn approval ON.</para>
/// </summary>
[TestClass]
public class AssignmentApprovalPolicyReconciliationTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000010");
    private static readonly Guid StudentId = Guid.Parse("00000000-0000-0000-0000-000000000020");

    private static Assignment NewAssignment() =>
        Assignment.Create("Math", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, null, null, null, TeacherId)
            .WithTenant(TenantId)
            // R2 (TGT-13): a publishable assignment needs at least one authored target row.
            .WithAllStudentsTarget();

    private static EffectiveAssignmentPolicy PolicyWith(bool requiresApproval) =>
        FakeAssignmentPolicyResolver.BuiltInDefault with { RequiresApprovalBeforePublish = requiresApproval };

    private static PublishAssignmentCommandHandler NewPublishHandler(
        Assignment assignment, IFeatureFlagService flags, IAssignmentPolicyResolver policyResolver) =>
        new(new StubAssignmentRepository(assignment), new StubSubmissionRepository(),
            new StubContactResolver(),
            new StubTopicAssignmentLookup(),
            new FakeAssignmentTargetResolver { StudentIds = [StudentId] },
            new StubTenantProvider(TenantId), new StubBroadcaster(),
            new FakeNotificationPolicyResolver(), policyResolver, flags, new FakeDeepLinkTokenMinter(),
            new StubHybridCache(), NullLogger<PublishAssignmentCommandHandler>.Instance);

    private static ScheduleAssignmentCommandHandler NewScheduleHandler(
        Assignment assignment, IFeatureFlagService flags, IAssignmentPolicyResolver policyResolver) =>
        new(new StubAssignmentRepository(assignment), flags, policyResolver, new StubHybridCache(),
            NullLogger<ScheduleAssignmentCommandHandler>.Instance);

    // ── (a) the new policy field alone gates ──────────────────────────────

    [TestMethod]
    public async Task Publish_PolicyRequiresApproval_FlagOff_IsGated()
    {
        // Arrange
        var assignment = NewAssignment();
        var handler = NewPublishHandler(
            assignment, new FakeFeatureFlagService(), new FakeAssignmentPolicyResolver { Policy = PolicyWith(true) });

        // Act / Assert
        await FluentActions.Awaiting(() => handler.HandleAsync(new PublishAssignmentCommand(assignment.Id)))
            .Should().ThrowAsync<AssignmentApprovalRequiredException>(
                "the policy field is an independent trigger from the flag");

        assignment.Status.Should().Be(AssignmentStatus.Draft);
    }

    [TestMethod]
    public async Task Schedule_PolicyRequiresApproval_FlagOff_IsGated()
    {
        // Arrange
        var assignment = NewAssignment();
        var handler = NewScheduleHandler(
            assignment, new FakeFeatureFlagService(), new FakeAssignmentPolicyResolver { Policy = PolicyWith(true) });

        // Act / Assert
        await FluentActions.Awaiting(() =>
                handler.HandleAsync(new ScheduleAssignmentCommand(assignment.Id, DateTimeOffset.UtcNow.AddDays(7))))
            .Should().ThrowAsync<AssignmentApprovalRequiredException>();

        assignment.Status.Should().Be(AssignmentStatus.Draft);
    }

    // ── (b) the pre-existing flag still alone gates ───────────────────────

    [TestMethod]
    public async Task Publish_PolicyDoesNotRequireApproval_FlagOn_IsGated()
    {
        // Arrange
        var assignment = NewAssignment();
        var flags = new FakeFeatureFlagService { IsEnabledValue = true };
        var handler = NewPublishHandler(
            assignment, flags, new FakeAssignmentPolicyResolver { Policy = PolicyWith(false) });

        // Act / Assert
        await FluentActions.Awaiting(() => handler.HandleAsync(new PublishAssignmentCommand(assignment.Id)))
            .Should().ThrowAsync<AssignmentApprovalRequiredException>(
                "reconcile, do not replace: the flag keeps gating for one release");

        assignment.Status.Should().Be(AssignmentStatus.Draft);
    }

    [TestMethod]
    public async Task Schedule_PolicyDoesNotRequireApproval_FlagOn_IsGated()
    {
        // Arrange
        var assignment = NewAssignment();
        var flags = new FakeFeatureFlagService { IsEnabledValue = true };
        var handler = NewScheduleHandler(
            assignment, flags, new FakeAssignmentPolicyResolver { Policy = PolicyWith(false) });

        // Act / Assert
        await FluentActions.Awaiting(() =>
                handler.HandleAsync(new ScheduleAssignmentCommand(assignment.Id, DateTimeOffset.UtcNow.AddDays(7))))
            .Should().ThrowAsync<AssignmentApprovalRequiredException>();

        assignment.Status.Should().Be(AssignmentStatus.Draft);
    }

    // ── (c) both off does not gate ────────────────────────────────────────

    [TestMethod]
    public async Task Publish_PolicyAndFlagOff_Publishes()
    {
        // Arrange
        var assignment = NewAssignment();
        var handler = NewPublishHandler(
            assignment, new FakeFeatureFlagService(), new FakeAssignmentPolicyResolver { Policy = PolicyWith(false) });

        // Act
        await handler.HandleAsync(new PublishAssignmentCommand(assignment.Id));

        // Assert
        assignment.Status.Should().Be(AssignmentStatus.Published);
    }

    [TestMethod]
    public async Task Schedule_PolicyAndFlagOff_Schedules()
    {
        // Arrange
        var assignment = NewAssignment();
        var availableFrom = DateTimeOffset.UtcNow.AddDays(7);
        var handler = NewScheduleHandler(
            assignment, new FakeFeatureFlagService(), new FakeAssignmentPolicyResolver { Policy = PolicyWith(false) });

        // Act
        await handler.HandleAsync(new ScheduleAssignmentCommand(assignment.Id, availableFrom));

        // Assert
        assignment.Status.Should().Be(AssignmentStatus.Scheduled);
        assignment.AvailableFromUtc.Should().Be(availableFrom);
    }

    // ── (d) fail-open: a policy outage must not turn the gate on ──────────

    [TestMethod]
    public async Task Publish_ResolverFailsOpen_FlagOff_Publishes()
    {
        // Arrange — BuiltInDefault is exactly what the HTTP resolver returns when both policy
        // fetches fail, so this pins the posture: a Settings/Students outage cannot silently
        // require approval.
        var assignment = NewAssignment();
        var handler = NewPublishHandler(assignment, new FakeFeatureFlagService(), new FakeAssignmentPolicyResolver());

        // Act
        await handler.HandleAsync(new PublishAssignmentCommand(assignment.Id));

        // Assert
        assignment.Status.Should().Be(AssignmentStatus.Published);
    }

    [TestMethod]
    public async Task Schedule_ResolverFailsOpen_FlagOff_Schedules()
    {
        // Arrange
        var assignment = NewAssignment();
        var availableFrom = DateTimeOffset.UtcNow.AddDays(7);
        var handler = NewScheduleHandler(assignment, new FakeFeatureFlagService(), new FakeAssignmentPolicyResolver());

        // Act
        await handler.HandleAsync(new ScheduleAssignmentCommand(assignment.Id, availableFrom));

        // Assert
        assignment.Status.Should().Be(AssignmentStatus.Scheduled);
    }

    // ── fakes (per-file, the repo's handler-test convention) ──────────────

    private sealed class StubTenantProvider(Guid tenantId) : ITenantProvider
    {
        private readonly TenantContext _ctx = new(tenantId, tenantId.ToString(), TenantType.School);
        public TenantContext GetTenantContext() => _ctx;
    }

    private sealed class StubContactResolver : IContactResolver
    {
        public Task<IReadOnlyList<SubscriberInfo>> ResolveSubscribersAsync(
            ResolveSubscribersRequest request, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SubscriberInfo>>([]);
    }

    private sealed class StubBroadcaster : IAssignmentNotificationBroadcaster
    {
        public Task BroadcastPublishedAsync(AssignmentPublishedContext context, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class StubActivityGroupLookup : IActivityGroupLookup
    {
        public Task<ActivityGroupRefDto[]> GetByIdsAsync(IReadOnlyList<Guid> activityGroupIds, CancellationToken ct = default) =>
            Task.FromResult(Array.Empty<ActivityGroupRefDto>());

        public Task<Guid[]> GetActiveMemberIdsAsync(IReadOnlyList<Guid> activityGroupIds, CancellationToken ct = default) =>
            Task.FromResult(Array.Empty<Guid>());
    }

    private sealed class StubTopicAssignmentLookup : ITopicAssignmentLookup
    {
        public Task<bool> IsTopicAssignedAsync(Guid? gradeLevelId, IReadOnlyList<Guid> activityGroupIds,
            Guid topicId, DateOnly effectiveDate, CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class StubLinkRepository : IAssignmentActivityGroupRepository
    {
        public Task<Guid[]> GetGroupIdsForAssignmentAsync(Guid assignmentId, CancellationToken ct = default) =>
            Task.FromResult(Array.Empty<Guid>());

        public Task ReplaceForAssignmentAsync(Guid assignmentId, Guid tenantId, IReadOnlyList<Guid> activityGroupIds, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<Guid[]> GetAssignmentIdsByGroupAsync(Guid activityGroupId, CancellationToken ct = default) =>
            Task.FromResult(Array.Empty<Guid>());

        public Task<AssignmentGroupSummaryDto[]> GetAssignmentsByGroupAsync(Guid activityGroupId, CancellationToken ct = default) =>
            Task.FromResult(Array.Empty<AssignmentGroupSummaryDto>());
    }

    private sealed class StubAssignmentRepository(Assignment assignment) : IAssignmentRepository
    {
        public Task<Assignment?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Assignment?>(assignment);
        public Task AddAsync(Assignment a, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(Assignment a, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Assignment a, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<AssignmentSummary>> ListAsync(AssignmentStatus? s, CancellationToken ct = default) =>
            Task.FromResult(new List<AssignmentSummary>());
        public void DetectChanges() { }
        public Task<List<AssignmentSweepCandidate>> ListScheduledForAutoPublishAsync(DateTimeOffset nowUtc, CancellationToken ct = default) =>
            Task.FromResult(new List<AssignmentSweepCandidate>());
        public Task<List<AssignmentSweepCandidate>> ListDueForArchiveAsync(DateTimeOffset nowUtc, CancellationToken ct = default) =>
            Task.FromResult(new List<AssignmentSweepCandidate>());
    }

    private sealed class StubSubmissionRepository : ISubmissionRepository
    {
        public Task<AssignmentRecipient?> GetRecipientAsync(Guid a, Guid c, CancellationToken ct = default) =>
            Task.FromResult<AssignmentRecipient?>(null);
        public void Add(AssignmentRecipient r) { }
        public void Update(AssignmentRecipient r) { }
        public Task<int> DeleteRecipientsForAssignmentAsync(Guid assignmentId, CancellationToken ct = default) => Task.FromResult(0);
        public Task<GuardianSubmissionGate?> GetGateAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<GuardianSubmissionGate?>(null);
        public Task<GuardianSubmissionGate?> GetGateByAssignmentStudentAsync(Guid a, Guid s, CancellationToken ct = default) =>
            Task.FromResult<GuardianSubmissionGate?>(null);
        public void Add(GuardianSubmissionGate g) { }
        public void Update(GuardianSubmissionGate g) { }
        public Task<List<GuardianSubmissionGate>> ListGatesForAssignmentAsync(Guid assignmentId, CancellationToken ct = default) =>
            Task.FromResult(new List<GuardianSubmissionGate>());
        public Task<AssignmentSubmission?> GetSubmissionAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<AssignmentSubmission?>(null);
        public Task<AssignmentSubmission?> GetSubmissionByAssignmentStudentAsync(Guid a, Guid s, CancellationToken ct = default) =>
            Task.FromResult<AssignmentSubmission?>(null);
        public void Add(AssignmentSubmission s) { }
        public void Update(AssignmentSubmission s) { }
        public void Add(AssignmentSubmissionVersion v) { }
        public void Add(SubmissionReview r) { }
        public void Add(SubmissionAnswer a) { }
        public void Add(SignatureEvent e) { }
        public Task<SignatureEvent?> GetSignatureEventByAssignmentStudentAsync(Guid a, Guid s, CancellationToken ct = default) =>
            Task.FromResult<SignatureEvent?>(null);
        public Task<List<AssignmentSubmission>> ListSubmissionEntitiesByAssignmentAsync(Guid a, CancellationToken ct = default) =>
            Task.FromResult(new List<AssignmentSubmission>());
        public Task<List<AssignmentRecipient>> ListRecipientEntitiesByAssignmentAsync(Guid a, CancellationToken ct = default) =>
            Task.FromResult(new List<AssignmentRecipient>());
        public Task<List<AssignmentSubmissionVersion>> ListVersionsForSubmissionIdsAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default) =>
            Task.FromResult(new List<AssignmentSubmissionVersion>());
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
        public Task<SubmissionForReviewDto[]> ListSubmissionsForReviewAsync(Guid t, CancellationToken ct = default) =>
            Task.FromResult(Array.Empty<SubmissionForReviewDto>());
        public Task<SubmissionForReviewDto[]> ListSubmissionsByAssignmentAsync(Guid a, CancellationToken ct = default) =>
            Task.FromResult(Array.Empty<SubmissionForReviewDto>());
        public Task<AssignmentRecipientDto[]> ListRecipientsForAssignmentAsync(Guid a, CancellationToken ct = default) =>
            Task.FromResult(Array.Empty<AssignmentRecipientDto>());
        public Task<SubmissionDetailDto?> GetSubmissionDetailAsync(Guid a, Guid s, CancellationToken ct = default) =>
            Task.FromResult<SubmissionDetailDto?>(null);
        public Task<GuardianGateDto?> GetGuardianGateAsync(Guid a, Guid s, CancellationToken ct = default) =>
            Task.FromResult<GuardianGateDto?>(null);
    }

    private sealed class StubHybridCache : HybridCache
    {
        public override ValueTask<T> GetOrCreateAsync<TState, T>(string key, TState state,
            Func<TState, CancellationToken, ValueTask<T>> factory, HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null, CancellationToken cancellationToken = default) =>
            factory(state, cancellationToken);

        public override ValueTask SetAsync<T>(string key, T value, HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }
}
