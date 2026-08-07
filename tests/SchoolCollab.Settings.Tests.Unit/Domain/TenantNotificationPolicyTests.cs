using FluentAssertions;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.Settings.Tests.Unit.Domain;

[TestClass]
public class TenantNotificationPolicyTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [TestMethod]
    public void Create_sets_defaults()
    {
        var p = TenantNotificationPolicy.Create(TenantId);
        p.TenantId.Should().Be(TenantId);
        p.PreferredChannelOrder.Should().BeEmpty();
        p.BlockedChannels.Should().BeEmpty();
        p.MaxNotifications.Should().BeNull();
        p.MaxReminders.Should().BeNull();
        p.ReminderIntervalHours.Should().BeNull();
        p.LinkValidityDays.Should().BeNull();
        p.SendoutTimeOfDay.Should().BeNull();
        p.SendoutIntervalMinutes.Should().BeNull();
    }

    [TestMethod]
    public void Create_captures_values()
    {
        var p = TenantNotificationPolicy.Create(
            TenantId,
            [NotificationChannel.Email, NotificationChannel.SMS],
            [NotificationChannel.WhatsApp],
            maxNotifications: 25,
            reminderIntervalHours: 48,
            linkValidityDays: 7,
            sendoutTimeOfDay: new TimeOnly(9, 0),
            sendoutIntervalMinutes: 60);

        p.PreferredChannelOrder.Should().Equal(NotificationChannel.Email, NotificationChannel.SMS);
        p.BlockedChannels.Should().Equal(NotificationChannel.WhatsApp);
        p.MaxNotifications.Should().Be(25);
        p.ReminderIntervalHours.Should().Be(48);
        p.LinkValidityDays.Should().Be(7);
        p.SendoutTimeOfDay.Should().Be(new TimeOnly(9, 0));
        p.SendoutIntervalMinutes.Should().Be(60);
    }

    [TestMethod]
    public void SetPolicy_replaces_and_stamps_UpdatedAt()
    {
        var p = TenantNotificationPolicy.Create(TenantId, maxNotifications: 10);
        var before = p.UpdatedAt;

        p.SetPolicy([NotificationChannel.WhatsApp], [NotificationChannel.SMS],
            maxNotifications: 50, null, null, null, null, null);

        p.PreferredChannelOrder.Should().Equal(NotificationChannel.WhatsApp);
        p.BlockedChannels.Should().Equal(NotificationChannel.SMS);
        p.MaxNotifications.Should().Be(50);
        p.MaxReminders.Should().BeNull();
        p.UpdatedAt.Should().BeOnOrAfter(before);
    }

    [TestMethod]
    public void SetPolicy_null_arrays_clear_lists()
    {
        var p = TenantNotificationPolicy.Create(TenantId, [NotificationChannel.Email], [NotificationChannel.SMS]);
        p.SetPolicy(null, null, null, null, null, null, null, null);
        p.PreferredChannelOrder.Should().BeEmpty();
        p.BlockedChannels.Should().BeEmpty();
    }

    [TestMethod]
    public void Create_rejects_negative_maxNotifications()
    {
        var act = () => TenantNotificationPolicy.Create(TenantId, maxNotifications: -1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    public void Create_rejects_reminderInterval_below_one()
    {
        var act = () => TenantNotificationPolicy.Create(TenantId, reminderIntervalHours: 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    public void SetPolicy_rejects_negative_maxReminders()
    {
        var p = TenantNotificationPolicy.Create(TenantId);
        var act = () => p.SetPolicy(null, null, null, maxReminders: -5, null, null, null, null);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
