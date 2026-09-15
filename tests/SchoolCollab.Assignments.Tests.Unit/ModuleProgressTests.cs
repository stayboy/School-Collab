using FluentAssertions;
using SchoolCollab.Assignments.Core.Domain;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// WS-D1 / decision (b) — <see cref="ModuleProgress"/> is the per-ward
/// content-module progress row. Recording is monotonic + idempotent
/// (replays / out-of-order heartbeat posts never regress) and
/// <see cref="ModuleProgress.CompletedAt"/> is stamped once the reported
/// percent first meets the module threshold, never un-stamped.
/// </summary>
[TestClass]
public class ModuleProgressTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AssignmentId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid StudentId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ModuleId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static ModuleProgress NewProgress(int percent, int threshold = 100)
        => ModuleProgress.Create(TenantId, AssignmentId, StudentId, ModuleId, percent, threshold);

    [TestMethod]
    public void Record_ClampsPercent_To0And100()
    {
        NewProgress(-25).PercentComplete.Should().Be(0);
        NewProgress(125).PercentComplete.Should().Be(100);

        var progress = NewProgress(50);
        progress.Record(200, 100);
        progress.PercentComplete.Should().Be(100);
        progress.Record(-5, 100);
        progress.PercentComplete.Should().Be(100, "record never regresses progress");
    }

    [TestMethod]
    public void Record_Monotonic_LowerReportIsNoOp()
    {
        var progress = NewProgress(50);
        var before = progress.UpdatedAt;
        progress.Record(30, 100);
        progress.Record(50, 100);
        progress.PercentComplete.Should().Be(50, "at-or-below reports are no-ops");
        progress.CompletedAt.Should().BeNull();
        progress.UpdatedAt.Should().Be(before, "a no-op report does not mutate or re-stamp UpdatedAt");
    }

    [TestMethod]
    public void CompletedAt_StampsOnce_AtThreshold()
    {
        var progress = NewProgress(70, threshold: 80);
        progress.CompletedAt.Should().BeNull();
        progress.Record(80, 80);
        progress.CompletedAt.Should().NotBeNull();

        var stamped = progress.CompletedAt;
        progress.Record(95, 80);
        progress.CompletedAt.Should().Be(stamped, "completion is never re-stamped");
        progress.PercentComplete.Should().Be(95);
    }

    [TestMethod]
    public void CompletedAt_NotStamped_BelowThreshold()
    {
        var progress = NewProgress(10, threshold: 80);
        progress.Record(79, 80);
        progress.CompletedAt.Should().BeNull();
        progress.Record(80, 80);
        progress.CompletedAt.Should().NotBeNull();
    }

    [TestMethod]
    public void Record_Idempotent_Replay()
    {
        var progress = NewProgress(100, threshold: 50);
        progress.CompletedAt.Should().NotBeNull();
        var stamped = progress.CompletedAt;
        var updated = progress.UpdatedAt;

        progress.Record(100, 50);
        progress.PercentComplete.Should().Be(100);
        progress.CompletedAt.Should().Be(stamped);
        progress.UpdatedAt.Should().Be(updated, "an equal report is a full no-op");
    }
}
