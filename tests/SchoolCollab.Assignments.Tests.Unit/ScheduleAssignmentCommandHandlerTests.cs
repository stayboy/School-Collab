using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.ScheduleAssignmentCommand;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.DTOs;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>WS-A2 / spec section 3.5 step 2 -
/// <see cref="ScheduleAssignmentCommandHandler"/> coverage. Mirrors the
/// scaffolding used by <c>AssignmentActivityGroupTests</c>: hand-rolled
/// fake repository + <see cref="FakeFeatureFlagService"/> + HybridCache
/// fake. Happypath: Draft goes to scheduled. Flag OFF schedules the
/// assignment; Flag ON + approved schedules; Flag ON + unapproved
/// throws <see cref="AssignmentApprovalRequiredException"/>. Unknown id
/// throws <see cref="AssignmentNotFoundException"/>. Past date throws
/// <see cref="ArgumentException"/> with no mutation.</summary>
[TestClass]
public class ScheduleAssignmentCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000010");

    private static Assignment NewAssignment() =>
        Assignment.Create("Math", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, null, null, null, TeacherId)
            .WithTenant(TenantId);

    [TestMethod]
    public async Task HandleAsync_Draft_FlagOff_Schedules()
    {
        var assignment = NewAssignment();
        var repo = new FakeAssignmentRepository { Assignment = assignment };
        var featureFlags = new FakeFeatureFlagService(); // OFF
        var cache = new FakeHybridCache();
        var handler = new ScheduleAssignmentCommandHandler(repo, featureFlags, cache,
            NullLogger<ScheduleAssignmentCommandHandler>.Instance);

        var availableFrom = DateTimeOffset.UtcNow.AddDays(7);
        await handler.HandleAsync(new ScheduleAssignmentCommand(assignment.Id, availableFrom));

        repo.LastUpdate.Should().BeSameAs(assignment, "the handler must persist the updated aggregate");
        assignment.Status.Should().Be(AssignmentStatus.Scheduled);
        assignment.AvailableFromUtc.Should().Be(availableFrom);
    }

    [TestMethod]
    public async Task HandleAsync_FlagOn_NotApproved_Throws()
    {
        var assignment = NewAssignment();
        var repo = new FakeAssignmentRepository { Assignment = assignment };
        var featureFlags = new FakeFeatureFlagService { IsEnabledValue = true };
        var cache = new FakeHybridCache();
        var handler = new ScheduleAssignmentCommandHandler(repo, featureFlags, cache,
            NullLogger<ScheduleAssignmentCommandHandler>.Instance);

        await FluentActions.Awaiting(() =>
                handler.HandleAsync(new ScheduleAssignmentCommand(assignment.Id, DateTimeOffset.UtcNow.AddDays(7))))
            .Should().ThrowAsync<AssignmentApprovalRequiredException>();

        assignment.Status.Should().Be(AssignmentStatus.Draft);
    }

    [TestMethod]
    public async Task HandleAsync_FlagOn_Approved_Schedules()
    {
        var assignment = NewAssignment();
        assignment.SubmitForApproval();
        assignment.Approve(Guid.NewGuid());
        var repo = new FakeAssignmentRepository { Assignment = assignment };
        var featureFlags = new FakeFeatureFlagService { IsEnabledValue = true };
        var cache = new FakeHybridCache();
        var handler = new ScheduleAssignmentCommandHandler(repo, featureFlags, cache,
            NullLogger<ScheduleAssignmentCommandHandler>.Instance);

        await handler.HandleAsync(new ScheduleAssignmentCommand(assignment.Id, DateTimeOffset.UtcNow.AddDays(7)));

        assignment.Status.Should().Be(AssignmentStatus.Scheduled);
    }

    [TestMethod]
    public async Task HandleAsync_UnknownId_Throws()
    {
        var repo = new FakeAssignmentRepository { Assignment = null };
        var handler = new ScheduleAssignmentCommandHandler(repo, new FakeFeatureFlagService(),
            new FakeHybridCache(), NullLogger<ScheduleAssignmentCommandHandler>.Instance);

        await FluentActions.Awaiting(() =>
                handler.HandleAsync(new ScheduleAssignmentCommand(Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(7))))
            .Should().ThrowAsync<AssignmentNotFoundException>();
    }

    [TestMethod]
    public async Task HandleAsync_PastDate_Throws_NoMutation()
    {
        var assignment = NewAssignment();
        var before = assignment.Status;
        var repo = new FakeAssignmentRepository { Assignment = assignment };
        var handler = new ScheduleAssignmentCommandHandler(repo, new FakeFeatureFlagService(),
            new FakeHybridCache(), NullLogger<ScheduleAssignmentCommandHandler>.Instance);

        await FluentActions.Awaiting(() =>
                handler.HandleAsync(new ScheduleAssignmentCommand(assignment.Id, DateTimeOffset.UtcNow.AddSeconds(-1))))
            .Should().ThrowAsync<ArgumentException>();

        assignment.Status.Should().Be(before, "past-date schedules must not mutate the aggregate");
        repo.UpdateCalls.Should().Be(0);
    }

    // Fakes (cross-handler scope; mirrors ActivityGroupTests fakes)

    private sealed class FakeAssignmentRepository : IAssignmentRepository
    {
        public Assignment? Assignment;
        public Assignment? LastUpdate { get; private set; }
        public int UpdateCalls { get; private set; }

        public Task<Assignment?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Assignment);
        public Task AddAsync(Assignment a, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(Assignment a, CancellationToken ct = default)
        {
            LastUpdate = a; UpdateCalls++;
            return Task.CompletedTask;
        }
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
