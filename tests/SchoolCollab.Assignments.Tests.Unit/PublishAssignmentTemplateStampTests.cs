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
using SchoolCollab.Core.Notifications;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain;
using static SchoolCollab.Assignments.Tests.Unit.PublishAssignmentCommandHandlerDeepLinkTestsFakes;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// Rounds notification-templates-b2a/b2c (spec §3.7 / §8) — the publish template-selection
/// lifecycle: an explicit request stamp is validated against the channel's live Publish
/// <b>candidates</b> and stamped on the assignment; a null request stamp <b>reuses</b> the stored one;
/// and a stored stamp that no longer validates degrades without blocking the publish.
/// <para>Round b2c: the gate is <b>existence</b> — a stamp naming a live candidate is accepted even
/// when the assignment's dimensions never intersect it, because applicability is the dialog's
/// pre-select guidance, not a 400. What these tests pin is the <b>lifecycle</b>: validation,
/// persistence, reuse and degradation.</para>
/// </summary>
[TestClass]
public class PublishAssignmentTemplateStampTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000010");
    private static readonly Guid StudentId = Guid.Parse("00000000-0000-0000-0000-000000000020");
    private static readonly Guid ContactId = Guid.Parse("00000000-0000-0000-0000-000000000030");

    private static readonly Guid LiveEmail = Guid.Parse("e0000000-0000-0000-0000-00000000000e");
    private static readonly Guid LiveSms = Guid.Parse("e0000000-0000-0000-0000-000000000055");

    private static readonly SubscriberInfo Subscriber =
        new(ContactId, ContactOwnerType.Student, StudentId, StudentId, ContactChannel.Email, null);

    private static Assignment NewAssignment() =>
        Assignment.Create("Math", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, null, null, TeacherId)
            .WithTenant(TenantId)
            .WithAllStudentsTarget();

    /// <summary>The tenant's two live Publish slots — the only stamps either channel can name.</summary>
    private static SlotOnlyTemplateResolver LiveSlots() => new(new Dictionary<NotificationChannel, Guid>
    {
        [NotificationChannel.Email] = LiveEmail,
        [NotificationChannel.SMS] = LiveSms,
    });

    private static PublishAssignmentCommandHandler NewHandler(
        Assignment assignment, INotificationTemplateResolver templateResolver) =>
        new(new StubAssignmentRepository(assignment), new StubSubmissionRepository(),
            new StubContactResolver([Subscriber]), new StubTopicAssignmentLookup(),
            new FakeAssignmentTargetResolver { StudentIds = [StudentId] },
            new StubTenantProvider(TenantId), new StubBroadcaster(),
            new FakeNotificationPolicyResolver(), new FakeAssignmentPolicyResolver(),
            new FakeFeatureFlagService(), new FakeDeepLinkTokenMinter(), templateResolver,
            new StubHybridCache(), NullLogger<PublishAssignmentCommandHandler>.Instance);

    // ── an explicit request stamp is persisted ──────────────────────────────────────────────

    [TestMethod]
    public async Task ExplicitStamps_ArePersistedOnTheAssignment()
    {
        var assignment = NewAssignment();
        var handler = NewHandler(assignment, LiveSlots());

        await handler.HandleAsync(new PublishAssignmentCommand(
            assignment.Id, PublishEmailTemplateId: LiveEmail, PublishSmsTemplateId: LiveSms));

        assignment.PublishEmailTemplateId.Should().Be(LiveEmail);
        assignment.PublishSmsTemplateId.Should().Be(LiveSms);
    }

    [TestMethod]
    public async Task NullRequestStamp_ReusesTheStoredOne()
    {
        // The pinned request semantics (§3.7): null = reuse, NOT clear.
        var assignment = NewAssignment();
        assignment.StampPublishTemplates(LiveEmail, LiveSms);
        var handler = NewHandler(assignment, LiveSlots());

        await handler.HandleAsync(new PublishAssignmentCommand(assignment.Id));

        assignment.PublishEmailTemplateId.Should().Be(LiveEmail);
        assignment.PublishSmsTemplateId.Should().Be(LiveSms);
    }

    [TestMethod]
    public async Task ClearedStamps_StayCleared_SoResolutionFallsBackToTheLiveTemplate()
    {
        // A cleared stamp is the fail-closed "no author choice" state; resetting the columns directly
        // is how B2a exercises the state the B2b clear operation will produce.
        var assignment = NewAssignment();
        assignment.StampPublishTemplates(null, null);
        var handler = NewHandler(assignment, LiveSlots());

        await handler.HandleAsync(new PublishAssignmentCommand(assignment.Id));

        assignment.PublishEmailTemplateId.Should().BeNull();
        assignment.PublishSmsTemplateId.Should().BeNull(
            "publish resolves the live template (or the built-in default); it must not invent a stamp");
    }

    // ── validation: explicit stamps only, and only when the slot is readable ────────────────

    /// <summary>
    /// Round b2c: a stamp naming a <b>live but non-applicable</b> candidate is <b>accepted</b>. The
    /// assignment targets all students and the candidate is constrained to one grade, so the matcher
    /// would never pick it — but existence is the only gate (§3.7): applicability is the dialog's
    /// pre-select guidance, not a 400.
    /// </summary>
    [TestMethod]
    public async Task ExplicitStamp_NamingALiveButNonApplicableCandidate_IsAccepted()
    {
        var assignment = NewAssignment();
        var constrained = new SlotOnlyTemplateResolver(
            new Dictionary<NotificationChannel, Guid> { [NotificationChannel.Email] = LiveEmail },
            constraints: new Dictionary<NotificationChannel, NotificationTemplateConstraint[]>
            {
                [NotificationChannel.Email] =
                    [new NotificationTemplateConstraint("GradeLevel", Guid.NewGuid().ToString("D"))],
            });
        var handler = NewHandler(assignment, constrained);

        await handler.HandleAsync(new PublishAssignmentCommand(
            assignment.Id, PublishEmailTemplateId: LiveEmail));

        assignment.Status.Should().Be(AssignmentStatus.Published);
        assignment.PublishEmailTemplateId.Should().Be(LiveEmail,
            "a live stamp is accepted even when the assignment's dimensions never intersect it");
    }

    [TestMethod]
    public async Task ExplicitStamp_ThatIsNotTheLiveTemplate_IsRejected_400()
    {
        // A deleted template, another tenant's id, or the wrong channel's id all land here: the
        // request stamp is author intent, so it fails loudly instead of silently falling back.
        var assignment = NewAssignment();
        var handler = NewHandler(assignment, LiveSlots());

        var act = () => handler.HandleAsync(new PublishAssignmentCommand(
            assignment.Id, PublishEmailTemplateId: Guid.NewGuid()));

        await act.Should().ThrowAsync<NotificationTemplateSelectionInvalidException>();
    }

    [TestMethod]
    public async Task ExplicitSmsStamp_NamingTheEmailTemplate_IsRejected()
    {
        // Kind/channel validation: the email slot's id is not the SMS slot's id.
        var assignment = NewAssignment();
        var handler = NewHandler(assignment, LiveSlots());

        var act = () => handler.HandleAsync(new PublishAssignmentCommand(
            assignment.Id, PublishSmsTemplateId: LiveEmail));

        await act.Should().ThrowAsync<NotificationTemplateSelectionInvalidException>();
    }

    [TestMethod]
    public async Task RejectedStamp_LeavesTheAssignmentUnpublished()
    {
        var assignment = NewAssignment();
        var handler = NewHandler(assignment, LiveSlots());

        var act = () => handler.HandleAsync(new PublishAssignmentCommand(
            assignment.Id, PublishEmailTemplateId: Guid.NewGuid()));

        await act.Should().ThrowAsync<NotificationTemplateSelectionInvalidException>();
        assignment.Status.Should().Be(AssignmentStatus.Draft,
            "validation runs before any state change, so a 400 leaves the draft untouched");
    }

    [TestMethod]
    public async Task ExplicitStamp_WhenNoLiveTemplateExists_IsRejected_400()
    {
        // WS5: an explicit stamp must name a live Publish template for its channel. When no live
        // template exists for that channel, the stamp cannot be matched and must fail loudly (400),
        // never persist an arbitrary Guid.
        var assignment = NewAssignment();
        var handler = NewHandler(assignment, new SlotOnlyTemplateResolver(new Dictionary<NotificationChannel, Guid>()));

        var act = () => handler.HandleAsync(new PublishAssignmentCommand(
            assignment.Id, PublishEmailTemplateId: LiveEmail));

        await act.Should().ThrowAsync<NotificationTemplateSelectionInvalidException>();
        assignment.PublishEmailTemplateId.Should().BeNull("a rejected stamp leaves the assignment untouched");
    }

    // ── Settings outage: fail-open on explicit stamps (§3.3) ────────────────────────────────

    [TestMethod]
    public async Task ExplicitStamp_DuringSettingsOutage_IsAcceptedAndPersisted()
    {
        // §3.3: a template outage must never block a publish. An Unreadable slot means validation
        // is skipped, so the author's explicit choice is persisted as-is — even if it does not
        // match any known live template.
        var assignment = NewAssignment();
        var requestedEmail = Guid.NewGuid();
        var handler = NewHandler(assignment, new SlotOnlyTemplateResolver(
            new Dictionary<NotificationChannel, Guid> { [NotificationChannel.Email] = LiveEmail },
            new HashSet<NotificationChannel> { NotificationChannel.Email }));

        await handler.HandleAsync(new PublishAssignmentCommand(
            assignment.Id, PublishEmailTemplateId: requestedEmail));

        assignment.Status.Should().Be(AssignmentStatus.Published);
        assignment.PublishEmailTemplateId.Should().Be(requestedEmail);
    }

    [TestMethod]
    public async Task GenuineCancellation_DuringTemplateValidation_Propagates()
    {
        // The new discriminated read must not swallow a real caller cancellation as Unreadable.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var assignment = NewAssignment();
        var handler = NewHandler(assignment, new CancellingTemplateResolver());

        var act = () => handler.HandleAsync(new PublishAssignmentCommand(
            assignment.Id, PublishEmailTemplateId: LiveEmail), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── a stale STORED stamp never blocks a publish ─────────────────────────────────────────

    [TestMethod]
    public async Task StaleStoredStamp_DoesNotBlockThePublish()
    {
        // §3.7: a deleted/foreign stored stamp degrades to the slot's live template (or the built-in
        // default) — never a 400, because it is history, not a request. It is also NOT erased here: a
        // transient Settings outage must not destroy the author's recorded choice.
        var assignment = NewAssignment();
        var deleted = Guid.Parse("d0000000-0000-0000-0000-00000000000d");
        assignment.StampPublishTemplates(deleted, null);
        var handler = NewHandler(assignment, LiveSlots());

        await handler.HandleAsync(new PublishAssignmentCommand(assignment.Id));

        assignment.Status.Should().Be(AssignmentStatus.Published);
        assignment.PublishEmailTemplateId.Should().Be(deleted,
            "the stamp is left intact; the render path resolves the slot independently and records what actually rendered");
    }

    [TestMethod]
    public async Task StaleStoredSmsStamp_DoesNotBlockThePublish()
    {
        var assignment = NewAssignment();
        assignment.StampPublishTemplates(null, Guid.Parse("d0000000-0000-0000-0000-00000000000d"));
        var handler = NewHandler(assignment, LiveSlots());

        await handler.HandleAsync(new PublishAssignmentCommand(assignment.Id));

        assignment.Status.Should().Be(AssignmentStatus.Published);
    }

    // ── §3.8: the medium restriction rides the same command ────────────────────────────────

    [TestMethod]
    public async Task EmailOnly_RestrictsTheBroadcastRecipients()
    {
        // End-to-end through the handler: the ContactIds subset filter runs first, then the medium
        // restriction narrows what the broadcaster receives.
        var emailSubscriber = Subscriber;
        var smsSubscriber = new SubscriberInfo(Guid.NewGuid(), ContactOwnerType.Student, StudentId,
            StudentId, ContactChannel.SMS, null);
        var assignment = NewAssignment();
        var broadcaster = new StubBroadcaster();
        var handler = new PublishAssignmentCommandHandler(
            new StubAssignmentRepository(assignment), new StubSubmissionRepository(),
            new StubContactResolver([emailSubscriber, smsSubscriber]), new StubTopicAssignmentLookup(),
            new FakeAssignmentTargetResolver { StudentIds = [StudentId] },
            new StubTenantProvider(TenantId), broadcaster,
            new FakeNotificationPolicyResolver(), new FakeAssignmentPolicyResolver(),
            new FakeFeatureFlagService(), new FakeDeepLinkTokenMinter(), LiveSlots(),
            new StubHybridCache(), NullLogger<PublishAssignmentCommandHandler>.Instance);

        await handler.HandleAsync(new PublishAssignmentCommand(assignment.Id, Channels: PublishChannel.EmailOnly));

        broadcaster.Last!.Recipients.Should().ContainSingle()
            .Which.Channel.Should().Be(ContactChannel.Email);
    }
}

/// <summary>The handler's collaborators, all inert: these tests assert the assignment's stamps and
/// the broadcast audience, not the recipient mint or the event outbox.</summary>
internal static class PublishAssignmentCommandHandlerDeepLinkTestsFakes
{
    internal sealed class StubAssignmentRepository(Assignment assignment) : IAssignmentRepository
    {
        public Task<Assignment?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Assignment?>(assignment);
        public Task AddAsync(Assignment a, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(Assignment a, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Assignment a, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<AssignmentSummary>> ListAsync(AssignmentStatus? status, CancellationToken ct = default)
            => Task.FromResult(new List<AssignmentSummary>());
        public void DetectChanges() { }
        public Task<List<AssignmentSweepCandidate>> ListScheduledForAutoPublishAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
            => Task.FromResult(new List<AssignmentSweepCandidate>());
        public Task<List<AssignmentSweepCandidate>> ListDueForArchiveAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
            => Task.FromResult(new List<AssignmentSweepCandidate>());
    }

    internal sealed class StubSubmissionRepository : ISubmissionRepository
    {
        public Task<AssignmentRecipient?> GetRecipientAsync(Guid assignmentId, Guid contactId, CancellationToken ct = default)
            => Task.FromResult<AssignmentRecipient?>(null);
        public void Add(AssignmentRecipient recipient) { }
        public void Update(AssignmentRecipient recipient) { }
        public Task<int> DeleteRecipientsForAssignmentAsync(Guid assignmentId, CancellationToken ct = default) => Task.FromResult(0);
        public Task<GuardianSubmissionGate?> GetGateAsync(Guid gateId, CancellationToken ct = default)
            => Task.FromResult<GuardianSubmissionGate?>(null);
        public Task<GuardianSubmissionGate?> GetGateByAssignmentStudentAsync(Guid assignmentId, Guid studentId, CancellationToken ct = default)
            => Task.FromResult<GuardianSubmissionGate?>(null);
        public void Add(GuardianSubmissionGate gate) { }
        public void Update(GuardianSubmissionGate gate) { }
        public Task<List<GuardianSubmissionGate>> ListGatesForAssignmentAsync(Guid assignmentId, CancellationToken ct = default)
            => Task.FromResult(new List<GuardianSubmissionGate>());
        public Task<AssignmentSubmission?> GetSubmissionAsync(Guid submissionId, CancellationToken ct = default)
            => Task.FromResult<AssignmentSubmission?>(null);
        public Task<AssignmentSubmission?> GetSubmissionByAssignmentStudentAsync(Guid assignmentId, Guid studentId, CancellationToken ct = default)
            => Task.FromResult<AssignmentSubmission?>(null);
        public void Add(AssignmentSubmission submission) { }
        public void Update(AssignmentSubmission submission) { }
        public void Add(AssignmentSubmissionVersion version) { }
        public void Add(SubmissionReview review) { }
        public void Add(SubmissionAnswer answer) { }
        public void Add(SignatureEvent signatureEvent) { }
        public Task<SignatureEvent?> GetSignatureEventByAssignmentStudentAsync(Guid assignmentId, Guid studentId, CancellationToken ct = default)
            => Task.FromResult<SignatureEvent?>(null);
        public Task<List<AssignmentSubmission>> ListSubmissionEntitiesByAssignmentAsync(Guid assignmentId, CancellationToken ct = default)
            => Task.FromResult(new List<AssignmentSubmission>());
        public Task<List<AssignmentRecipient>> ListRecipientEntitiesByAssignmentAsync(Guid assignmentId, CancellationToken ct = default)
            => Task.FromResult(new List<AssignmentRecipient>());
        public Task<List<AssignmentSubmissionVersion>> ListVersionsForSubmissionIdsAsync(IReadOnlyList<Guid> submissionIds, CancellationToken ct = default)
            => Task.FromResult(new List<AssignmentSubmissionVersion>());
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
        public Task<SubmissionForReviewDto[]> ListSubmissionsForReviewAsync(Guid teacherId, CancellationToken ct = default)
            => Task.FromResult(Array.Empty<SubmissionForReviewDto>());
        public Task<SubmissionForReviewDto[]> ListSubmissionsByAssignmentAsync(Guid assignmentId, CancellationToken ct = default)
            => Task.FromResult(Array.Empty<SubmissionForReviewDto>());
        public Task<AssignmentRecipientDto[]> ListRecipientsForAssignmentAsync(Guid assignmentId, CancellationToken ct = default)
            => Task.FromResult(Array.Empty<AssignmentRecipientDto>());
        public Task<SubmissionDetailDto?> GetSubmissionDetailAsync(Guid assignmentId, Guid studentId, CancellationToken ct = default)
            => Task.FromResult<SubmissionDetailDto?>(null);
        public Task<GuardianGateDto?> GetGuardianGateAsync(Guid assignmentId, Guid studentId, CancellationToken ct = default)
            => Task.FromResult<GuardianGateDto?>(null);
    }

    internal sealed class StubContactResolver(IReadOnlyList<SubscriberInfo> subscribers) : IContactResolver
    {
        public Task<IReadOnlyList<SubscriberInfo>> ResolveSubscribersAsync(
            ResolveSubscribersRequest request, CancellationToken ct = default) => Task.FromResult(subscribers);
    }

    internal sealed class StubTopicAssignmentLookup : ITopicAssignmentLookup
    {
        public Task<bool> IsTopicAssignedAsync(Guid? gradeLevelId, IReadOnlyList<Guid> activityGroupIds,
            Guid topicId, DateOnly effectiveDate, CancellationToken ct = default) => Task.FromResult(true);
    }

    internal sealed class StubTenantProvider(Guid tenantId) : ITenantProvider
    {
        private readonly TenantContext _context = new(tenantId, "SchoolA", TenantType.School);
        public TenantContext GetTenantContext() => _context;
    }

    internal sealed class StubBroadcaster : IAssignmentNotificationBroadcaster
    {
        public AssignmentPublishedContext? Last { get; private set; }

        public Task BroadcastPublishedAsync(AssignmentPublishedContext context, CancellationToken ct = default)
        {
            Last = context;
            return Task.CompletedTask;
        }
    }

    internal sealed class StubHybridCache : HybridCache
    {
        public override ValueTask<T> GetOrCreateAsync<TState, T>(string key, TState state,
            Func<TState, CancellationToken, ValueTask<T>> factory, HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
            => factory(state, cancellationToken);
        public override ValueTask SetAsync<T>(string key, T value, HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
        public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
        public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }

    /// <summary>The tenant's live Publish slots — the dictionary is the Found reality, the
    /// unreadable set is the Unreadable (Settings-outage) reality, and any other channel is NotSet.
    /// <paramref name="constraints"/> optionally constrains one channel's single candidate (the
    /// "live but not applicable" case, which validation must still accept).</summary>
    internal sealed class SlotOnlyTemplateResolver : INotificationTemplateResolver
    {
        private readonly Dictionary<NotificationChannel, Guid> _liveSlots;
        private readonly HashSet<NotificationChannel> _unreadableChannels;
        private readonly Dictionary<NotificationChannel, NotificationTemplateConstraint[]> _constraints;

        public SlotOnlyTemplateResolver(
            Dictionary<NotificationChannel, Guid> liveSlots,
            HashSet<NotificationChannel>? unreadableChannels = null,
            Dictionary<NotificationChannel, NotificationTemplateConstraint[]>? constraints = null)
        {
            _liveSlots = liveSlots;
            _unreadableChannels = unreadableChannels ?? [];
            _constraints = constraints ?? [];
        }

        public Task<NotificationTemplateCandidatesRead> ReadCandidatesAsync(
            NotificationKind kind, NotificationChannel channel, Guid tenantId,
            CancellationToken cancellationToken = default)
        {
            if (_unreadableChannels.Contains(channel))
            {
                return Task.FromResult(NotificationTemplateCandidatesRead.Unreadable);
            }

            return Task.FromResult(_liveSlots.TryGetValue(channel, out var id)
                ? NotificationTemplateCandidatesRead.Found(
                    [new NotificationTemplateCandidate(id, "Live subject", "<p>live</p>",
                        _constraints.TryGetValue(channel, out var constraints) ? constraints : [],
                        DateTimeOffset.UnixEpoch)])
                : NotificationTemplateCandidatesRead.NotSet);
        }
    }

    /// <summary>A resolver that exists only to prove genuine caller cancellation still propagates
    /// through the candidate read instead of being swallowed as Unreadable.</summary>
    internal sealed class CancellingTemplateResolver : INotificationTemplateResolver
    {
        public Task<NotificationTemplateCandidatesRead> ReadCandidatesAsync(
            NotificationKind kind, NotificationChannel channel, Guid tenantId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(NotificationTemplateCandidatesRead.NotSet);
        }
    }
}
