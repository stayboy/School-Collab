using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Core.Services;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// WS-E2 (ar-16) — binding case 4: the pure retry-backoff schedule (deterministic
/// sequence + the named attempt cap that makes a failure terminal).
/// </summary>
[TestClass]
public class NotificationRetryScheduleTests
{
    [TestMethod]
    public void Next_FollowsFixedBackoffSequence()
    {
        NotificationRetrySchedule.Next(1).Should().Be(TimeSpan.FromMinutes(1));
        NotificationRetrySchedule.Next(2).Should().Be(TimeSpan.FromMinutes(5));
        NotificationRetrySchedule.Next(3).Should().Be(TimeSpan.FromMinutes(15));
        NotificationRetrySchedule.Next(4).Should().Be(TimeSpan.FromMinutes(60));
    }

    [TestMethod]
    public void Next_AtCapAndBeyond_ReturnsNull()
    {
        NotificationRetrySchedule.MaxAttempts.Should().Be(5);

        NotificationRetrySchedule.Next(NotificationRetrySchedule.MaxAttempts).Should().BeNull();
        NotificationRetrySchedule.Next(NotificationRetrySchedule.MaxAttempts + 3).Should().BeNull();
        NotificationRetrySchedule.Next(0).Should().BeNull();
    }

    [TestMethod]
    public void NextRetryAt_AddsTheScheduledDelay()
    {
        var now = new DateTimeOffset(2026, 9, 17, 8, 0, 0, TimeSpan.Zero);

        NotificationRetrySchedule.NextRetryAt(1, now).Should().Be(now.AddMinutes(1));
        NotificationRetrySchedule.NextRetryAt(4, now).Should().Be(now.AddMinutes(60));
        NotificationRetrySchedule.NextRetryAt(NotificationRetrySchedule.MaxAttempts, now).Should().BeNull();
    }
}
