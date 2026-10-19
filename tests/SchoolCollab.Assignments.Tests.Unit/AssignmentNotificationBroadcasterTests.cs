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
using SchoolCollab.Core.AssignmentPolicies;
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
        string database, Dictionary<Guid, string> addresses, INotificationTemplateResolver? templateResolver = null)
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
            db, new StubAddressResolver(addresses), publisher,
            templateResolver ?? new NullNotificationTemplateResolver(), tenants,
            Microsoft.Extensions.Options.Options.Create(new NotificationLinkOptions()),
            TimeProvider.System,
            NullLogger<AssignmentNotificationBroadcaster>.Instance);

        return (db, broadcaster, publisher);
    }

    /// <summary>Minimal in-process tenant context for broadcaster construction — the
    /// broadcaster only reads <see cref="TenantContext.TenantName"/> for SchoolName.</summary>
    private sealed class FixedTenantProvider : ITenantProvider
    {
        public TenantContext GetTenantContext() => new(TenantId, "SchoolA", TenantType.School);
    }

    private static AssignmentRecipient Recipient(
        Guid contactId,
        ContactChannel channel,
        string? token,
        DateTimeOffset? expiresAt = null,
        ContactOwnerType ownerType = ContactOwnerType.Guardian,
        GuardianRole? role = GuardianRole.Primary)
    {
        var recipient = AssignmentRecipient.Create(
            TenantId, AssignmentId, ownerType, Guid.NewGuid(), StudentId,
            contactId, channel, role, notifyOnBroadcast: true, subscriptionActive: true);

        if (token is not null)
        {
            recipient.AttachDeepLink(token, expiresAt ?? DateTimeOffset.UtcNow.AddDays(7), shortLinkCode: null);
        }

        return recipient;
    }

    /// <summary>A resolver that always yields one template — proves which tag VALUES the
    /// broadcaster assembles, where the fail-open default would only render its own text.</summary>
    private sealed class FixedTemplateResolver(string? subject, string body) : INotificationTemplateResolver
    {
        public Task<ResolvedNotificationTemplate?> ResolveAsync(
            NotificationKind kind, NotificationChannel channel, Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ResolvedNotificationTemplate?>(new ResolvedNotificationTemplate(Guid.NewGuid(), subject, body));

        public Task<NotificationTemplateSlot?> ResolveSlotAsync(
            NotificationKind kind, NotificationChannel channel, Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<NotificationTemplateSlot?>(
                new NotificationTemplateSlot(Guid.NewGuid(), kind, channel, subject, body, []));

        public async Task<NotificationTemplateSlotRead> ReadSlotAsync(
            NotificationKind kind, NotificationChannel channel, Guid tenantId, CancellationToken cancellationToken = default)
        {
            var slot = await ResolveSlotAsync(kind, channel, tenantId, cancellationToken);
            return new NotificationTemplateSlotRead(
                slot is null ? NotificationTemplateSlotState.NotSet : NotificationTemplateSlotState.Found, slot);
        }
    }

    private static EffectiveNotificationPolicy Policy(
        NotificationChannel[]? blocked = null, int? maxNotifications = null) =>
        new([], blocked ?? [], maxNotifications, null, null, null, null, null,
            PreferredChannelOrderFromOverride: false, BlockedChannelsFromOverride: false, MaxNotificationsFromOverride: false,
            MaxRemindersFromOverride: false, ReminderIntervalHoursFromOverride: false, LinkValidityDaysFromOverride: false,
            SendoutTimeOfDayFromOverride: false, SendoutIntervalMinutesFromOverride: false);

    /// <summary>
    /// Round B1: the widened <c>NotificationRecipientFilter.Apply</c> also takes the effective
    /// assignment policy whose two contact caps (D4) it now enforces. This fixture models a tenant
    /// with no assignment policy configured — the built-in default, i.e. both caps unset — so these
    /// broadcaster tests keep exercising exactly the pre-B1 filtering (blocked channels +
    /// <c>MaxNotifications</c>). The caps themselves are covered by
    /// <c>NotificationRecipientFilterTests</c>.
    /// </summary>
    private static EffectiveAssignmentPolicy UncappedAssignmentPolicy() =>
        new EffectiveAssignmentPolicyResolver().Resolve(tenantDefault: null, gradeOverride: null);

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
        var filtered = NotificationRecipientFilter.Apply(
            recipients, Policy(blocked: [NotificationChannel.WhatsApp]), UncappedAssignmentPolicy());

        await broadcaster.BroadcastPublishedAsync(
            new AssignmentPublishedContext(AssignmentId, "Math homework", DateTimeOffset.UtcNow, null, null, null, filtered));

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

        var filtered = NotificationRecipientFilter.Apply(
            recipients, Policy(maxNotifications: 1), UncappedAssignmentPolicy());

        await broadcaster.BroadcastPublishedAsync(
            new AssignmentPublishedContext(AssignmentId, "Math homework", DateTimeOffset.UtcNow, null, null, null, filtered));

        var rows = await db.NotificationLogs.ToListAsync();
        rows.Should().ContainSingle();
        rows[0].ContactId.Should().Be(first);
    }

    /// <summary>
    /// §3.2 tag values: <c>{{GuardianName}}</c>/<c>{{GuardianRole}}</c> come off the recipient
    /// row (enriched name + guardian link role), and a student-owned recipient — which has no
    /// guardian link — degrades the role to the empty string rather than failing the send.
    /// </summary>
    [TestMethod]
    public async Task GuardianNameAndRole_ComeFromTheRecipient_EmptyForStudentOwned()
    {
        var contactGuardian = Guid.NewGuid();
        var contactStudent = Guid.NewGuid();
        var (db, broadcaster, _) = Build(
            nameof(GuardianNameAndRole_ComeFromTheRecipient_EmptyForStudentOwned),
            new Dictionary<Guid, string>
            {
                [contactGuardian] = "guardian@example.com",
                [contactStudent] = "student@example.com",
            },
            new FixedTemplateResolver(null, "{{GuardianName}}|{{GuardianRole}}"));

        var guardianRecipient = Recipient(contactGuardian, ContactChannel.Email, "tok-guardian");
        guardianRecipient.AttachDisplayNames("Mrs Dlamini", "Thabo Dlamini");
        var studentRecipient = Recipient(
            contactStudent, ContactChannel.Email, "tok-student",
            ownerType: ContactOwnerType.Student, role: null);

        await broadcaster.BroadcastPublishedAsync(
            new AssignmentPublishedContext(
                AssignmentId, "Math homework", DateTimeOffset.UtcNow, null, null, null,
                NotificationRecipientFilter.Apply(
                    [guardianRecipient, studentRecipient], Policy(), UncappedAssignmentPolicy())));

        var rows = await db.NotificationLogs.ToListAsync();
        rows.Single(r => r.ContactId == contactGuardian).BodyHtml.Should().Be("Mrs Dlamini|Primary");
        rows.Single(r => r.ContactId == contactStudent).BodyHtml.Should().Be("|",
            "a student-owned recipient has no guardian link — the role degrades to empty, never fails");
    }

    [TestMethod]
    public async Task RecipientWithoutToken_IsSkippedWithReason()
    {
        var contactNoToken = Guid.NewGuid();
        var (db, broadcaster, _) = Build(nameof(RecipientWithoutToken_IsSkippedWithReason),
            new Dictionary<Guid, string> { [contactNoToken] = "guardian@example.com" });

        var recipients = new[] { Recipient(contactNoToken, ContactChannel.Email, token: null) };

        await broadcaster.BroadcastPublishedAsync(
            new AssignmentPublishedContext(AssignmentId, "Math homework", DateTimeOffset.UtcNow, null, null, null, recipients));

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
            new AssignmentPublishedContext(AssignmentId, "Math homework", DateTimeOffset.UtcNow, null, null, null, recipients));

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
            new AssignmentPublishedContext(AssignmentId, "Math homework", DateTimeOffset.UtcNow, null, null, null, recipients));

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
            db, new CancellingAddressResolver(), new RecordingPublisher(),
            new NullNotificationTemplateResolver(), new FixedTenantProvider(),
            Microsoft.Extensions.Options.Options.Create(new NotificationLinkOptions()),
            TimeProvider.System,
            NullLogger<AssignmentNotificationBroadcaster>.Instance);

        var recipients = new[] { Recipient(contact, ContactChannel.Email, "tok-cancel") };

        // A genuine cancellation must NOT be converted into a terminal Skipped row — that would
        // silently lose the notification. It propagates, and nothing is persisted (review P2).
        await FluentActions
            .Awaiting(() => broadcaster.BroadcastPublishedAsync(
                new AssignmentPublishedContext(AssignmentId, "Math homework", DateTimeOffset.UtcNow, null, null, null, recipients),
                cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        (await db.NotificationLogs.ToListAsync()).Should().BeEmpty(
            "a cancelled publish must not persist a Skipped row");
    }

    /// <summary>Returns one fixed tenant template — the enrichment tests need a body that
    /// actually consumes the name tags.</summary>
    private sealed class StubTemplateResolver(ResolvedNotificationTemplate template) : INotificationTemplateResolver
    {
        public Task<ResolvedNotificationTemplate?> ResolveAsync(
            NotificationKind kind, NotificationChannel channel, Guid tenantId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ResolvedNotificationTemplate?>(template);

        public Task<NotificationTemplateSlot?> ResolveSlotAsync(
            NotificationKind kind, NotificationChannel channel, Guid tenantId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<NotificationTemplateSlot?>(new NotificationTemplateSlot(
                template.TemplateId, kind, channel, template.Subject, template.Body, []));

        public async Task<NotificationTemplateSlotRead> ReadSlotAsync(
            NotificationKind kind, NotificationChannel channel, Guid tenantId,
            CancellationToken cancellationToken = default)
        {
            var slot = await ResolveSlotAsync(kind, channel, tenantId, cancellationToken);
            return new NotificationTemplateSlotRead(
                slot is null ? NotificationTemplateSlotState.NotSet : NotificationTemplateSlotState.Found, slot);
        }
    }

    private static (AssignmentsDbContext db, AssignmentNotificationBroadcaster broadcaster) BuildWithTemplate(
        string database, Dictionary<Guid, string> addresses, ResolvedNotificationTemplate template)
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
            db, new StubAddressResolver(addresses), new RecordingPublisher(),
            new StubTemplateResolver(template), tenants,
            Microsoft.Extensions.Options.Options.Create(new NotificationLinkOptions()),
            TimeProvider.System,
            NullLogger<AssignmentNotificationBroadcaster>.Instance);

        return (db, broadcaster);
    }

    /// <summary>
    /// Round B1 (§3.4): the enrichment names the publish handler attaches to the recipient reach
    /// the renderer as real tag values — the assertion the whole name-widening exists for.
    /// </summary>
    [TestMethod]
    public async Task EnrichmentNames_RenderIntoTheContactAndStudentTags()
    {
        var contact = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var (db, broadcaster) = BuildWithTemplate(
            nameof(EnrichmentNames_RenderIntoTheContactAndStudentTags),
            new Dictionary<Guid, string> { [contact] = "guardian@example.com" },
            new ResolvedNotificationTemplate(
                templateId, "Update for {{ContactName}}",
                "<p>{{StudentName}} has a new assignment.</p>"));

        var recipient = Recipient(contact, ContactChannel.Email, "tok-enriched");
        recipient.AttachDisplayNames("Mrs Mensah", "Ada Mensah");

        await broadcaster.BroadcastPublishedAsync(
            new AssignmentPublishedContext(AssignmentId, "Math homework", DateTimeOffset.UtcNow, null, null, null, [recipient]));

        var row = (await db.NotificationLogs.ToListAsync()).Should().ContainSingle().Subject;
        row.Subject.Should().Be("Update for Mrs Mensah");
        row.BodyHtml.Should().Contain("Ada Mensah");
        row.TemplateId.Should().Be(templateId, "the resolved template is the send-time record");
    }

    /// <summary>
    /// Round B1 (§3.4): a recipient whose names were never resolved (the enrichment degrade path)
    /// still renders — the tags substitute to <see cref="string.Empty"/> rather than staying
    /// literal or failing the broadcast.
    /// </summary>
    [TestMethod]
    public async Task MissingEnrichmentNames_DegradeToEmpty_AndTheBroadcastStillQueues()
    {
        var contact = Guid.NewGuid();
        var (db, broadcaster) = BuildWithTemplate(
            nameof(MissingEnrichmentNames_DegradeToEmpty_AndTheBroadcastStillQueues),
            new Dictionary<Guid, string> { [contact] = "guardian@example.com" },
            new ResolvedNotificationTemplate(
                Guid.NewGuid(), "Update for {{ContactName}}",
                "<p>{{StudentName}} has a new assignment.</p>"));

        var recipient = Recipient(contact, ContactChannel.Email, "tok-unenriched");
        recipient.ContactDisplayName.Should().BeEmpty("nothing has enriched this recipient");

        await broadcaster.BroadcastPublishedAsync(
            new AssignmentPublishedContext(AssignmentId, "Math homework", DateTimeOffset.UtcNow, null, null, null, [recipient]));

        var row = (await db.NotificationLogs.ToListAsync()).Should().ContainSingle().Subject;
        row.DeliveryStatus.Should().Be(NotificationDeliveryStatus.Queued);
        row.Subject.Should().Be("Update for ", "an unresolved name is empty, not a literal tag");
        row.BodyHtml.Should().NotContain("StudentName");
    }
}
