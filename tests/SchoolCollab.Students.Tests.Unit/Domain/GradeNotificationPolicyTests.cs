using FluentAssertions;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Tests.Unit.Domain;

[TestClass]
public class GradeNotificationPolicyTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid GradeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [TestMethod]
    public void Create_stores_grade_and_nullable_fields()
    {
        var p = GradeNotificationPolicy.Create(TenantId, GradeId);
        p.TenantId.Should().Be(TenantId);
        p.GradeLevelId.Should().Be(GradeId);
        p.PreferredChannelOrder.Should().BeNull();
        p.BlockedChannels.Should().BeNull();
        p.MaxNotifications.Should().BeNull();
    }

    [TestMethod]
    public void Create_captures_explicit_overrides()
    {
        var p = GradeNotificationPolicy.Create(
            TenantId, GradeId,
            [NotificationChannel.SMS], [NotificationChannel.Email],
            maxNotifications: 5);

        p.PreferredChannelOrder.Should().Equal(NotificationChannel.SMS);
        p.BlockedChannels.Should().Equal(NotificationChannel.Email);
        p.MaxNotifications.Should().Be(5);
    }

    [TestMethod]
    public void SetOverride_replaces_fields_and_nulls_clear_to_inherit()
    {
        var p = GradeNotificationPolicy.Create(TenantId, GradeId, [NotificationChannel.SMS], null, maxNotifications: 5);
        var before = p.UpdatedAt;

        p.SetOverride([NotificationChannel.WhatsApp], [NotificationChannel.SMS], maxNotifications: null, null, null, null, null, null);

        p.PreferredChannelOrder.Should().Equal(NotificationChannel.WhatsApp);
        p.BlockedChannels.Should().Equal(NotificationChannel.SMS);
        p.MaxNotifications.Should().BeNull(); // cleared → inherit
        p.UpdatedAt.Should().BeOnOrAfter(before);
    }

    [TestMethod]
    public void Create_rejects_negative_maxNotifications()
    {
        var act = () => GradeNotificationPolicy.Create(TenantId, GradeId, maxNotifications: -3);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    public void SetOverride_rejects_reminderInterval_below_one()
    {
        var p = GradeNotificationPolicy.Create(TenantId, GradeId);
        var act = () => p.SetOverride(null, null, null, null, reminderIntervalHours: 0, null, null, null);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
