using FluentAssertions;
using SchoolCollab.Core.Notifications;

namespace SchoolCollab.Students.Tests.Unit.Notifications;

[TestClass]
public class EffectiveNotificationPolicyResolverTests
{
    private readonly EffectiveNotificationPolicyResolver _resolver = new();

    [TestMethod]
    public void NoTenantDefault_NoOverride_returns_empty_policy()
    {
        var result = _resolver.Resolve(null, null);

        result.PreferredChannelOrder.Should().BeEmpty();
        result.BlockedChannels.Should().BeEmpty();
        result.MaxNotifications.Should().BeNull();
        result.MaxNotificationsFromOverride.Should().BeFalse();
        result.PreferredChannelOrderFromOverride.Should().BeFalse();
    }

    [TestMethod]
    public void GradeOverride_wins_over_tenant_default_and_flags_source()
    {
        var tenant = new NotificationPolicyFields { MaxNotifications = 50, PreferredChannelOrder = [NotificationChannel.Email] };
        var grade = new NotificationPolicyFields { MaxNotifications = 10, BlockedChannels = [NotificationChannel.SMS] };

        var result = _resolver.Resolve(tenant, grade);

        result.MaxNotifications.Should().Be(10);
        result.MaxNotificationsFromOverride.Should().BeTrue();
        // Inherited from tenant (grade did not override).
        result.PreferredChannelOrder.Should().Equal(NotificationChannel.Email);
        result.PreferredChannelOrderFromOverride.Should().BeFalse();
        // Grade-only value.
        result.BlockedChannels.Should().Equal(NotificationChannel.SMS);
        result.BlockedChannelsFromOverride.Should().BeTrue();
    }

    [TestMethod]
    public void GradeNullField_inherits_tenant_value()
    {
        var tenant = new NotificationPolicyFields { MaxNotifications = 25, LinkValidityDays = 7 };
        var grade = new NotificationPolicyFields { MaxNotifications = null, LinkValidityDays = 14 };

        var result = _resolver.Resolve(tenant, grade);

        result.MaxNotifications.Should().Be(25); // inherited
        result.MaxNotificationsFromOverride.Should().BeFalse();
        result.LinkValidityDays.Should().Be(14); // override
        result.LinkValidityDaysFromOverride.Should().BeTrue();
    }

    [TestMethod]
    public void GradeExplicitEmptyChannelList_overrides_nonEmptyTenantList()
    {
        var tenant = new NotificationPolicyFields { PreferredChannelOrder = [NotificationChannel.Email, NotificationChannel.SMS] };
        var grade = new NotificationPolicyFields { PreferredChannelOrder = [] };

        var result = _resolver.Resolve(tenant, grade);

        result.PreferredChannelOrder.Should().BeEmpty();
        result.PreferredChannelOrderFromOverride.Should().BeTrue();
    }

    [TestMethod]
    public void TenantOnly_usesTenantValues_noOverrideFlags()
    {
        var tenant = new NotificationPolicyFields { MaxNotifications = 100, SendoutTimeOfDay = new TimeOnly(8, 30) };

        var result = _resolver.Resolve(tenant, null);

        result.MaxNotifications.Should().Be(100);
        result.MaxNotificationsFromOverride.Should().BeFalse();
        result.SendoutTimeOfDay.Should().Be(new TimeOnly(8, 30));
        result.SendoutTimeOfDayFromOverride.Should().BeFalse();
    }
}
