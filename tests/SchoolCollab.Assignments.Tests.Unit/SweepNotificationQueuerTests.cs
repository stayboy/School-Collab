using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Assignments.Core.Services.Delivery;
using SchoolCollab.Assignments.Worker.Services;
using SchoolCollab.Core.DeepLinks;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// Round notification-templates-b2a (WS3) — the E3 sweep's render + queue step:
/// tenant templates render and record the selected id, missing templates fall back to the
/// built-in default, SMS/WhatsApp recipients are ensure-minted a short-link code at queue
/// time, and email recipients keep the full deep link.
/// </summary>
[TestClass]
public class SweepNotificationQueuerTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid AssignmentId = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid ContactId = Guid.Parse("00000000-0000-0000-0000-000000000004");
    private static readonly Guid RecipientId = Guid.Parse("00000000-0000-0000-0000-000000000005");
    private static readonly Guid TemplateId = Guid.Parse("e0000000-0000-0000-0000-00000000000e");

    private static (AssignmentsDbContext Db, TenantProvider Tenants) BuildDb()
    {
        var services = new ServiceCollection();
        services.AddTenancy();
        services.AddDbContext<AssignmentsDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));
        var provider = services.BuildServiceProvider();

        var db = provider.GetRequiredService<AssignmentsDbContext>();
        var tenants = (TenantProvider)provider.GetRequiredService<ITenantProvider>();
        tenants.SetTenant(new TenantContext(TenantId, "SchoolA", TenantType.School));
        db.Database.EnsureCreated();
        return (db, tenants);
    }

    private static AssignmentRecipient SmsRecipientWithTokenButNoCode()
    {
        var r = AssignmentRecipient.Create(
            TenantId, AssignmentId, ContactOwnerType.Guardian, RecipientId, RecipientId,
            ContactId, ContactChannel.SMS, role: null,
            notifyOnBroadcast: true, subscriptionActive: true);
        r.AttachDeepLink("deep-token", DateTimeOffset.UtcNow.AddDays(7), shortLinkCode: null);
        return r;
    }

    private static AssignmentRecipient EmailRecipientWithToken()
    {
        var r = AssignmentRecipient.Create(
            TenantId, AssignmentId, ContactOwnerType.Guardian, RecipientId, RecipientId,
            ContactId, ContactChannel.Email, role: null,
            notifyOnBroadcast: true, subscriptionActive: true);
        r.AttachDeepLink("deep-token", DateTimeOffset.UtcNow.AddDays(7), shortLinkCode: null);
        return r;
    }

    private static SweepRenderDependencies Dependencies(INotificationTemplateResolver resolver) =>
        new(resolver, new NotificationLinkOptions { PublicBaseUrl = "http://localhost" },
            new Dictionary<(NotificationKind, NotificationChannel), ResolvedNotificationTemplate?>());

    private sealed class FixedAddressResolver(string address) : IContactAddressResolver
    {
        public Task<string?> ResolveAddressAsync(
            Guid contactId, ContactOwnerType ownerType, Guid ownerId, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(address);
    }

    [TestMethod]
    public async Task ResolveTemplateAsync_TenantTemplate_RendersAndRecordsTemplateId()
    {
        var (db, _) = BuildDb();
        var recipient = EmailRecipientWithToken();
        db.AssignmentRecipients.Add(recipient);
        await db.SaveChangesAsync();

        var resolver = new FixedTemplateResolver(new ResolvedNotificationTemplate(
            TemplateId, "Reminder: {{AssignmentTitle}}", "<p>Reminder: {{AssignmentTitle}}</p>"));

        await SweepNotificationQueuer.QueueAsync(
            db, new FixedAddressResolver("guardian@example.com"), NullLogger.Instance, TimeProvider.System,
            Dependencies(resolver), TenantId, AssignmentId, "Math", recipient.Id, ContactId,
            ContactOwnerType.Guardian, RecipientId, NotificationChannel.Email,
            recipient.DeepLinkToken, recipient.DeepLinkExpiresAt,
            NotificationKind.Reminder, CancellationToken.None);

        var row = await db.NotificationLogs.SingleAsync();
        row.TemplateId.Should().Be(TemplateId);
        row.Subject.Should().Be("Reminder: Math");
    }

    [TestMethod]
    public async Task ResolveTemplateAsync_NoTemplate_UsesBuiltInDefault_AndRecordsNullTemplateId()
    {
        var (db, _) = BuildDb();
        var recipient = EmailRecipientWithToken();
        db.AssignmentRecipients.Add(recipient);
        await db.SaveChangesAsync();

        await SweepNotificationQueuer.QueueAsync(
            db, new FixedAddressResolver("guardian@example.com"), NullLogger.Instance, TimeProvider.System,
            Dependencies(new NullNotificationTemplateResolver()), TenantId, AssignmentId, "Math", recipient.Id, ContactId,
            ContactOwnerType.Guardian, RecipientId, NotificationChannel.Email,
            recipient.DeepLinkToken, recipient.DeepLinkExpiresAt,
            NotificationKind.Reminder, CancellationToken.None);

        var row = await db.NotificationLogs.SingleAsync();
        row.TemplateId.Should().BeNull("a built-in default has no stored template id");
        row.Subject.Should().NotBeNullOrWhiteSpace();
        row.BodyHtml.Should().NotBeNullOrWhiteSpace();
    }

    [TestMethod]
    public async Task EnsureShortLinkCodeAsync_SmsRecipientWithoutCode_GainsCode_AndLinkUsesShortLink()
    {
        var (db, _) = BuildDb();
        var recipient = SmsRecipientWithTokenButNoCode();
        db.AssignmentRecipients.Add(recipient);
        await db.SaveChangesAsync();

        await SweepNotificationQueuer.QueueAsync(
            db, new FixedAddressResolver("+233555123456"), NullLogger.Instance, TimeProvider.System,
            Dependencies(new NullNotificationTemplateResolver()), TenantId, AssignmentId, "Math", recipient.Id, ContactId,
            ContactOwnerType.Guardian, RecipientId, NotificationChannel.SMS,
            recipient.DeepLinkToken, recipient.DeepLinkExpiresAt,
            NotificationKind.Reminder, CancellationToken.None);

        recipient.ShortLinkCode.Should().NotBeNullOrWhiteSpace(
            "an SMS recipient predating short links must be ensure-minted at queue time");
        recipient.ShortLinkCode!.Length.Should().Be(DeepLinkConstants.ShortLinkCodeLength);

        var row = await db.NotificationLogs.SingleAsync();
        row.BodyHtml.Should().Contain($"/l/{recipient.ShortLinkCode}",
            "the rendered SMS link must use the short link");
        row.BodyHtml.Should().NotContain("/deeplink/",
            "the rendered SMS link must not expose the full deep-link token");
    }

    [TestMethod]
    public async Task EnsureShortLinkCodeAsync_EmailRecipient_KeepsDeepLinkToken()
    {
        var (db, _) = BuildDb();
        var recipient = EmailRecipientWithToken();
        db.AssignmentRecipients.Add(recipient);
        await db.SaveChangesAsync();

        await SweepNotificationQueuer.QueueAsync(
            db, new FixedAddressResolver("guardian@example.com"), NullLogger.Instance, TimeProvider.System,
            Dependencies(new NullNotificationTemplateResolver()), TenantId, AssignmentId, "Math", recipient.Id, ContactId,
            ContactOwnerType.Guardian, RecipientId, NotificationChannel.Email,
            recipient.DeepLinkToken, recipient.DeepLinkExpiresAt,
            NotificationKind.Reminder, CancellationToken.None);

        recipient.ShortLinkCode.Should().BeNull("email recipients never carry a short-link code");

        var row = await db.NotificationLogs.SingleAsync();
        row.BodyHtml.Should().Contain("/deeplink/deep-token",
            "the rendered email link keeps the full deep-link token");
    }

    private sealed class FixedTemplateResolver(ResolvedNotificationTemplate template) : INotificationTemplateResolver
    {
        public Task<ResolvedNotificationTemplate?> ResolveAsync(
            NotificationKind kind, NotificationChannel channel, Guid tenantId, CancellationToken cancellationToken = default)
            => Task.FromResult<ResolvedNotificationTemplate?>(template);

        public Task<NotificationTemplateSlot?> ResolveSlotAsync(
            NotificationKind kind, NotificationChannel channel, Guid tenantId, CancellationToken cancellationToken = default)
            => Task.FromResult<NotificationTemplateSlot?>(
                new NotificationTemplateSlot(template.TemplateId, kind, channel, template.Subject, template.Body, []));

        public async Task<NotificationTemplateSlotRead> ReadSlotAsync(
            NotificationKind kind, NotificationChannel channel, Guid tenantId, CancellationToken cancellationToken = default)
        {
            var slot = await ResolveSlotAsync(kind, channel, tenantId, cancellationToken);
            return new NotificationTemplateSlotRead(
                slot is null ? NotificationTemplateSlotState.NotSet : NotificationTemplateSlotState.Found, slot);
        }
    }
}
