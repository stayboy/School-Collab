using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Assignments.Core.Services.Delivery;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// WS-E2 (ar-16) — binding case 6: publisher v1.1 queues exactly one
/// <see cref="NotificationLog"/> per <b>policy-filtered</b> recipient, each carrying
/// that recipient's deep link, skips recipients with no unexpired token, and never
/// creates rows for recipients the effective policy blocked or capped.
/// </summary>
[TestClass]
public class AssignmentNotificationBroadcasterTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid AssignmentId = Guid.Parse("00000000-0000-0000-0000-0000000000aa");
    private static readonly Guid StudentId = Guid.Parse("00000000-0000-0000-0000-0000000000dd");

    private sealed class StubAddressResolver(Dictionary<Guid, string> addresses) : IContactAddressResolver
    {
        public Task<string?> ResolveAddressAsync(
            Guid contactId, ContactOwnerType ownerType, Guid ownerId, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(addresses.TryGetValue(contactId, out var address) ? address : null);
    }

    /// <summary>Address resolver that reports cancellation — models a client disconnect or
    /// host shutdown while the publish is resolving addresses (review P2, ar-16).</summary>
    private sealed class CancellingAddressResolver : IContactAddressResolver
    {
        public Task<string?> ResolveAddressAsync(
            Guid contactId, ContactOwnerType ownerType, Guid ownerId, CancellationToken cancellationToken = default) =>
            Task.FromException<string?>(new OperationCanceledException(cancellationToken));
    }

    private sealed class RecordingPublisher : IIntegrationEventPublisher
    {
        public int Enqueued { get; private set; }

        public Task EnqueueAsync<T>(T message, CancellationToken cancellationToken = default) where T : class
        {
            Enqueued++;
            return Task.CompletedTask;
        }

        public Task EnqueueAsync<T>(T message, Guid? tenantStamp, CancellationToken cancellationToken = default) where T : class
        {
            Enqueued++;
            return Task.CompletedTask;
        }
    }

    private static (AssignmentsDbContext db, AssignmentNotificationBroadcaster broadcaster, RecordingPublisher publisher) Build(
        string database, Dictionary<Guid, string> addresses)
    {
        var services = new ServiceCollection();
        services.AddTenancy();
        services.AddDbContext<AssignmentsDbContext>(o => o.UseInMemoryDatabase(database));
        var provider = services.BuildServiceProvider();

        var db = provider.GetRequiredService<AssignmentsDbContext>();
        var tenants = (TenantProvider)provider.GetRequiredService<ITenantProvider>();
        tenants.SetTenant(new TenantContext(TenantId, "SchoolA", TenantType.School));
        db.Database.EnsureCreated();

        var publisher = new RecordingPublisher();
        var broadcaster = new AssignmentNotificationBroadcaster(
            db, new StubAddressResolver(addresses), publisher, TimeProvider.System,
            NullLogger<AssignmentNotificationBroadcaster>.Instance);

        return (db, broadcaster, publisher);
    }

    private static AssignmentRecipient Recipient(Guid contactId, ContactChannel channel, string? token, DateTimeOffset? expiresAt = null)
    {
        var recipient = AssignmentRecipient.Create(
            TenantId, AssignmentId, ContactOwnerType.Guardian, Guid.NewGuid(), StudentId,
            contactId, channel, GuardianRole.Primary, notifyOnBroadcast: true, subscriptionActive: true);

        if (token is not null)
        {
            recipient.AttachDeepLink(token, expiresAt ?? DateTimeOffset.UtcNow.AddDays(7));
        }

        return recipient;
    }

    private static EffectiveNotificationPolicy Policy(
        NotificationChannel[]? blocked = null, int? maxNotifications = null) =>
        new([], blocked ?? [], maxNotifications, null, null, null, null, null,
            PreferredChannelOrderFromOverride: false, BlockedChannelsFromOverride: false, MaxNotificationsFromOverride: false,
            MaxRemindersFromOverride: false, ReminderIntervalHoursFromOverride: false, LinkValidityDaysFromOverride: false,
            SendoutTimeOfDayFromOverride: false, SendoutIntervalMinutesFromOverride: false);

    [TestMethod]
    public async Task OneQueuedRowPerFilteredRecipient_WithThatRecipientsDeepLink()
    {
        var contactEmail = Guid.NewGuid();
        var contactSms = Guid.NewGuid();
        var contactBlocked = Guid.NewGuid();
        var (db, broadcaster, publisher) = Build(nameof(OneQueuedRowPerFilteredRecipient_WithThatRecipientsDeepLink),
            new Dictionary<Guid, string>
            {
                [contactEmail] = "guardian@example.com",
                [contactSms] = "+27123456789",
                [contactBlocked] = "+27999999999",
            });

        var recipients = new[]
        {
            Recipient(contactEmail, ContactChannel.Email, "tok-email"),
            Recipient(contactSms, ContactChannel.SMS, "tok-sms"),
            Recipient(contactBlocked, ContactChannel.WhatsApp, "tok-whatsapp"),
        };

        // The publish handler applies the effective policy before broadcasting; the
        // broadcaster must therefore only see (and log) the surviving set.
        var filtered = NotificationRecipientFilter.Apply(recipients, Policy(blocked: [NotificationChannel.WhatsApp]));

        await broadcaster.BroadcastPublishedAsync(
            new AssignmentPublishedContext(AssignmentId, "Math homework", DateTimeOffset.UtcNow, filtered));

        var rows = await db.NotificationLogs.ToListAsync();
        rows.Should().HaveCount(2);
        publisher.Enqueued.Should().Be(1, "the E3 outbox enqueue stays in place");

        var emailRow = rows.Single(r => r.ContactId == contactEmail);
        emailRow.DeliveryStatus.Should().Be(NotificationDeliveryStatus.Queued);
        emailRow.Kind.Should().Be(NotificationKind.Publish);
        emailRow.Attempt.Should().Be(0);
        emailRow.RecipientId.Should().NotBeEmpty();
        emailRow.ToAddress.Should().Be("guardian@example.com");
        emailRow.BodyHtml.Should().Contain("/deeplink/tok-email");

        var smsRow = rows.Single(r => r.ContactId == contactSms);
        smsRow.DeliveryStatus.Should().Be(NotificationDeliveryStatus.Queued);
        smsRow.ToAddress.Should().Be("+27123456789");
        smsRow.BodyHtml.Should().Contain("/deeplink/tok-sms");

        rows.Should().NotContain(r => r.ContactId == contactBlocked,
            "the policy-filtered recipient must not produce a log row at all");
    }

    [TestMethod]
    public async Task CappedRecipients_AreAbsentFromTheLog()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var (db, broadcaster, _) = Build(nameof(CappedRecipients_AreAbsentFromTheLog),
            new Dictionary<Guid, string> { [first] = "a@example.com", [second] = "b@example.com" });

        var recipients = new[]
        {
            Recipient(first, ContactChannel.Email, "tok-1"),
            Recipient(second, ContactChannel.Email, "tok-2"),
        };

        var filtered = NotificationRecipientFilter.Apply(recipients, Policy(maxNotifications: 1));

        await broadcaster.BroadcastPublishedAsync(
            new AssignmentPublishedContext(AssignmentId, "Math homework", DateTimeOffset.UtcNow, filtered));

        var rows = await db.NotificationLogs.ToListAsync();
        rows.Should().ContainSingle();
        rows[0].ContactId.Should().Be(first);
    }

    [TestMethod]
    public async Task RecipientWithoutToken_IsSkippedWithReason()
    {
        var contactNoToken = Guid.NewGuid();
        var (db, broadcaster, _) = Build(nameof(RecipientWithoutToken_IsSkippedWithReason),
            new Dictionary<Guid, string> { [contactNoToken] = "guardian@example.com" });

        var recipients = new[] { Recipient(contactNoToken, ContactChannel.Email, token: null) };

        await broadcaster.BroadcastPublishedAsync(
            new AssignmentPublishedContext(AssignmentId, "Math homework", DateTimeOffset.UtcNow, recipients));

        var row = (await db.NotificationLogs.ToListAsync()).Should().ContainSingle().Subject;
        row.DeliveryStatus.Should().Be(NotificationDeliveryStatus.Skipped);
        row.FailureReason.Should().Contain("deep-link token");
        row.NextRetryAt.Should().BeNull();
        row.Attempt.Should().Be(0);
    }

    [TestMethod]
    public async Task ExpiredToken_IsSkippedWithReason()
    {
        var contactExpired = Guid.NewGuid();
        var (db, broadcaster, _) = Build(nameof(ExpiredToken_IsSkippedWithReason),
            new Dictionary<Guid, string> { [contactExpired] = "guardian@example.com" });

        var recipients = new[]
        {
            Recipient(contactExpired, ContactChannel.Email, "tok-expired", DateTimeOffset.UtcNow.AddMinutes(-5)),
        };

        await broadcaster.BroadcastPublishedAsync(
            new AssignmentPublishedContext(AssignmentId, "Math homework", DateTimeOffset.UtcNow, recipients));

        var row = (await db.NotificationLogs.ToListAsync()).Should().ContainSingle().Subject;
        row.DeliveryStatus.Should().Be(NotificationDeliveryStatus.Skipped);
        row.FailureReason.Should().Contain("deep-link token");
        row.BodyHtml.Should().BeEmpty("a skipped row is never sent");
    }

    [TestMethod]
    public async Task UnresolvableAddress_IsSkippedNotQueued()
    {
        var contactUnknown = Guid.NewGuid();
        var (db, broadcaster, _) = Build(nameof(UnresolvableAddress_IsSkippedNotQueued), []);

        var recipients = new[] { Recipient(contactUnknown, ContactChannel.Email, "tok-unknown") };

        await broadcaster.BroadcastPublishedAsync(
            new AssignmentPublishedContext(AssignmentId, "Math homework", DateTimeOffset.UtcNow, recipients));

        var row = (await db.NotificationLogs.ToListAsync()).Should().ContainSingle().Subject;
        row.DeliveryStatus.Should().Be(NotificationDeliveryStatus.Skipped);
        row.FailureReason.Should().Contain("address");
    }

    [TestMethod]
    public async Task CancellationDuringAddressResolution_Propagates_AndPersistsNoRow()
    {
        var contact = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var (db, _, _) = Build(nameof(CancellationDuringAddressResolution_Propagates_AndPersistsNoRow),
            new Dictionary<Guid, string> { [contact] = "guardian@example.com" });

        var broadcaster = new AssignmentNotificationBroadcaster(
            db, new CancellingAddressResolver(), new RecordingPublisher(), TimeProvider.System,
            NullLogger<AssignmentNotificationBroadcaster>.Instance);

        var recipients = new[] { Recipient(contact, ContactChannel.Email, "tok-cancel") };

        // A genuine cancellation must NOT be converted into a terminal Skipped row — that would
        // silently lose the notification. It propagates, and nothing is persisted (review P2).
        await FluentActions
            .Awaiting(() => broadcaster.BroadcastPublishedAsync(
                new AssignmentPublishedContext(AssignmentId, "Math homework", DateTimeOffset.UtcNow, recipients),
                cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        (await db.NotificationLogs.ToListAsync()).Should().BeEmpty(
            "a cancelled publish must not persist a Skipped row");
    }
}
