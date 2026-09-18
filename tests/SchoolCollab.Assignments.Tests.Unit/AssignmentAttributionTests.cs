using FluentAssertions;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.CreateAssignmentCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.ReviewAssignmentCommand;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.DTOs;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.EntityCodes;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// ar-20 discriminating tests for the P1-6 attribution rule (auth-mode-keyed principal-wins):
/// a principal's <c>teacher_id</c> claim overrides the wire identity field, a missing claim
/// is REJECTED in real-auth mode, and the wire field is honored ONLY under the TestAuth/dev
/// branch (regression guard). Request-field values are non-empty Guids so "ignored" is
/// distinguishable from "fallback".
/// </summary>
[TestClass]
public class AssignmentAttributionTests
{
    private static readonly Guid PrincipalTeacher = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid WireTeacher = Guid.Parse("00000000-0000-0000-0000-0000000000AA");
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000010");
    private static readonly Guid SubjectId = Guid.Parse("00000000-0000-0000-0000-000000000020");

    private static Assignment NewAssignment() =>
        Assignment.Create("Attribution", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, null, null, null, Guid.Empty)
            .WithTenant(TenantId);

    private static FakeAssignmentRepositoryForCapture NewCaptureRepo() => new();

    // ── CreateAssignment: principal claim ⇒ not Guid.Empty ──────────────

    [TestMethod]
    public async Task CreateAssignment_WithTeacherClaim_PersistsPrincipalTeacher()
    {
        var repo = NewCaptureRepo();
        var currentUser = new FakeCurrentUser { TeacherId = PrincipalTeacher };
        var handler = new CreateAssignmentCommandHandler(
            repo,
            StubEntityCodeGenerator(),
            StubPublisher(),
            new FakeHybridCache(),
            new StubTenantProvider(TenantId),
            Options.Create(new AttachmentUploadOptions()),
            currentUser,
            new FakeTeacherDirectory { ExistsResult = true },
            new FakeFeatureFlagService { IsEnabledValue = true }, // dev; claim governs
            NullLogger<CreateAssignmentCommandHandler>.Instance);

        await handler.HandleAsync(new CreateAssignmentCommand(
            "Attribution HW", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, null, null, null));

        var created = repo.Added.Should().ContainSingle().Subject;
        created.CreatedByTeacherId.Should().Be(PrincipalTeacher);
        created.CreatedByTeacherId.Should().NotBe(Guid.Empty);
    }

    [TestMethod]
    public async Task CreateAssignment_RealAuth_MissingClaim_ThrowsTyped()
    {
        // Regression: a real-auth identity with no teacher_id must NOT fall back to the wire.
        var repo = NewCaptureRepo();
        var handler = new CreateAssignmentCommandHandler(
            repo,
            StubEntityCodeGenerator(),
            StubPublisher(),
            new FakeHybridCache(),
            new StubTenantProvider(TenantId),
            Options.Create(new AttachmentUploadOptions()),
            new FakeCurrentUser(),                       // TeacherId = null
            new FakeTeacherDirectory(),
            new FakeFeatureFlagService { IsEnabledValue = false }, // real-auth (DisableOIDCAuth=false)
            NullLogger<CreateAssignmentCommandHandler>.Instance);

        await FluentActions.Awaiting(() =>
            handler.HandleAsync(new CreateAssignmentCommand(
                "Attribution HW", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
                TargetAudienceType.AllStudents, TopicId, null, null, null, MandatoryReview: true)))
            .Should().ThrowAsync<MissingTeacherPrincipalException>();
    }

    // ── ReviewAssignment: request TeacherId ignored when claim present ──

    [TestMethod]
    public async Task Review_RequestField_Ignored_WhenClaimPresent()
    {
        var assignment = NewAssignment();
        var repo = new FakeReturningRepo { Assignment = assignment };
        var handler = new ReviewAssignmentCommandHandler(
            repo, new FakeHybridCache(),
            new FakeCurrentUser { TeacherId = PrincipalTeacher },
            new FakeFeatureFlagService { IsEnabledValue = true },
            NullLogger<ReviewAssignmentCommandHandler>.Instance);

        await handler.HandleAsync(new ReviewAssignmentCommand(
            assignment.Id, WireTeacher, 90m, "ok")); // WireTeacher ≠ PrincipalTeacher

        var review = assignment.Reviews.Should().ContainSingle().Subject;
        review.TeacherId.Should().Be(PrincipalTeacher);     // principal wins
        review.TeacherId.Should().NotBe(WireTeacher);      // request field ignored
    }

