using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Assignments.Tests.Unit;

[TestClass]
public class NotificationRecipientFilterTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid AssignmentId = Guid.NewGuid();

    private static AssignmentRecipient Recipient(ContactChannel channel, int n) =>
        AssignmentRecipient.Create(
            TenantId, AssignmentId, ContactOwnerType.Guardian, Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), channel, GuardianRole.Primary, notifyOnBroadcast: true, subscriptionActive: true);

    private static EffectiveNotificationPolicy Policy(
        NotificationChannel[]? preferred = null,
        NotificationChannel[]? blocked = null,
        int? maxNotifications = null)
    {
        preferred ??= [];
        blocked ??= [];
        return new EffectiveNotificationPolicy(
            preferred, blocked, maxNotifications, null, null, null, null, null,
            PreferredChannelOrderFromOverride: false, BlockedChannelsFromOverride: false, MaxNotificationsFromOverride: false,
            MaxRemindersFromOverride: false, ReminderIntervalHoursFromOverride: false, LinkValidityDaysFromOverride: false,
            SendoutTimeOfDayFromOverride: false, SendoutIntervalMinutesFromOverride: false);
    }

    [TestMethod]
    public void Empty_recipients_returns_empty()
    {
        NotificationRecipientFilter.Apply([], Policy()).Should().BeEmpty();
    }

    [TestMethod]
    public void No_policy_keeps_all_in_enum_order()
    {
        var input = new[] { Recipient(ContactChannel.SMS, 1), Recipient(ContactChannel.Email, 2), Recipient(ContactChannel.WhatsApp, 3) };

        var result = NotificationRecipientFilter.Apply(input, Policy());

        result.Select(r => r.Channel).Should().Equal(ContactChannel.Email, ContactChannel.SMS, ContactChannel.WhatsApp);
    }

    [TestMethod]
    public void Filters_blocked_channel_recipients()
    {
        var input = new[] { Recipient(ContactChannel.Email, 1), Recipient(ContactChannel.SMS, 2), Recipient(ContactChannel.WhatsApp, 3) };
        var policy = Policy(blocked: [NotificationChannel.SMS]);

        var result = NotificationRecipientFilter.Apply(input, policy);

        result.Select(r => r.Channel).Should().Equal(ContactChannel.Email, ContactChannel.WhatsApp);
    }

    [TestMethod]
    public void Applies_preferred_channel_order()
    {
        var input = new[] { Recipient(ContactChannel.Email, 1), Recipient(ContactChannel.SMS, 2), Recipient(ContactChannel.WhatsApp, 3) };
        var policy = Policy(preferred: [NotificationChannel.SMS, NotificationChannel.Email]);

        var result = NotificationRecipientFilter.Apply(input, policy);

        result.Select(r => r.Channel).Should().Equal(ContactChannel.SMS, ContactChannel.Email, ContactChannel.WhatsApp);
    }

    [TestMethod]
    public void Channels_not_in_preferred_order_go_last_then_by_enum()
    {
        var input = new[] { Recipient(ContactChannel.WhatsApp, 1), Recipient(ContactChannel.Email, 2), Recipient(ContactChannel.SMS, 3) };
        var policy = Policy(preferred: [NotificationChannel.Email]);

        var result = NotificationRecipientFilter.Apply(input, policy);

        result.Select(r => r.Channel).Should().Equal(ContactChannel.Email, ContactChannel.SMS, ContactChannel.WhatsApp);
    }

    [TestMethod]
    public void Caps_sendout_at_max_notifications()
    {
        var input = new[] { Recipient(ContactChannel.Email, 1), Recipient(ContactChannel.SMS, 2), Recipient(ContactChannel.WhatsApp, 3) };
        var policy = Policy(maxNotifications: 2);

        var result = NotificationRecipientFilter.Apply(input, policy);

        result.Select(r => r.Channel).Should().Equal(ContactChannel.Email, ContactChannel.SMS);
    }

    [TestMethod]
    public void Max_notifications_zero_broadcasts_nobody()
    {
        var input = new[] { Recipient(ContactChannel.Email, 1) };
        var policy = Policy(maxNotifications: 0);

        NotificationRecipientFilter.Apply(input, policy).Should().BeEmpty();
    }
}
