using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Api.Services;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.PublishAssignmentCommand;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.DTOs;
using SchoolCollab.Core.CQRS;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Api.Tests.Unit;

/// <summary>
/// WS-A2 / spec §3.5 step 8 — pure <see cref="ScheduledPublishSweeper.PublishDueAsync"/>
/// core. Three behaviours:
/// <list type="number">
///   <item>Two candidates → handler receives <see cref="PublishAssignmentCommand"/>(id, null) per candidate, count 2.</item>
///   <item>First candidate's handler throws → second still dispatched, count 1 (error isolation).</item>
///   <item>Empty list → 0, handler never called.</item>
/// </list>
/// No DbContext, no BackgroundService. Hand-rolled fakes for the
/// handler, logger, and tenant accessor — the repo convention.
/// </summary>
[TestClass]
public class ScheduledPublishSweeperTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [TestMethod]
    public async Task PublishDueAsync_TwoCandidates_DispatchesBoth()
    {
        var recorder = new RecordingHandler();
        var tenants = new RecordingTenantAccessor();
        var candidates = new[]
        {
            new AssignmentSweepCandidate(Guid.NewGuid(), TenantA),
            new AssignmentSweepCandidate(Guid.NewGuid(), TenantA),
        };

        var processed = await ScheduledPublishSweeper.PublishDueAsync(
            candidates, recorder, tenants, NullLogger.Instance);

        processed.Should().Be(2);
        recorder.Commands.Should().HaveCount(2);
        recorder.Commands[0].Id.Should().Be(candidates[0].Id);
        recorder.Commands[0].ContactIds.Should().BeNull("the sweep sends no recipient selection — the publish flow resolves its own");
        recorder.Commands[1].Id.Should().Be(candidates[1].Id);
        tenants.CapturedTenantIds.Should().Equal(candidates.Select(c => (Guid?)c.TenantId).ToArray());
    }

    [TestMethod]
    public async Task PublishDueAsync_FirstCandidateThrows_SecondStillProcessed()
    {
        var recorder = new RecordingHandler { ThrowOnFirstCall = true };
        var tenants = new RecordingTenantAccessor();
        var candidates = new[]
        {
            new AssignmentSweepCandidate(Guid.NewGuid(), TenantA),
            new AssignmentSweepCandidate(Guid.NewGuid(), TenantA),
        };

        var processed = await ScheduledPublishSweeper.PublishDueAsync(
            candidates, recorder, tenants, NullLogger.Instance);

        processed.Should().Be(1, "the second candidate must still be dispatched after the first throws");
        recorder.Commands.Should().HaveCount(2, "the dispatcher never short-circuits on the per-candidate exception");
    }

    [TestMethod]
    public async Task PublishDueAsync_EmptyList_HandlerNeverCalled()
    {
        var recorder = new RecordingHandler();
        var tenants = new RecordingTenantAccessor();

        var processed = await ScheduledPublishSweeper.PublishDueAsync(
            Array.Empty<AssignmentSweepCandidate>(), recorder, tenants, NullLogger.Instance);

        processed.Should().Be(0);
        recorder.Commands.Should().BeEmpty();
    }

    // ── Round assignment-approval-workflow (spec §3.5, Q12): the awaiting-approval skip ──

    [TestMethod]
    public async Task PublishDueAsync_AwaitingApproval_IsSkippedAtInfoLevel_NotErrorLogged()
    {
        var logger = new CapturingLogger();
        var handler = new AwaitingApprovalHandler();
        var tenants = new RecordingTenantAccessor();
        var candidates = new[] { new AssignmentSweepCandidate(Guid.NewGuid(), TenantA) };

        var processed = await ScheduledPublishSweeper.PublishDueAsync(
            candidates, handler, tenants, logger);

        processed.Should().Be(0, "the unapproved row was not published");
        logger.Entries.Should().NotContain(e => e.Level == LogLevel.Error,
            "an unapproved scheduled row is an expected posture — error-logging it every 15 minutes forever is the bug");
        logger.Entries.Should().Contain(e =>
                e.Level == LogLevel.Information && e.Message.Contains("awaiting approval"),
            "the skip is counted and reported once per pass");
    }

    [TestMethod]
    public async Task PublishDueAsync_AnyOtherFailure_StaysAnError()
    {
        var logger = new CapturingLogger();
        var tenants = new RecordingTenantAccessor();
        var candidates = new[] { new AssignmentSweepCandidate(Guid.NewGuid(), TenantA) };

        await ScheduledPublishSweeper.PublishDueAsync(
            candidates, new RecordingHandler { ThrowOnFirstCall = true }, tenants, logger);

        logger.Entries.Should().Contain(e => e.Level == LogLevel.Error,
            "every other exception keeps today's error handling");
    }

    // ── Fakes ───────────────────────────────────────────────────────────

    private sealed class AwaitingApprovalHandler : ICommandHandler<PublishAssignmentCommand>
    {
        public Task HandleAsync(PublishAssignmentCommand command, CancellationToken cancellationToken = default) =>
            Task.FromException(new AssignmentApprovalRequiredException(
                "This assignment requires approval before it can be published."));
    }

    /// <summary>A logger that keeps what was written, with levels — the observable for "info, not
    /// error" and for the counted summary line.</summary>
    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => new NoopScope();

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));

        private sealed class NoopScope : IDisposable
        {
            public void Dispose() { }
        }
    }

    private sealed class RecordingHandler : ICommandHandler<PublishAssignmentCommand>
    {
        public List<PublishAssignmentCommand> Commands { get; } = new();
        public bool ThrowOnFirstCall { get; set; }

        public Task HandleAsync(PublishAssignmentCommand command, CancellationToken cancellationToken = default)
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
