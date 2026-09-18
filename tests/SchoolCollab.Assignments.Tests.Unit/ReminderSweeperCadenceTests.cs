using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Worker.Services;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>E3 (ar-19) — the reminder sweep's pure cadence math: interval elapsed since the
/// later of publish / awaiting-signature, capped by MaxReminders non-terminal reminders,
/// and only after the current cycle has settled.</summary>
[TestClass]
public class ReminderSweeperCadenceTests
{
    private static readonly DateTimeOffset Now = new(2025, 1, 10, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void BeforeFirstInterval_NotDue()
    {
        var published = Now.AddHours(-12); // 12h ago, interval 24h
        ReminderSweeper.IsReminderDue(Now, published, null, null, 0, 24, 3)
            .Should().BeFalse("interval has not elapsed yet");
    }

    [TestMethod]
    public void AfterInterval_WithNoPriorReminder_IsDue()
    {
        var published = Now.AddHours(-48); // two days ago
        ReminderSweeper.IsReminderDue(Now, published, null, null, 0, 24, 3)
            .Should().BeTrue();
    }

    [TestMethod]
    public void AwaitingSignatureAnchorsCadence_LaterOfPublishOrSubmission()
    {
        var published = Now.AddDays(-10);
        var awaitingSince = Now.AddHours(-12); // submission is the later anchor
        ReminderSweeper.IsReminderDue(Now, published, awaitingSince, null, 0, 24, 3)
            .Should().BeFalse("the awaiting-signature anchor has not elapsed 24h");
    }

    [TestMethod]
    public void WithinCycle_AfterLastReminder_NotDue()
    {
        var published = Now.AddDays(-10);
        var lastReminder = Now.AddHours(-6); // reminded 6h ago, interval 24h
        ReminderSweeper.IsReminderDue(Now, published, null, lastReminder, 1, 24, 3)
            .Should().BeFalse("the current cycle has not settled");
    }

    [TestMethod]
    public void MaxRemindersCap_SuppressesFurtherReminders()
    {
        var published = Now.AddDays(-10);
        ReminderSweeper.IsReminderDue(Now, published, null, Now.AddDays(-1), 3, 24, 3)
            .Should().BeFalse("the MaxReminders cap is reached");
    }

    [TestMethod]
    public void NullPolicyValues_FallBackToBuiltInDefaults()
    {
        var published = Now.AddDays(-2);
        ReminderSweeper.IsReminderDue(Now, published, null, null, 0, null, null)
            .Should().BeTrue("null policy uses the built-in 24h interval / 3 cap");
    }
}
