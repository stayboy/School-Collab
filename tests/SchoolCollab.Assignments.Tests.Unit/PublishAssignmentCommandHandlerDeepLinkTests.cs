using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.PublishAssignmentCommand;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.DTOs;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.DeepLinks;
using SchoolCollab.Core.Features;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// WS-E1 (ar-14-deep-links) — deep-link minting at the publish handler seam.
/// Verifies:
/// <list type="bullet">
///   <item>a NEW recipient always mints (expiry = mint + policy LinkValidityDays, or +7 when null);</item>
///   <item>republish REUSES an unexpired token (same string, no re-mint);</item>
///   <item>republish RE-MINTS when the token is absent or expired;</item>
///   <item>minting is independent of the FEATURE:EnableDeepLinks flag (OFF still mints).</item>
/// </list>
/// Uses the real <see cref="DeepLinkTokenMinter"/> (with a passthrough protector) for
/// expiry assertions and a counting <see cref="IDeepLinkTokenMinter"/> for reuse/re-mint.
/// </summary>
[TestClass]
public class PublishAssignmentCommandHandlerDeepLinkTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000010");
    private static readonly Guid StudentId = Guid.Parse("00000000-0000-0000-0000-000000000020");
    private static readonly Guid ContactStudent = Guid.Parse("00000000-0000-0000-0000-000000000030");

    private static readonly SubscriberInfo Subscriber =
        new(ContactStudent, ContactOwnerType.Student, StudentId, StudentId, ContactChannel.Email, null);

    private static Assignment NewAssignment() =>
        Assignment.Create("Math", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, null, null, null, TeacherId)
            .WithTenant(TenantId);

    private static PublishAssignmentCommandHandler NewHandler(
        FakeSubmissionRepository submissionRepo,
        FakeNotificationPolicyResolver policyResolver,
        IDeepLinkTokenMinter minter,
        IFeatureFlagService? flags = null)
    {
        var assignment = NewAssignment();
        var assignmentRepo = new FakeAssignmentRepository { Assignment = assignment };
        var contactResolver = new FakeContactResolver([Subscriber]);
        var linkRepo = new FakeLinkRepository();
        var groupLookup = new FakeActivityGroupLookup();
        var topicLookup = new FakeTopicAssignmentLookup();
        var tenantProvider = new FakeTenantProvider(TenantId);
        var broadcaster = new FakeBroadcaster();
        var cache = new FakeHybridCache();

        return new PublishAssignmentCommandHandler(
            assignmentRepo, submissionRepo, contactResolver, linkRepo, groupLookup,
            topicLookup, tenantProvider, broadcaster, policyResolver,
            flags ?? new FakeFeatureFlagService(), minter, cache,
            NullLogger<PublishAssignmentCommandHandler>.Instance);
    }

    [TestMethod]
    public void NewRecipient_MintsToken_ExpiryUsesPolicyValidity()
    {
        var submissionRepo = new FakeSubmissionRepository();
        var policy = new FakeNotificationPolicyResolver(linkValidityDays: 5);
        var minter = new DeepLinkTokenMinter(new DeepLinkProtector(new PassthroughProvider()));
        var handler = NewHandler(submissionRepo, policy, minter);
        var before = DateTimeOffset.UtcNow;

        handler.HandleAsync(new PublishAssignmentCommand(NewAssignment().Id)).GetAwaiter().GetResult();

        var recipient = submissionRepo.Added.Should().ContainSingle().Subject;
        recipient.DeepLinkToken.Should().NotBeNullOrWhiteSpace();
        recipient.DeepLinkExpiresAt.Should().NotBeNull();
        // expiry ≈ mint-time + 5 days (allow processing latency).
        recipient.DeepLinkExpiresAt.Should().BeCloseTo(before.AddDays(5), TimeSpan.FromMinutes(2));
    }

    [TestMethod]
    public void NewRecipient_MintsToken_NullValidityDefaultsToSevenDays()
    {
        var submissionRepo = new FakeSubmissionRepository();
        var policy = new FakeNotificationPolicyResolver(linkValidityDays: null);
        var minter = new DeepLinkTokenMinter(new DeepLinkProtector(new PassthroughProvider()));
        var handler = NewHandler(submissionRepo, policy, minter);
        var before = DateTimeOffset.UtcNow;

        handler.HandleAsync(new PublishAssignmentCommand(NewAssignment().Id)).GetAwaiter().GetResult();

        var recipient = submissionRepo.Added.Should().ContainSingle().Subject;
        recipient.DeepLinkExpiresAt.Should().BeCloseTo(before.AddDays(DeepLinkConstants.DefaultValidityDays), TimeSpan.FromMinutes(2));
    }

    [TestMethod]
    public void Minting_IsIndependentOfEnableDeepLinksFlag_OffStillMints()
    {
        var submissionRepo = new FakeSubmissionRepository();
        var policy = new FakeNotificationPolicyResolver();
        var flags = new FakeFeatureFlagService(); // EnableDeepLinks OFF
        var minter = new DeepLinkTokenMinter(new DeepLinkProtector(new PassthroughProvider()));
        var handler = NewHandler(submissionRepo, policy, minter, flags);

        handler.HandleAsync(new PublishAssignmentCommand(NewAssignment().Id)).GetAwaiter().GetResult();

        submissionRepo.Added.Should().ContainSingle().Subject.DeepLinkToken.Should().NotBeNullOrWhiteSpace();
    }

    [TestMethod]
    public void Republish_ReusesUnexpiredToken_SameStringNoReMint()
    {
        var existing = AssignmentRecipient.Create(TenantId, NewAssignment().Id, ContactOwnerType.Student,
            StudentId, StudentId, ContactStudent, ContactChannel.Email, null, true, true);
        existing.AttachDeepLink("unexpired-token", DateTimeOffset.UtcNow.AddDays(3));
        var seeding = new FakeSubmissionRepository();
        seeding.Seed(existing);

        var recording = new RecordingMinter();
        var handler = NewHandler(seeding, new FakeNotificationPolicyResolver(), recording);
        var originalToken = existing.DeepLinkToken;

        handler.HandleAsync(new PublishAssignmentCommand(NewAssignment().Id)).GetAwaiter().GetResult();

        recording.CallCount.Should().Be(0, "an unexpired token must not be re-minted on republish");
        existing.DeepLinkToken.Should().Be(originalToken);
    }

    [TestMethod]
    public void Republish_Remints_WhenExpired()
    {
        var existing = AssignmentRecipient.Create(TenantId, NewAssignment().Id, ContactOwnerType.Student,
            StudentId, StudentId, ContactStudent, ContactChannel.Email, null, true, true);
        existing.AttachDeepLink("expired-token", DateTimeOffset.UtcNow.AddDays(-1));
        var seeding = new FakeSubmissionRepository();
        seeding.Seed(existing);

        var recording = new RecordingMinter();
        var handler = NewHandler(seeding, new FakeNotificationPolicyResolver(), recording);

        handler.HandleAsync(new PublishAssignmentCommand(NewAssignment().Id)).GetAwaiter().GetResult();

        recording.CallCount.Should().Be(1);
        existing.DeepLinkToken.Should().Be("token-1");
        existing.DeepLinkExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [TestMethod]
    public void Republish_Remints_WhenTokenAbsent()
    {
        var existing = AssignmentRecipient.Create(TenantId, NewAssignment().Id, ContactOwnerType.Student,
            StudentId, StudentId, ContactStudent, ContactChannel.Email, null, true, true);
        // no DeepLinkToken attached (absent)
        var seeding = new FakeSubmissionRepository();
        seeding.Seed(existing);

        var recording = new RecordingMinter();
        var handler = NewHandler(seeding, new FakeNotificationPolicyResolver(), recording);

        handler.HandleAsync(new PublishAssignmentCommand(NewAssignment().Id)).GetAwaiter().GetResult();

        recording.CallCount.Should().Be(1);
        existing.DeepLinkToken.Should().Be("token-1");
        existing.DeepLinkExpiresAt.Should().NotBeNull();
    }

    // ── Fakes (handler construction) ─────────────────────────────────────────

    private sealed class RecordingMinter : IDeepLinkTokenMinter
    {
        public int CallCount { get; private set; }
        public DeepLinkMintedToken Mint(AssignmentRecipient recipient, int? linkValidityDays, DateTimeOffset now)
        {
            CallCount++;
            return new DeepLinkMintedToken($"token-{CallCount}", now.AddDays(linkValidityDays ?? 7));
        }
    }

    private sealed class FakeSubmissionRepository : ISubmissionRepository
    {
        public List<AssignmentRecipient> Added { get; } = new();
        private AssignmentRecipient? _existing;

        public void Seed(AssignmentRecipient recipient) => _existing = recipient;

        public Task<AssignmentRecipient?> GetRecipientAsync(Guid assignmentId, Guid contactId, CancellationToken ct = default)
            => Task.FromResult(_existing);
        public void Add(AssignmentRecipient r) => Added.Add(r);
        public void Update(AssignmentRecipient r) { }

        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
        public Task<GuardianSubmissionGate?> GetGateAsync(Guid id, CancellationToken ct = default) => Task.FromResult<GuardianSubmissionGate?>(null);
        public Task<GuardianSubmissionGate?> GetGateByAssignmentStudentAsync(Guid a, Guid s, CancellationToken ct = default) => Task.FromResult<GuardianSubmissionGate?>(null);
        public void Add(GuardianSubmissionGate g) { }
        public void Update(GuardianSubmissionGate g) { }
        public Task<List<GuardianSubmissionGate>> ListGatesForAssignmentAsync(Guid assignmentId, CancellationToken ct = default) => Task.FromResult(new List<GuardianSubmissionGate>());
        public Task<int> DeleteRecipientsForAssignmentAsync(Guid assignmentId, CancellationToken ct = default) => Task.FromResult(0);
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
        public Task<SubmissionForReviewDto[]> ListSubmissionsForReviewAsync(Guid t, CancellationToken ct = default) => Task.FromResult(Array.Empty<SubmissionForReviewDto>());
        public Task<SubmissionForReviewDto[]> ListSubmissionsByAssignmentAsync(Guid a, CancellationToken ct = default) => Task.FromResult(Array.Empty<SubmissionForReviewDto>());
        public Task<AssignmentRecipientDto[]> ListRecipientsForAssignmentAsync(Guid a, CancellationToken ct = default) => Task.FromResult(Array.Empty<AssignmentRecipientDto>());
        public Task<SubmissionDetailDto?> GetSubmissionDetailAsync(Guid a, Guid s, CancellationToken ct = default) => Task.FromResult<SubmissionDetailDto?>(null);
        public Task<GuardianGateDto?> GetGuardianGateAsync(Guid a, Guid s, CancellationToken ct = default) => Task.FromResult<GuardianGateDto?>(null);
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

    private sealed class FakeBroadcaster : IAssignmentNotificationBroadcaster
    {
        public Task BroadcastPublishedAsync(AssignmentPublishedContext context, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeActivityGroupLookup : IActivityGroupLookup
    {
        public Task<ActivityGroupRefDto[]> GetByIdsAsync(IReadOnlyList<Guid> activityGroupIds, CancellationToken ct = default) => Task.FromResult(Array.Empty<ActivityGroupRefDto>());
        public Task<Guid[]> GetActiveMemberIdsAsync(IReadOnlyList<Guid> activityGroupIds, CancellationToken ct = default) => Task.FromResult(Array.Empty<Guid>());
    }

    private sealed class FakeTopicAssignmentLookup : ITopicAssignmentLookup
    {
        public Task<bool> IsTopicAssignedAsync(Guid? gradeLevelId, IReadOnlyList<Guid> activityGroupIds, Guid topicId, DateOnly effectiveDate, CancellationToken ct = default)
            => Task.FromResult(true);
    }

    private sealed class FakeLinkRepository : IAssignmentActivityGroupRepository
    {
        public Task<Guid[]> GetGroupIdsForAssignmentAsync(Guid assignmentId, CancellationToken ct = default) => Task.FromResult(Array.Empty<Guid>());
        public Task ReplaceForAssignmentAsync(Guid assignmentId, Guid tenantId, IReadOnlyList<Guid> activityGroupIds, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Guid[]> GetAssignmentIdsByGroupAsync(Guid activityGroupId, CancellationToken ct = default) => Task.FromResult(Array.Empty<Guid>());
        public Task<AssignmentGroupSummaryDto[]> GetAssignmentsByGroupAsync(Guid activityGroupId, CancellationToken ct = default) => Task.FromResult(Array.Empty<AssignmentGroupSummaryDto>());
    }

    private sealed class FakeAssignmentRepository : IAssignmentRepository
    {
        public Assignment? Assignment;
        public Task<Assignment?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Assignment);
        public Task AddAsync(Assignment a, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(Assignment a, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Assignment a, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<AssignmentSummary>> ListAsync(AssignmentStatus? s, CancellationToken ct = default) => Task.FromResult(new List<AssignmentSummary>());
        public void DetectChanges() { }
        public Task<List<AssignmentSweepCandidate>> ListScheduledForAutoPublishAsync(DateTimeOffset nowUtc, CancellationToken ct = default) => Task.FromResult(new List<AssignmentSweepCandidate>());
        public Task<List<AssignmentSweepCandidate>> ListDueForArchiveAsync(DateTimeOffset nowUtc, CancellationToken ct = default) => Task.FromResult(new List<AssignmentSweepCandidate>());
    }

    private sealed class FakeHybridCache : HybridCache
    {
        public override ValueTask<T> GetOrCreateAsync<TState, T>(string key, TState state, Func<TState, CancellationToken, ValueTask<T>> factory, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
            => factory(state, cancellationToken);
        public override ValueTask SetAsync<T>(string key, T value, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
        public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    // ── Passthrough DataProtection (lets the real minter compute expiry without a real keyring) ──

    private sealed class PassthroughProvider : IDataProtectionProvider
    {
        public IDataProtector CreateProtector(string purpose) => new PassthroughProtector();
    }

    private sealed class PassthroughProtector : IDataProtector
    {
        public IDataProtector CreateProtector(string purpose) => this;
        public byte[] Protect(byte[] plaintext) => plaintext;
        public byte[] Unprotect(byte[] protectedData) => protectedData;
    }
}
