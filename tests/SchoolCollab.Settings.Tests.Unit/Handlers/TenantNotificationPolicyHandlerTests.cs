using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Settings.Core.CQRS.NotificationPolicies.Commands.UpsertTenantNotificationPolicy;
using SchoolCollab.Settings.Core.CQRS.NotificationPolicies.Queries.GetTenantNotificationPolicy;
using SchoolCollab.Settings.Core.Data;

namespace SchoolCollab.Settings.Tests.Unit.Handlers;

[TestClass]
public class TenantNotificationPolicyHandlerTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private SettingsDbContext _db = default!;
    private TenantProvider _tenants = default!;
    private GetTenantNotificationPolicyHandler _getHandler = default!;
    private UpsertTenantNotificationPolicyHandler _upsertHandler = default!;

    [TestInitialize]
    public void Setup()
    {
        _tenants = new TenantProvider();
        var options = new DbContextOptionsBuilder<SettingsDbContext>()
            .UseInMemoryDatabase($"NotificationPolicy_{Guid.NewGuid()}")
            .Options;
        _db = new SettingsDbContext(options, _tenants);
        _getHandler = new GetTenantNotificationPolicyHandler(_db);
        _upsertHandler = new UpsertTenantNotificationPolicyHandler(_db, _tenants);
    }

    [TestCleanup]
    public void Cleanup() => _db.Dispose();

    private void AsTenant(Guid tenantId) =>
        _tenants.SetTenant(new TenantContext(tenantId, tenantId.ToString(), TenantType.School));

    [TestMethod]
    public async Task Get_returns_null_when_unset()
    {
        AsTenant(TenantA);
        var result = await _getHandler.HandleAsync(GetTenantNotificationPolicy.Instance);
        result.Should().BeNull();
    }

    [TestMethod]
    public async Task Upsert_creates_row_and_get_returns_it()
    {
        AsTenant(TenantA);
        await _upsertHandler.HandleAsync(new UpsertTenantNotificationPolicy(
            [NotificationChannel.Email], [NotificationChannel.WhatsApp],
            MaxNotifications: 30, null, null, null, null, null));

        var result = await _getHandler.HandleAsync(GetTenantNotificationPolicy.Instance);
        result.Should().NotBeNull();
        result!.PreferredChannelOrder.Should().Equal(NotificationChannel.Email);
        result.BlockedChannels.Should().Equal(NotificationChannel.WhatsApp);
        result.MaxNotifications.Should().Be(30);
    }

    [TestMethod]
    public async Task Upsert_updates_existing_row()
    {
        AsTenant(TenantA);
        await _upsertHandler.HandleAsync(new UpsertTenantNotificationPolicy(
            [NotificationChannel.Email], null, MaxNotifications: 10, null, null, null, null, null));
        await _upsertHandler.HandleAsync(new UpsertTenantNotificationPolicy(
            [NotificationChannel.SMS], null, MaxNotifications: 20, null, null, null, null, null));

        var result = await _getHandler.HandleAsync(GetTenantNotificationPolicy.Instance);
        result!.MaxNotifications.Should().Be(20);
        result.PreferredChannelOrder.Should().Equal(NotificationChannel.SMS);
        _db.TenantNotificationPolicies.Count().Should().Be(1);
    }

    [TestMethod]
    public async Task Tenant_isolation_get_returns_null_for_other_tenant()
    {
        AsTenant(TenantA);
        await _upsertHandler.HandleAsync(new UpsertTenantNotificationPolicy(
            [NotificationChannel.Email], null, null, null, null, null, null, null));

        AsTenant(TenantB);
        var result = await _getHandler.HandleAsync(GetTenantNotificationPolicy.Instance);
        result.Should().BeNull();
    }

    [TestMethod]
    public async Task Upsert_propagates_validation_error()
    {
        AsTenant(TenantA);
        var act = () => _upsertHandler.HandleAsync(new UpsertTenantNotificationPolicy(
            null, null, MaxNotifications: -1, null, null, null, null, null));
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }
}
