using FluentAssertions;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.ApproveAssignmentCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.RejectAssignmentCommand;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.SubmitAssignmentForApprovalCommand;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.DTOs;
using SchoolCollab.Core.Tenancy;


namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// WS-A2 / spec §7 Q2 — the three approval-flow command handlers:
/// <list type="bullet">
///   <item><see cref="SubmitAssignmentForApprovalCommandHandler"/> — Draft → Pending</item>
///   <item><see cref="ApproveAssignmentCommandHandler"/> — Pending → Approved (with approver id)</item>
///   <item><see cref="RejectAssignmentCommandHandler"/> — Pending → Rejected (clears stamps)</item>
/// </list>
/// Mirrors the fake-repo handler tests in
/// <c>ScheduleAssignmentCommandHandlerTests</c>.
/// </summary>
[TestClass]
public class AssignmentApprovalCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid ApproverId = Guid.Parse("00000000-0000-0000-0000-000000000099");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000010");

    private static Assignment NewAssignment() =>
        Assignment.Create("Math", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, null, null, null, TeacherId)
            .WithTenant(TenantId);

    // ── SubmitForApproval ───────────────────────────────────────────────

    [TestMethod]
    public async Task Submit_Draft_SetsPending()
    {
        var assignment = NewAssignment();
        var repo = new FakeAssignmentRepository { Assignment = assignment };
        var handler = new SubmitAssignmentForApprovalCommandHandler(repo, new FakeHybridCache(),
            NullLogger<SubmitAssignmentForApprovalCommandHandler>.Instance);

        await handler.HandleAsync(new SubmitAssignmentForApprovalCommand(assignment.Id));

        assignment.ApprovalStatus.Should().Be(ApprovalStatus.Pending);
        repo.LastUpdate.Should().BeSameAs(assignment);
    }

    [TestMethod]
    public async Task Submit_Scheduled_Throws()
    {
        var assignment = NewAssignment();
        assignment.Schedule(DateTimeOffset.UtcNow.AddDays(7));
        var repo = new FakeAssignmentRepository { Assignment = assignment };
        var handler = new SubmitAssignmentForApprovalCommandHandler(repo, new FakeHybridCache(),
            NullLogger<SubmitAssignmentForApprovalCommandHandler>.Instance);

        await FluentActions.Awaiting(() =>
                handler.HandleAsync(new SubmitAssignmentForApprovalCommand(assignment.Id)))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [TestMethod]
    public async Task Submit_UnknownId_Throws()
    {
        var repo = new FakeAssignmentRepository { Assignment = null };
        var handler = new SubmitAssignmentForApprovalCommandHandler(repo, new FakeHybridCache(),
            NullLogger<SubmitAssignmentForApprovalCommandHandler>.Instance);

        await FluentActions.Awaiting(() =>
                handler.HandleAsync(new SubmitAssignmentForApprovalCommand(Guid.NewGuid())))
            .Should().ThrowAsync<AssignmentNotFoundException>();
    }

    // ── Approve ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Approve_Pending_StampsApprover()
    {
        var assignment = NewAssignment();
        assignment.SubmitForApproval();
        var repo = new FakeAssignmentRepository { Assignment = assignment };
        var handler = new ApproveAssignmentCommandHandler(repo, new FakeHybridCache(),
            NullLogger<ApproveAssignmentCommandHandler>.Instance);

        await handler.HandleAsync(new ApproveAssignmentCommand(assignment.Id, ApproverId));

        assignment.ApprovalStatus.Should().Be(ApprovalStatus.Approved);
        assignment.ApprovedBy.Should().Be(ApproverId);
        assignment.ApprovedAt.Should().NotBeNull();
    }

    [TestMethod]
    public async Task Approve_Draft_Throws()
    {
        var assignment = NewAssignment();
        var repo = new FakeAssignmentRepository { Assignment = assignment };
        var handler = new ApproveAssignmentCommandHandler(repo, new FakeHybridCache(),
            NullLogger<ApproveAssignmentCommandHandler>.Instance);

        await FluentActions.Awaiting(() =>
                handler.HandleAsync(new ApproveAssignmentCommand(assignment.Id, ApproverId)))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [TestMethod]
    public async Task Approve_UnknownId_Throws()
    {
        var repo = new FakeAssignmentRepository { Assignment = null };
        var handler = new ApproveAssignmentCommandHandler(repo, new FakeHybridCache(),
            NullLogger<ApproveAssignmentCommandHandler>.Instance);

        await FluentActions.Awaiting(() =>
                handler.HandleAsync(new ApproveAssignmentCommand(Guid.NewGuid(), ApproverId)))
            .Should().ThrowAsync<AssignmentNotFoundException>();
    }

    // ── Reject ──────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Reject_Pending_ClearsStamps()
    {
        var assignment = NewAssignment();
        assignment.SubmitForApproval();
        assignment.Approve(ApproverId);
        assignment.SubmitForApproval(); // back to Pending
        var repo = new FakeAssignmentRepository { Assignment = assignment };
        var handler = new RejectAssignmentCommandHandler(repo, new FakeHybridCache(),
            NullLogger<RejectAssignmentCommandHandler>.Instance);

        await handler.HandleAsync(new RejectAssignmentCommand(assignment.Id, ApproverId));

        assignment.ApprovalStatus.Should().Be(ApprovalStatus.Rejected);
        assignment.ApprovedBy.Should().BeNull();
        assignment.ApprovedAt.Should().BeNull();
    }

    [TestMethod]
    public async Task Reject_Draft_Throws()
    {
        var assignment = NewAssignment();
        var repo = new FakeAssignmentRepository { Assignment = assignment };
        var handler = new RejectAssignmentCommandHandler(repo, new FakeHybridCache(),
            NullLogger<RejectAssignmentCommandHandler>.Instance);

        await FluentActions.Awaiting(() =>
                handler.HandleAsync(new RejectAssignmentCommand(assignment.Id, ApproverId)))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [TestMethod]
    public async Task Reject_UnknownId_Throws()
    {
        var repo = new FakeAssignmentRepository { Assignment = null };
        var handler = new RejectAssignmentCommandHandler(repo, new FakeHybridCache(),
            NullLogger<RejectAssignmentCommandHandler>.Instance);

        await FluentActions.Awaiting(() =>
                handler.HandleAsync(new RejectAssignmentCommand(Guid.NewGuid(), ApproverId)))
            .Should().ThrowAsync<AssignmentNotFoundException>();
    }

    // ── Fakes ───────────────────────────────────────────────────────────

    private sealed class FakeAssignmentRepository : IAssignmentRepository
    {
        public Assignment? Assignment;
        public Assignment? LastUpdate { get; private set; }

        public Task<Assignment?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Assignment);
        public Task AddAsync(Assignment a, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(Assignment a, CancellationToken ct = default)
        { LastUpdate = a; return Task.CompletedTask; }
        public Task DeleteAsync(Assignment a, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<AssignmentSummary>> ListAsync(AssignmentStatus? s, CancellationToken ct = default)
            => Task.FromResult(new List<AssignmentSummary>());
        public void DetectChanges() { }
        public Task<List<AssignmentSweepCandidate>> ListScheduledForAutoPublishAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
            => Task.FromResult(new List<AssignmentSweepCandidate>());
        public Task<List<AssignmentSweepCandidate>> ListDueForArchiveAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
            => Task.FromResult(new List<AssignmentSweepCandidate>());
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
