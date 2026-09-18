using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Assignments.Core.Services.Delivery;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// Discriminating test (c): the broadcaster's republish path is an <b>upsert in place</b> —
/// re-broadcasting an already-notified recipient must NOT insert a second
/// <see cref="NotificationKind.Publish"/> row (the ar-16/ar-17 F1 duplicate class is closed
/// at the source, not just at the partial unique index).
/// </summary>
[TestClass]
public class BroadcasterRepublishTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid AssignmentId = Guid.Parse("00000000-0000-0000-0000-0000000000dd");
    private static readonly Guid StudentId = Guid.Parse("00000000-0000-0000-0000-0000000000ee");
    private static readonly Guid ContactId = Guid.Parse("00000000-0000-0000-0000-0000000000ff");

    private sealed class StubAddressResolver : IContactAddressResolver
    {
        public Task<string?> ResolveAddressAsync(
            Guid contactId, ContactOwnerType ownerType, Guid ownerId, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>("guardian@example.com");
    }

    private sealed class RecordingPublisher : IIntegrationEventPublisher
    {
        public Task EnqueueAsync<T>(T message, CancellationToken cancellationToken = default) where T : class
            => Task.CompletedTask;

        public Task EnqueueAsync<T>(T message, Guid? tenantStamp, CancellationToken cancellationToken = default) where T : class
            => Task.CompletedTask;
    }

    private static (AssignmentsDbContext db, AssignmentNotificationBroadcaster broadcaster) Build(string database)
    {
        var services = new ServiceCollection();
        services.AddTenancy();
        services.AddDbContext<AssignmentsDbContext>(o => o.UseInMemoryDatabase(database));
        var provider = services.BuildServiceProvider();

        var db = provider.GetRequiredService<AssignmentsDbContext>();
        var tenants = (TenantProvider)provider.GetRequiredService<ITenantProvider>();
        tenants.SetTenant(new TenantContext(TenantId, "SchoolA", TenantType.School));
        db.Database.EnsureCreated();

        var broadcaster = new AssignmentNotificationBroadcaster(
            db, new StubAddressResolver(), new RecordingPublisher(), TimeProvider.System,
            NullLogger<AssignmentNotificationBroadcaster>.Instance);

        return (db, broadcaster);
    }

    [TestMethod]
    public async Task Republish_UpsertsInPlace_DoesNotInsertDuplicatePublishRow()
    {
        var (db, broadcaster) = Build(nameof(Republish_UpsertsInPlace_DoesNotInsertDuplicatePublishRow));

        // A subscribed, token-bearing, addressable recipient.
        var recipient = AssignmentRecipient.Create(
            TenantId, AssignmentId, ContactOwnerType.Guardian, Guid.NewGuid(), StudentId,
            ContactId, ContactChannel.Email, GuardianRole.Primary, notifyOnBroadcast: true, subscriptionActive: true);
        recipient.AttachDeepLink("tok-v1", DateTimeOffset.UtcNow.AddDays(7));

        // First broadcast → one Queued Publish row.
        await broadcaster.BroadcastPublishedAsync(
            new AssignmentPublishedContext(AssignmentId, "Math homework", DateTimeOffset.UtcNow, [recipient]));
        (await db.NotificationLogs.CountAsync()).Should().Be(1);

        // Simulate the Api drain delivering the row (attempt 1, terminal Sent).
        var sent = await db.NotificationLogs.SingleAsync();
        sent.MarkSent(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();

        // Republish with a refreshed deep-link token (the fresh F1-shaped re-broadcast).
        recipient.AttachDeepLink("tok-v2", DateTimeOffset.UtcNow.AddDays(7));
        await broadcaster.BroadcastPublishedAsync(
            new AssignmentPublishedContext(AssignmentId, "Math homework", DateTimeOffset.UtcNow, [recipient]));

        // (c): still exactly ONE Publish row — the republish was an upsert in place.
        var rows = await db.NotificationLogs.ToListAsync();
        rows.Should().HaveCount(1, "a republish must not insert a second Publish row");
        var updated = rows.Single();
        updated.Kind.Should().Be(NotificationKind.Publish);
        updated.Attempt.Should().Be(0, "the upsert resets the attempt cycle");
        updated.DeliveryStatus.Should().Be(NotificationDeliveryStatus.Queued,
            "the upsert re-queues the row with the fresh payload");
        updated.BodyHtml.Should().Contain("tok-v2",
            "the freshest deep-link token wins on republish");
        updated.BodyHtml.Should().NotContain("tok-v1");
    }
}