    // ── TestAuth/dev fallback (regression guard) ─────────────────────────

    [TestMethod]
    public async Task Review_DevFallback_NoClaim_HonorsRequestField()
    {
        var assignment = NewAssignment();
        var repo = new FakeReturningRepo { Assignment = assignment };
        var handler = new ReviewAssignmentCommandHandler(
            repo, new FakeHybridCache(),
            new FakeCurrentUser(),                          // no claim
            new FakeFeatureFlagService { IsEnabledValue = true }, // TestAuth/dev
            NullLogger<ReviewAssignmentCommandHandler>.Instance);

        await handler.HandleAsync(new ReviewAssignmentCommand(
            assignment.Id, WireTeacher, 90m, "ok"));

        var review = assignment.Reviews.Should().ContainSingle().Subject;
        review.TeacherId.Should().Be(WireTeacher);          // dev honors the wire field
    }

    // ── Real-auth + missing claim ⇒ typed rejection ─────────────────────

    [TestMethod]
    public async Task Review_RealAuth_MissingClaim_ThrowsTyped()
    {
        var assignment = NewAssignment();
        var repo = new FakeReturningRepo { Assignment = assignment };
        var handler = new ReviewAssignmentCommandHandler(
            repo, new FakeHybridCache(),
            new FakeCurrentUser(),                          // no claim
            new FakeFeatureFlagService { IsEnabledValue = false }, // real-auth
            NullLogger<ReviewAssignmentCommandHandler>.Instance);

        await FluentActions.Awaiting(() =>
            handler.HandleAsync(new ReviewAssignmentCommand(assignment.Id, WireTeacher, 90m, "ok")))
            .Should().ThrowAsync<MissingTeacherPrincipalException>();
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static IEntityCodeGenerator StubEntityCodeGenerator()
    {
        var g = new Mock<IEntityCodeGenerator>();
        g.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
         .ReturnsAsync("ASGA99");
        return g.Object;
    }

    private static IIntegrationEventPublisher StubPublisher() => new Mock<IIntegrationEventPublisher>().Object;

    private sealed class FakeAssignmentRepositoryForCapture : IAssignmentRepository
    {
        public List<Assignment> Added { get; } = [];
        public Task<Assignment?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Assignment?>(null);
        public Task AddAsync(Assignment a, CancellationToken ct = default) { Added.Add(a); return Task.CompletedTask; }
        public Task UpdateAsync(Assignment a, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Assignment a, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<AssignmentSummary>> ListAsync(AssignmentStatus? s, CancellationToken ct = default) => Task.FromResult(new List<AssignmentSummary>());
        public void DetectChanges() { }
        public Task<List<AssignmentSweepCandidate>> ListScheduledForAutoPublishAsync(DateTimeOffset nowUtc, CancellationToken ct = default) => Task.FromResult(new List<AssignmentSweepCandidate>());
        public Task<List<AssignmentSweepCandidate>> ListDueForArchiveAsync(DateTimeOffset nowUtc, CancellationToken ct = default) => Task.FromResult(new List<AssignmentSweepCandidate>());
    }

    private sealed class FakeReturningRepo : IAssignmentRepository
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

    private sealed class StubTenantProvider(Guid tenantId) : ITenantProvider
    {
        public TenantContext GetTenantContext() => new(tenantId, "Dev School", TenantType.School);
    }

    private sealed class FakeHybridCache : HybridCache
    {
        public override ValueTask<T> GetOrCreateAsync<TState, T>(string key, TState state, Func<TState, CancellationToken, ValueTask<T>> factory, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default) => factory(state, cancellationToken);
        public override ValueTask SetAsync<T>(string key, T value, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
