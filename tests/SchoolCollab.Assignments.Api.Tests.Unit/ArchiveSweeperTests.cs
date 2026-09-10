using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Api.Services;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.ArchiveAssignmentCommand;
using SchoolCollab.Assignments.Core.DTOs;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Api.Tests.Unit;

/// <summary>
/// WS-A2 / spec §7 Q6 — pure <see cref="ArchiveSweeper.ArchiveDueAsync"/>
/// core. Mirrors <see cref="ScheduledPublishSweeperTests"/> structure:
/// happy path + per-candidate error isolation + empty list no-op.
/// </summary>
[TestClass]
public class ArchiveSweeperTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [TestMethod]
    public async Task ArchiveDueAsync_TwoCandidates_DispatchesBoth()
    {
        var recorder = new RecordingHandler();
        var tenants = new RecordingTenantAccessor();
        var candidates = new[]
        {
            new AssignmentSweepCandidate(Guid.NewGuid(), TenantA),
            new AssignmentSweepCandidate(Guid.NewGuid(), TenantA),
        };

        var processed = await ArchiveSweeper.ArchiveDueAsync(
            candidates, recorder, tenants, NullLogger.Instance);

        processed.Should().Be(2);
        recorder.Commands.Should().HaveCount(2);
        recorder.Commands[0].AssignmentId.Should().Be(candidates[0].Id);
        recorder.Commands[1].AssignmentId.Should().Be(candidates[1].Id);
        tenants.CapturedTenantIds.Should().Equal(candidates.Select(c => (Guid?)c.TenantId).ToArray());
    }

    [TestMethod]
    public async Task ArchiveDueAsync_FirstCandidateThrows_SecondStillProcessed()
    {
        var recorder = new RecordingHandler { ThrowOnFirstCall = true };
        var tenants = new RecordingTenantAccessor();
        var candidates = new[]
        {
            new AssignmentSweepCandidate(Guid.NewGuid(), TenantA),
            new AssignmentSweepCandidate(Guid.NewGuid(), TenantA),
        };

        var processed = await ArchiveSweeper.ArchiveDueAsync(
            candidates, recorder, tenants, NullLogger.Instance);

        processed.Should().Be(1);
        recorder.Commands.Should().HaveCount(2);
    }

    [TestMethod]
    public async Task ArchiveDueAsync_EmptyList_HandlerNeverCalled()
    {
        var recorder = new RecordingHandler();
        var tenants = new RecordingTenantAccessor();

        var processed = await ArchiveSweeper.ArchiveDueAsync(
            Array.Empty<AssignmentSweepCandidate>(), recorder, tenants, NullLogger.Instance);

        processed.Should().Be(0);
        recorder.Commands.Should().BeEmpty();
    }

    // ── Fakes ───────────────────────────────────────────────────────────

    private sealed class RecordingHandler : ICommandHandler<ArchiveAssignmentCommand>
    {
        public List<ArchiveAssignmentCommand> Commands { get; } = new();
        public bool ThrowOnFirstCall { get; set; }

        public Task HandleAsync(ArchiveAssignmentCommand command, CancellationToken cancellationToken = default)
        {
            if (ThrowOnFirstCall && Commands.Count == 0)
            {
                Commands.Add(command);
                throw new InvalidOperationException("test-synthetic failure");
            }
            Commands.Add(command);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingTenantAccessor : ITenantContextAccessor
    {
        public List<Guid?> CapturedTenantIds { get; } = new();

        public Task<T> RunWithExplicitTenantAsync<T>(
            Guid? tenantId,
            Func<CancellationToken, Task<T>> callback,
            CancellationToken ct = default)
        {
            CapturedTenantIds.Add(tenantId);
            return callback(ct);
        }

        public IDisposable SuppressTenantGuard() => new NoopDisposable();
        private sealed class NoopDisposable : IDisposable { public void Dispose() { } }
    }
}
