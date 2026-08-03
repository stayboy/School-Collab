using FluentAssertions;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging.Abstractions;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.CreateStudentSubmission;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.PublishAssignmentCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.ReviewSubmission;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.ReviewSubmissionGate;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.SubmitAssignmentOnBehalf;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.UnpublishAssignmentCommand;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.DTOs;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetSubmission;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.ListAssignmentRecipients;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.ListSubmissionsByAssignment;

namespace SchoolCollab.Assignments.Tests.Unit;

[TestClass]
public class SubmissionEngineTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000010");
    private static readonly Guid GradeLevelId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid StudentId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid GuardianId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid AssignmentId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid ContactStudent = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid ContactGuardian = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid OtherTeacherId = Guid.Parse("00000000-0000-0000-0000-000000000099");

    private static ITenantProvider TenantProvider() => new FakeTenantProvider(TenantId);
    private static HybridCache Cache() => new FakeHybridCache();

    private static Assignment NewAssignment(bool mandatoryReview = true)
    {
        var a = Assignment.Create("Math", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, GradeLevelId, null, null, TeacherId)
            .WithTenant(TenantProvider());
        if (!mandatoryReview)
            typeof(Assignment).GetProperty(nameof(Assignment.MandatoryReview))!.SetValue(a, false);
        return a;
    }

    // ── Domain entity tests ──────────────────────────────────────────────────

    [TestMethod]
    public void Assignment_Publish_SetsPublishedAt()
    {
        var a = NewAssignment();
        a.Publish();
        a.Status.Should().Be(AssignmentStatus.Published);
        a.PublishedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public void GuardianSubmissionGate_Review_ApproveEnables_DenyDisables()
    {
        var gate = GuardianSubmissionGate.Create(TenantId, AssignmentId, StudentId);

        gate.Review(GuardianId, approve: true, "looks good");
        gate.SubmissionEnabledForStudent.Should().BeTrue();
        gate.ReviewedByGuardianId.Should().Be(GuardianId);
        gate.ReviewComment.Should().Be("looks good");

        var gate2 = GuardianSubmissionGate.Create(TenantId, AssignmentId, StudentId);
        gate2.Review(GuardianId, approve: false, "not yet");
        gate2.SubmissionEnabledForStudent.Should().BeFalse();
        gate2.ReviewedByGuardianId.Should().Be(GuardianId);
    }

    [TestMethod]
    public void GuardianSubmissionGate_SubmitOnBehalf_SetsGuardian()
    {
        var gate = GuardianSubmissionGate.Create(TenantId, AssignmentId, StudentId);
        gate.SubmitOnBehalf(GuardianId, "done");
        gate.SubmittedByGuardianId.Should().Be(GuardianId);
        gate.SubmittedByGuardianAt.Should().NotBeNull();
        gate.SubmissionEnabledForStudent.Should().BeTrue();
    }

    [TestMethod]
    public void AssignmentSubmission_RecordSubmission_BumpsVersion()
    {
        var s = AssignmentSubmission.Create(TenantId, AssignmentId, StudentId, null);
        s.RecordSubmission(1, SubmissionSource.GuardianOnBehalf, GuardianId, DateTimeOffset.UtcNow);
        s.CurrentVersionNumber.Should().Be(1);
        s.CurrentSource.Should().Be(SubmissionSource.GuardianOnBehalf);
        s.SubmittedByGuardianId.Should().Be(GuardianId);
    }

    [TestMethod]
    public void AssignmentSubmission_ApplyReview_SetsState()
    {
        var s = AssignmentSubmission.Create(TenantId, AssignmentId, StudentId, null);
        s.ApplyReview(ReviewState.Graded);
        s.ReviewState.Should().Be(ReviewState.Graded);
        s.ApplyReview(ReviewState.Reviewed);
        s.ReviewState.Should().Be(ReviewState.Reviewed);
    }

    [TestMethod]
    public void AssignmentRecipient_Create_And_MarkSubscribed()
    {
        var r = AssignmentRecipient.Create(TenantId, AssignmentId, ContactOwnerType.Guardian, GuardianId,
            StudentId, ContactGuardian, ContactChannel.Email, GuardianRole.Primary, true, true);
        r.NotifyOnBroadcast.Should().BeTrue();
        r.SubscriptionActive.Should().BeTrue();
        r.Role.Should().Be(GuardianRole.Primary);

        r.MarkSubscribed(true);
        r.SubscriptionActive.Should().BeTrue();
    }

    // ── Handler orchestration tests (faked repositories) ─────────────────────

    [TestMethod]
    public async Task PublishAssignment_ResolvesRecipientsAndCreatesGate_WhenMandatoryReview()
    {
        var assignment = NewAssignment(mandatoryReview: true);
        var subscribers = new List<SubscriberInfo>
        {
            new(ContactStudent, ContactOwnerType.Student, StudentId, StudentId, ContactChannel.Email, null),
            new(ContactGuardian, ContactOwnerType.Guardian, GuardianId, StudentId, ContactChannel.Email, GuardianRole.Primary),
        };

        var assignmentRepo = new FakeAssignmentRepository { Assignment = assignment };
        var submissionRepo = new FakeSubmissionRepository();
        var broadcaster = new FakeBroadcaster();

        var handler = new PublishAssignmentCommandHandler(
            assignmentRepo, submissionRepo, new FakeContactResolver(subscribers),
            new FakeLinkRepository(), new FakeActivityGroupLookup(),
            TenantProvider(), broadcaster, Cache(), NullLogger<PublishAssignmentCommandHandler>.Instance);

        await handler.HandleAsync(new PublishAssignmentCommand(assignment.Id));

        assignment.PublishedAt.Should().NotBeNull();
        assignment.Status.Should().Be(AssignmentStatus.Published);
        submissionRepo.AddedRecipients.Should().HaveCount(2);
        submissionRepo.AddedRecipients.Should().Contain(r => r.OwnerType == ContactOwnerType.Student && r.ContactId == ContactStudent);
        submissionRepo.AddedRecipients.Should().Contain(r => r.OwnerType == ContactOwnerType.Guardian && r.ContactId == ContactGuardian && r.Role == GuardianRole.Primary);
        submissionRepo.AddedGates.Should().ContainSingle(g => g.StudentId == StudentId && g.SubmissionEnabledForStudent == false);
        broadcaster.BroadcastCount.Should().Be(1);
        broadcaster.Last!.Recipients.Should().HaveCount(2);
    }

    [TestMethod]
    public async Task PublishAssignment_NoGate_WhenNotMandatoryReview()
    {
        var assignment = NewAssignment(mandatoryReview: false);
        var subscribers = new List<SubscriberInfo>
        {
            new(ContactGuardian, ContactOwnerType.Guardian, GuardianId, StudentId, ContactChannel.Email, GuardianRole.Primary),
        };

        var assignmentRepo = new FakeAssignmentRepository { Assignment = assignment };
        var submissionRepo = new FakeSubmissionRepository();
        var handler = new PublishAssignmentCommandHandler(
            assignmentRepo, submissionRepo, new FakeContactResolver(subscribers),
            new FakeLinkRepository(), new FakeActivityGroupLookup(),
            TenantProvider(), new FakeBroadcaster(), Cache(), NullLogger<PublishAssignmentCommandHandler>.Instance);

        await handler.HandleAsync(new PublishAssignmentCommand(assignment.Id));

        submissionRepo.AddedRecipients.Should().HaveCount(1);
        submissionRepo.AddedGates.Should().BeEmpty();
    }

    [TestMethod]
    public async Task SubmitAssignmentOnBehalf_CreatesSubmissionAndRecordsGate()
    {
        var enabledGate = GuardianSubmissionGate.Create(TenantId, AssignmentId, StudentId);
        enabledGate.Review(GuardianId, approve: true, null); // guardian reviewed → enabled

        var submissionRepo = new FakeSubmissionRepository { GateToReturn = enabledGate, SubmissionToReturn = null };
        var handler = new SubmitAssignmentOnBehalfCommandHandler(
            submissionRepo, TenantProvider(), NullLogger<SubmitAssignmentOnBehalfCommandHandler>.Instance);

        await handler.HandleAsync(new SubmitAssignmentOnBehalfCommand(AssignmentId, StudentId, GuardianId, "my work"));

        submissionRepo.AddedSubmissions.Should().ContainSingle();
        submissionRepo.AddedVersions.Should().ContainSingle(v =>
            v.Source == SubmissionSource.GuardianOnBehalf && v.SubmittedByGuardianId == GuardianId && v.Content == "my work");
        submissionRepo.UpdatedSubmissions.Should().ContainSingle(s => s.CurrentVersionNumber == 1);
        submissionRepo.UpdatedGates.Should().ContainSingle(g => g.SubmittedByGuardianId == GuardianId);
    }

    [TestMethod]
    public async Task SubmitAssignmentOnBehalf_Throws_WhenGateNotEnabled()
    {
        var disabledGate = GuardianSubmissionGate.Create(TenantId, AssignmentId, StudentId); // not reviewed
        var submissionRepo = new FakeSubmissionRepository { GateToReturn = disabledGate };
        var handler = new SubmitAssignmentOnBehalfCommandHandler(
            submissionRepo, TenantProvider(), NullLogger<SubmitAssignmentOnBehalfCommandHandler>.Instance);

        var act = async () => await handler.HandleAsync(new SubmitAssignmentOnBehalfCommand(AssignmentId, StudentId, GuardianId, "x"));
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [TestMethod]
    public async Task ReviewSubmission_SetsGraded_WhenScorePresent()
    {
        var submission = AssignmentSubmission.Create(TenantId, AssignmentId, StudentId, null);
        var submissionRepo = new FakeSubmissionRepository { SubmissionToReturn = submission };
        var assignmentRepo = new FakeAssignmentRepository { Assignment = NewAssignment() };
        var handler = new ReviewSubmissionCommandHandler(
            submissionRepo, assignmentRepo, TenantProvider(), NullLogger<ReviewSubmissionCommandHandler>.Instance);

        await handler.HandleAsync(new ReviewSubmissionCommand(submission.Id, TeacherId, 95m, null, "great"));

        submissionRepo.AddedReviews.Should().ContainSingle(r => r.Score == 95m && r.TeacherId == TeacherId);
        submissionRepo.UpdatedSubmissions.Should().ContainSingle(s => s.ReviewState == ReviewState.Graded);
    }

    [TestMethod]
    public async Task ReviewSubmission_SetsReviewed_WhenNoOutcome()
    {
        var submission = AssignmentSubmission.Create(TenantId, AssignmentId, StudentId, null);
        var submissionRepo = new FakeSubmissionRepository { SubmissionToReturn = submission };
        var assignmentRepo = new FakeAssignmentRepository { Assignment = NewAssignment() };
        var handler = new ReviewSubmissionCommandHandler(
            submissionRepo, assignmentRepo, TenantProvider(), NullLogger<ReviewSubmissionCommandHandler>.Instance);

        await handler.HandleAsync(new ReviewSubmissionCommand(submission.Id, TeacherId, null, null, "ok"));

        submissionRepo.UpdatedSubmissions.Should().ContainSingle(s => s.ReviewState == ReviewState.Reviewed);
    }

    [TestMethod]
    public async Task ReviewSubmissionGate_Approve_EnablesStudent()
    {
        var gate = GuardianSubmissionGate.Create(TenantId, AssignmentId, StudentId);
        var submissionRepo = new FakeSubmissionRepository { GateToReturn = gate };
        var handler = new ReviewSubmissionGateCommandHandler(
            submissionRepo, NullLogger<ReviewSubmissionGateCommandHandler>.Instance);

        await handler.HandleAsync(new ReviewSubmissionGateCommand(gate.Id, GuardianId, true, "approved"));

        submissionRepo.UpdatedGates.Should().ContainSingle(g =>
            g.SubmissionEnabledForStudent && g.ReviewedByGuardianId == GuardianId);
    }

    [TestMethod]
    public async Task ReviewSubmissionGate_Deny_KeepsDisabled()
    {
        var gate = GuardianSubmissionGate.Create(TenantId, AssignmentId, StudentId);
        var submissionRepo = new FakeSubmissionRepository { GateToReturn = gate };
        var handler = new ReviewSubmissionGateCommandHandler(
            submissionRepo, NullLogger<ReviewSubmissionGateCommandHandler>.Instance);

        await handler.HandleAsync(new ReviewSubmissionGateCommand(gate.Id, GuardianId, false, "denied"));

        submissionRepo.UpdatedGates.Should().ContainSingle(g =>
            !g.SubmissionEnabledForStudent && g.ReviewedByGuardianId == GuardianId);
    }

    [TestMethod]
    public async Task ReviewSubmission_RejectsNonCreatorTeacher()
    {
        var submission = AssignmentSubmission.Create(TenantId, AssignmentId, StudentId, null);
        var submissionRepo = new FakeSubmissionRepository { SubmissionToReturn = submission };
        var assignmentRepo = new FakeAssignmentRepository { Assignment = NewAssignment() }; // CreatedBy = TeacherId
        var handler = new ReviewSubmissionCommandHandler(
            submissionRepo, assignmentRepo, TenantProvider(), NullLogger<ReviewSubmissionCommandHandler>.Instance);

        var act = async () => await handler.HandleAsync(
            new ReviewSubmissionCommand(submission.Id, OtherTeacherId, 95m, null, "x"));
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [TestMethod]
    public async Task PublishAssignment_DeduplicatesByContact()
    {
        var existing = AssignmentRecipient.Create(TenantId, AssignmentId, ContactOwnerType.Guardian,
            GuardianId, StudentId, ContactGuardian, ContactChannel.Email, GuardianRole.Primary, true, false);
        var assignment = NewAssignment();
        var subscribers = new List<SubscriberInfo>
        {
            new(ContactGuardian, ContactOwnerType.Guardian, GuardianId, StudentId, ContactChannel.Email, GuardianRole.Primary),
        };

        var assignmentRepo = new FakeAssignmentRepository { Assignment = assignment };
        var submissionRepo = new FakeSubmissionRepository { RecipientToReturn = existing };
        var broadcaster = new FakeBroadcaster();
        var handler = new PublishAssignmentCommandHandler(
            assignmentRepo, submissionRepo, new FakeContactResolver(subscribers),
            new FakeLinkRepository(), new FakeActivityGroupLookup(),
            TenantProvider(), broadcaster, Cache(), NullLogger<PublishAssignmentCommandHandler>.Instance);

        await handler.HandleAsync(new PublishAssignmentCommand(assignment.Id));

        submissionRepo.AddedRecipients.Should().BeEmpty();               // no duplicate add
        submissionRepo.UpdatedRecipients.Should().ContainSingle(r => r.ContactId == ContactGuardian && r.SubscriptionActive);
        broadcaster.Last!.Recipients.Should().ContainSingle();
    }

    [TestMethod]
    public async Task SubmitAssignmentOnBehalf_ResubmissionBumpsVersion()
    {
        var enabledGate = GuardianSubmissionGate.Create(TenantId, AssignmentId, StudentId);
        enabledGate.Review(GuardianId, approve: true, null);

        var submissionRepo = new FakeSubmissionRepository { GateToReturn = enabledGate, SubmissionToReturn = null };
        var handler = new SubmitAssignmentOnBehalfCommandHandler(
            submissionRepo, TenantProvider(), NullLogger<SubmitAssignmentOnBehalfCommandHandler>.Instance);

        await handler.HandleAsync(new SubmitAssignmentOnBehalfCommand(AssignmentId, StudentId, GuardianId, "v1"));
        submissionRepo.SubmissionToReturn = submissionRepo.AddedSubmissions[0]; // simulate persistence for call 2
        await handler.HandleAsync(new SubmitAssignmentOnBehalfCommand(AssignmentId, StudentId, GuardianId, "v2"));

        submissionRepo.AddedVersions.Should().HaveCount(2);
        submissionRepo.AddedVersions[1].VersionNumber.Should().Be(2);
        submissionRepo.AddedVersions[1].Content.Should().Be("v2");
        submissionRepo.AddedSubmissions.Should().ContainSingle();              // created once
        submissionRepo.SubmissionToReturn!.CurrentVersionNumber.Should().Be(2);
    }

    [TestMethod]
    public async Task CreateStudentSubmission_AllowedWhenNotMandatory()
    {
        var assignment = NewAssignment(mandatoryReview: false);
        var assignmentRepo = new FakeAssignmentRepository { Assignment = assignment };
        var submissionRepo = new FakeSubmissionRepository();
        var handler = new CreateStudentSubmissionCommandHandler(
            assignmentRepo, submissionRepo, TenantProvider(), NullLogger<CreateStudentSubmissionCommandHandler>.Instance);

        await handler.HandleAsync(new CreateStudentSubmissionCommand(AssignmentId, StudentId, "my work"));

        submissionRepo.AddedSubmissions.Should().ContainSingle();
        submissionRepo.AddedVersions.Should().ContainSingle(v =>
            v.Source == SubmissionSource.Student && v.VersionNumber == 1 && v.Content == "my work");
        submissionRepo.UpdatedSubmissions.Should().ContainSingle(s => s.CurrentVersionNumber == 1);
    }

    [TestMethod]
    public async Task CreateStudentSubmission_RejectedWhenMandatoryAndGateDisabled()
    {
        var assignment = NewAssignment(); // MandatoryReview = true
        var assignmentRepo = new FakeAssignmentRepository { Assignment = assignment };
        var gate = GuardianSubmissionGate.Create(TenantId, AssignmentId, StudentId); // not reviewed → disabled
        var submissionRepo = new FakeSubmissionRepository { GateToReturn = gate };
        var handler = new CreateStudentSubmissionCommandHandler(
            assignmentRepo, submissionRepo, TenantProvider(), NullLogger<CreateStudentSubmissionCommandHandler>.Instance);

        var act = async () => await handler.HandleAsync(new CreateStudentSubmissionCommand(AssignmentId, StudentId, "x"));
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [TestMethod]
    public async Task CreateStudentSubmission_AllowedWhenMandatoryAndGateEnabled()
    {
        var assignment = NewAssignment(); // MandatoryReview = true
        var assignmentRepo = new FakeAssignmentRepository { Assignment = assignment };
        var gate = GuardianSubmissionGate.Create(TenantId, AssignmentId, StudentId);
        gate.Review(GuardianId, approve: true, null); // enabled
        var submissionRepo = new FakeSubmissionRepository { GateToReturn = gate };
        var handler = new CreateStudentSubmissionCommandHandler(
            assignmentRepo, submissionRepo, TenantProvider(), NullLogger<CreateStudentSubmissionCommandHandler>.Instance);

        await handler.HandleAsync(new CreateStudentSubmissionCommand(AssignmentId, StudentId, "my work"));

        submissionRepo.AddedVersions.Should().ContainSingle(v => v.Source == SubmissionSource.Student && v.VersionNumber == 1);
        submissionRepo.UpdatedSubmissions.Should().ContainSingle(s => s.CurrentVersionNumber == 1);
    }

    [TestMethod]
    public async Task PublishAssignment_ContactSelection_FiltersToSubset()
    {
        var assignment = NewAssignment();
        var subscribers = new List<SubscriberInfo>
        {
            new(ContactStudent, ContactOwnerType.Student, StudentId, StudentId, ContactChannel.Email, null),
            new(ContactGuardian, ContactOwnerType.Guardian, GuardianId, StudentId, ContactChannel.Email, GuardianRole.Primary),
        };
        var assignmentRepo = new FakeAssignmentRepository { Assignment = assignment };
        var submissionRepo = new FakeSubmissionRepository();
        var broadcaster = new FakeBroadcaster();
        var handler = new PublishAssignmentCommandHandler(
            assignmentRepo, submissionRepo, new FakeContactResolver(subscribers),
            new FakeLinkRepository(), new FakeActivityGroupLookup(),
            TenantProvider(), broadcaster, Cache(), NullLogger<PublishAssignmentCommandHandler>.Instance);

        // Select only the guardian contact (spec §8).
        await handler.HandleAsync(new PublishAssignmentCommand(assignment.Id, new[] { ContactGuardian }));

        submissionRepo.AddedRecipients.Should().ContainSingle(r => r.ContactId == ContactGuardian);
        broadcaster.Last!.Recipients.Should().ContainSingle();
    }

    [TestMethod]
    public async Task Unpublish_RebuildsRecipientsAndResetsGate()
    {
        var assignment = NewAssignment();
        assignment.Publish(); // Unpublish() requires Published status
        var assignmentRepo = new FakeAssignmentRepository { Assignment = assignment };
        var gate = GuardianSubmissionGate.Create(TenantId, AssignmentId, StudentId);
        gate.Review(GuardianId, approve: true, "ok"); // enabled + reviewed
        var submissionRepo = new FakeSubmissionRepository();
        submissionRepo.GatesForAssignment.Add(gate);
        var handler = new UnpublishAssignmentCommandHandler(
            assignmentRepo, submissionRepo, new FakePublisher(), Cache(), NullLogger<UnpublishAssignmentCommandHandler>.Instance);

        await handler.HandleAsync(new UnpublishAssignmentCommand(AssignmentId));

        assignment.Status.Should().Be(AssignmentStatus.Draft);
        submissionRepo.DeletedRecipientsCount.Should().Be(1);          // recipients rebuilt
        gate.SubmissionEnabledForStudent.Should().BeFalse();            // gate reset
        gate.ReviewedByGuardianId.Should().BeNull();
        submissionRepo.UpdatedGates.Should().ContainSingle(g => !g.SubmissionEnabledForStudent);
    }

    [TestMethod]
    public async Task ListAssignmentRecipients_ReturnsRecipients()
    {
        var submissionRepo = new FakeSubmissionRepository
        {
            RecipientsForAssignment = new[]
            {
                new AssignmentRecipientDto(Guid.NewGuid(), AssignmentId, ContactOwnerTypeDto.Guardian, GuardianId, StudentId, ContactGuardian, ContactChannelDto.Email, GuardianRoleDto.Primary, true, true),
                new AssignmentRecipientDto(Guid.NewGuid(), AssignmentId, ContactOwnerTypeDto.Student, StudentId, null, ContactStudent, ContactChannelDto.SMS, null, true, true),
            }
        };
        var handler = new ListAssignmentRecipientsHandler(submissionRepo, NullLogger<ListAssignmentRecipientsHandler>.Instance);

        var result = await handler.HandleAsync(new ListAssignmentRecipients(AssignmentId));

        result.Should().HaveCount(2);
        result.Should().Contain(r => r.OwnerType == ContactOwnerTypeDto.Guardian && r.Role == GuardianRoleDto.Primary);
        result.Should().Contain(r => r.OwnerType == ContactOwnerTypeDto.Student && r.Channel == ContactChannelDto.SMS);
    }

    [TestMethod]
    public async Task GetSubmission_ReturnsDetailWithVersionsAndReview()
    {
        var submissionId = Guid.NewGuid();
        var submissionRepo = new FakeSubmissionRepository
        {
            SubmissionDetailToReturn = new SubmissionDetailDto(
                submissionId, AssignmentId, StudentId, 2, ReviewStateDto.Reviewed, DateTimeOffset.UtcNow,
                new[]
                {
                    new SubmissionVersionDto(Guid.NewGuid(), 1, SubmissionSourceDto.Student, "v1", null, DateTimeOffset.UtcNow),
                    new SubmissionVersionDto(Guid.NewGuid(), 2, SubmissionSourceDto.GuardianOnBehalf, "v2", GuardianId, DateTimeOffset.UtcNow),
                },
                new SubmissionReviewDto(Guid.NewGuid(), submissionId, Guid.Empty, 9.5m, "A", "good", DateTimeOffset.UtcNow))
        };
        var handler = new GetSubmissionHandler(submissionRepo, NullLogger<GetSubmissionHandler>.Instance);

        var result = await handler.HandleAsync(new GetSubmission(AssignmentId, StudentId));

        result.Should().NotBeNull();
        result!.Versions.Should().HaveCount(2);
        result.CurrentVersionNumber.Should().Be(2);
        result.Review.Should().NotBeNull();
        result.Review!.Score.Should().Be(9.5m);
    }

    [TestMethod]
    public async Task ListSubmissionsByAssignment_ReturnsSubmissions()
    {
        var submissionRepo = new FakeSubmissionRepository
        {
            SubmissionsForAssignment = new[]
            {
                new SubmissionForReviewDto(Guid.NewGuid(), AssignmentId, "Math", StudentId, 1, ReviewStateDto.Pending, DateTimeOffset.UtcNow),
                new SubmissionForReviewDto(Guid.NewGuid(), AssignmentId, "Math", Guid.NewGuid(), 2, ReviewStateDto.Reviewed, DateTimeOffset.UtcNow),
            }
        };
        var handler = new ListSubmissionsByAssignmentHandler(submissionRepo, NullLogger<ListSubmissionsByAssignmentHandler>.Instance);

        var result = await handler.HandleAsync(new ListSubmissionsByAssignment(AssignmentId));

        result.Should().HaveCount(2);
        result.Should().OnlyContain(s => s.AssignmentId == AssignmentId);
    }

    // ── Fakes ────────────────────────────────────────────────────────────

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
        public int BroadcastCount;
        public AssignmentPublishedContext? Last;
        public Task BroadcastPublishedAsync(AssignmentPublishedContext context, CancellationToken ct = default)
        { BroadcastCount++; Last = context; return Task.CompletedTask; }
    }

    private sealed class FakePublisher : IIntegrationEventPublisher
    {
        public int Count;
        public Task EnqueueAsync<T>(T message, CancellationToken ct = default) where T : class { Count++; return Task.CompletedTask; }
    }

    private sealed class FakeActivityGroupLookup : IActivityGroupLookup
    {
        public Task<ActivityGroupRefDto[]> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default)
            => Task.FromResult(Array.Empty<ActivityGroupRefDto>());
        public Task<Guid[]> GetActiveMemberIdsAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default)
            => Task.FromResult(Array.Empty<Guid>());
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
    }

    private sealed class FakeSubmissionRepository : ISubmissionRepository
    {
        public List<AssignmentRecipient> AddedRecipients { get; } = new();
        public List<AssignmentRecipient> UpdatedRecipients { get; } = new();
        public List<GuardianSubmissionGate> AddedGates { get; } = new();
        public List<GuardianSubmissionGate> UpdatedGates { get; } = new();
        public List<AssignmentSubmission> AddedSubmissions { get; } = new();
        public List<AssignmentSubmission> UpdatedSubmissions { get; } = new();
        public List<AssignmentSubmissionVersion> AddedVersions { get; } = new();
        public List<SubmissionReview> AddedReviews { get; } = new();

        public AssignmentRecipient? RecipientToReturn;
        public GuardianSubmissionGate? GateToReturn;
        public AssignmentSubmission? SubmissionToReturn;

        public Task<AssignmentRecipient?> GetRecipientAsync(Guid a, Guid c, CancellationToken ct = default) => Task.FromResult(RecipientToReturn);
        public void Add(AssignmentRecipient r) => AddedRecipients.Add(r);
        public void Update(AssignmentRecipient r) => UpdatedRecipients.Add(r);
        public int DeletedRecipientsCount;
        public Task<int> DeleteRecipientsForAssignmentAsync(Guid assignmentId, CancellationToken ct = default)
        { DeletedRecipientsCount++; return Task.FromResult(1); }
        public Task<GuardianSubmissionGate?> GetGateAsync(Guid id, CancellationToken ct = default) => Task.FromResult(GateToReturn);
        public Task<GuardianSubmissionGate?> GetGateByAssignmentStudentAsync(Guid a, Guid s, CancellationToken ct = default) => Task.FromResult(GateToReturn);
        public void Add(GuardianSubmissionGate g) => AddedGates.Add(g);
        public void Update(GuardianSubmissionGate g) => UpdatedGates.Add(g);
        public List<GuardianSubmissionGate> GatesForAssignment { get; } = new();
        public Task<List<GuardianSubmissionGate>> ListGatesForAssignmentAsync(Guid assignmentId, CancellationToken ct = default)
            => Task.FromResult(GatesForAssignment);
        public Task<AssignmentSubmission?> GetSubmissionAsync(Guid id, CancellationToken ct = default) => Task.FromResult(SubmissionToReturn);
        public Task<AssignmentSubmission?> GetSubmissionByAssignmentStudentAsync(Guid a, Guid s, CancellationToken ct = default) => Task.FromResult(SubmissionToReturn);
        public void Add(AssignmentSubmission s) => AddedSubmissions.Add(s);
        public void Update(AssignmentSubmission s) => UpdatedSubmissions.Add(s);
        public void Add(AssignmentSubmissionVersion v) => AddedVersions.Add(v);
        public void Add(SubmissionReview r) => AddedReviews.Add(r);
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
        public Task<SubmissionForReviewDto[]> ListSubmissionsForReviewAsync(Guid t, CancellationToken ct = default)
            => Task.FromResult(Array.Empty<SubmissionForReviewDto>());
        public Task<SubmissionForReviewDto[]> ListSubmissionsByAssignmentAsync(Guid a, CancellationToken ct = default)
            => Task.FromResult(SubmissionsForAssignment);
        public Task<AssignmentRecipientDto[]> ListRecipientsForAssignmentAsync(Guid a, CancellationToken ct = default)
            => Task.FromResult(RecipientsForAssignment);
        public Task<SubmissionDetailDto?> GetSubmissionDetailAsync(Guid a, Guid s, CancellationToken ct = default)
            => Task.FromResult(SubmissionDetailToReturn);

        public SubmissionForReviewDto[] SubmissionsForAssignment { get; set; } = Array.Empty<SubmissionForReviewDto>();
        public AssignmentRecipientDto[] RecipientsForAssignment { get; set; } = Array.Empty<AssignmentRecipientDto>();
        public SubmissionDetailDto? SubmissionDetailToReturn;

        public Task<GuardianGateDto?> GetGuardianGateAsync(Guid a, Guid s, CancellationToken ct = default)
            => Task.FromResult((GuardianGateDto?)null);
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
