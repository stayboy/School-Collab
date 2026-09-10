using FluentAssertions;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.PublishAssignmentCommand;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.DTOs;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.Features;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// WS-A2 / spec §7 Q2 — the approval gate on
/// <see cref="PublishAssignmentCommandHandler"/>. Verifies the
/// flag-driven behaviour at the handler seam:
/// <list type="bullet">
///   <item>Flag ON + Draft not-approved → <see cref="AssignmentApprovalRequiredException"/> propagates, NO publishes.</item>
///   <item>Flag ON + Approved (after <see cref="Assignment.SubmitForApproval"/> + <see cref="Assignment.Approve"/>) → publishes.</item>
///   <item>Flag OFF + never-submitted → publishes (today's behaviour pinned).</item>
/// </list>
/// Uses a minimal handler construction — fake repos + lookup fakes return
/// the empty results so the publish path runs through to the cache step
/// without recipients/broadcast side effects.
/// </summary>
[TestClass]
public class PublishAssignmentApprovalGateTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000010");

    private static Assignment NewAssignment() =>
        Assignment.Create("Math", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, null, null, null, TeacherId)
            .WithTenant(TenantId);

    private static PublishAssignmentCommandHandler NewHandler(
        Assignment assignment, IFeatureFlagService featureFlags)
    {
        var assignmentRepo = new FakeAssignmentRepository { Assignment = assignment };
        var submissionRepo = new FakeSubmissionRepository();
        var contactResolver = new FakeContactResolver([]);
        var linkRepo = new FakeLinkRepository();
        var groupLookup = new FakeActivityGroupLookup();
        var topicLookup = new FakeTopicAssignmentLookup();
        var tenantProvider = new FakeTenantProvider(TenantId);
        var broadcaster = new FakeBroadcaster();
        var policyResolver = new FakeNotificationPolicyResolver();
        var cache = new FakeHybridCache();

        return new PublishAssignmentCommandHandler(
            assignmentRepo, submissionRepo, contactResolver, linkRepo, groupLookup,
            topicLookup, tenantProvider, broadcaster, policyResolver,
            featureFlags, cache,
            NullLogger<PublishAssignmentCommandHandler>.Instance);
    }

    [TestMethod]
    public async Task FlagOn_DraftNotApproved_Throws()
    {
        var assignment = NewAssignment();
        var flags = new FakeFeatureFlagService { IsEnabledValue = true };
        var handler = NewHandler(assignment, flags);

        await FluentActions.Awaiting(() => handler.HandleAsync(new PublishAssignmentCommand(assignment.Id)))
            .Should().ThrowAsync<AssignmentApprovalRequiredException>();

        assignment.Status.Should().Be(AssignmentStatus.Draft, "publish must roll back / not mutate when the guard throws");
    }

    [TestMethod]
    public async Task FlagOn_Approved_Publishes()
    {
        var assignment = NewAssignment();
        assignment.SubmitForApproval();
        assignment.Approve(Guid.NewGuid());
        var flags = new FakeFeatureFlagService { IsEnabledValue = true };
        var handler = NewHandler(assignment, flags);

        await handler.HandleAsync(new PublishAssignmentCommand(assignment.Id));

        assignment.Status.Should().Be(AssignmentStatus.Published);
        assignment.PublishedAt.Should().NotBeNull();
    }

    [TestMethod]
    public async Task FlagOff_NeverSubmitted_Publishes()
    {
        var assignment = NewAssignment();
        var flags = new FakeFeatureFlagService(); // OFF
        var handler = NewHandler(assignment, flags);

        await handler.HandleAsync(new PublishAssignmentCommand(assignment.Id));

        assignment.Status.Should().Be(AssignmentStatus.Published);
        assignment.PublishedAt.Should().NotBeNull();
    }

    // ── Fakes (cross-handler scope; mirrors ActivityGroupTests fakes) ───

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

    private sealed class FakeBroadcaster : IAssignmentNotificationBroadcaster
    {
        public AssignmentPublishedContext? Last { get; private set; }
        public Task BroadcastPublishedAsync(AssignmentPublishedContext context, CancellationToken ct = default)
        { Last = context; return Task.CompletedTask; }
    }

    private sealed class FakeActivityGroupLookup : IActivityGroupLookup
    {
        public Task<ActivityGroupRefDto[]> GetByIdsAsync(IReadOnlyList<Guid> activityGroupIds, CancellationToken ct = default)
            => Task.FromResult(Array.Empty<ActivityGroupRefDto>());
        public Task<Guid[]> GetActiveMemberIdsAsync(IReadOnlyList<Guid> activityGroupIds, CancellationToken ct = default)
            => Task.FromResult(Array.Empty<Guid>());
    }

    private sealed class FakeTopicAssignmentLookup : ITopicAssignmentLookup
    {
        public bool Result = true;
        public Task<bool> IsTopicAssignedAsync(Guid? gradeLevelId, IReadOnlyList<Guid> activityGroupIds,
            Guid topicId, DateOnly effectiveDate, CancellationToken ct = default)
            => Task.FromResult(Result);
    }

    private sealed class FakeLinkRepository : IAssignmentActivityGroupRepository
    {
        public Task<Guid[]> GetGroupIdsForAssignmentAsync(Guid assignmentId, CancellationToken ct = default)
            => Task.FromResult(Array.Empty<Guid>());
        public Task ReplaceForAssignmentAsync(Guid assignmentId, Guid tenantId, IReadOnlyList<Guid> activityGroupIds, CancellationToken ct = default)
            => Task.CompletedTask;
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
        public void DetectChanges() { }
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
