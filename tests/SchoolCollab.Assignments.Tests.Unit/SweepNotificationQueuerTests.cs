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
    private static readonly Guid GradeId = Guid.Parse("00000000-0000-0000-0000-0000000000f1");

    private static readonly DateTimeOffset TemplateCreatedAt = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

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

    private static AssignmentRecipient EmailRecipientWithToken(Guid? assignmentId = null)
    {
        var r = AssignmentRecipient.Create(
            TenantId, assignmentId ?? AssignmentId, ContactOwnerType.Guardian, RecipientId, RecipientId,
            ContactId, ContactChannel.Email, role: null,
            notifyOnBroadcast: true, subscriptionActive: true);
        r.AttachDeepLink("deep-token", DateTimeOffset.UtcNow.AddDays(7), shortLinkCode: null);
        return r;
    }

    /// <summary>
    /// Seeds a published-window assignment targeting one grade on a topic (type Digital) — the
    /// dimensions the sweep's pure evaluation intersects candidates against.
    /// </summary>
    private static Assignment SeedAssignment(AssignmentsDbContext db)
    {
        var assignment = Assignment.Create(
            "Math", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, null, null, TeacherId).WithTenant(TenantId);
        assignment.SetTargets([(TargetKind.GradeLevel, (Guid?)GradeId)], TenantId);
        db.Assignments.Add(assignment);
        db.SaveChanges();
        return assignment;
    }

    private static SweepRenderDependencies Dependencies(INotificationTemplateResolver resolver) =>
        new(resolver, new NotificationLinkOptions { PublicBaseUrl = "http://localhost" },
            new Dictionary<(NotificationKind, NotificationChannel), NotificationTemplateCandidatesRead>(),
            new Dictionary<Guid, AssignmentTemplateContext>());

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

        var resolver = new FixedTemplateResolver(new NotificationTemplateCandidate(
            TemplateId, "Reminder: {{AssignmentTitle}}", "<p>Reminder: {{AssignmentTitle}}</p>", [],
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero)));

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

    /// <summary>
    /// Round notification-templates-b2c — the E3 sweeps stay <b>pure evaluation</b> (no stamps): the
    /// most specific <b>applicable</b> candidate renders and its id is recorded, and nothing applicable
    /// falls to the built-in default. These are the "sweeps most-specific" half of the selection
    /// matrix; the stamped half lives in the broadcaster tests.
    /// </summary>
    [TestMethod]
    public async Task PureEvaluation_SelectsTheMostSpecificApplicableCandidate_AndRecordsItsId()
    {
        var (db, _) = BuildDb();
        var assignment = SeedAssignment(db);
        var recipient = EmailRecipientWithToken(assignment.Id);
        db.AssignmentRecipients.Add(recipient);
        await db.SaveChangesAsync();

        var unconstrained = new NotificationTemplateCandidate(
            Guid.NewGuid(), "Default: {{AssignmentTitle}}", "<p>default</p>", [], TemplateCreatedAt);
        var constrained = new NotificationTemplateCandidate(
            Guid.NewGuid(), "Grade: {{AssignmentTitle}}", "<p>grade</p>",
            [new NotificationTemplateConstraint("GradeLevel", GradeId.ToString("D"))], TemplateCreatedAt);

        await SweepNotificationQueuer.QueueAsync(
            db, new FixedAddressResolver("guardian@example.com"), NullLogger.Instance, TimeProvider.System,
            Dependencies(new FixedTemplateResolver(unconstrained, constrained)), TenantId, assignment.Id, "Math",
            recipient.Id, ContactId, ContactOwnerType.Guardian, RecipientId, NotificationChannel.Email,
            recipient.DeepLinkToken, recipient.DeepLinkExpiresAt,
            NotificationKind.Reminder, CancellationToken.None);

        var row = await db.NotificationLogs.SingleAsync();
        row.TemplateId.Should().Be(constrained.TemplateId, "the most specific applicable candidate wins without a stamp");
        row.BodyHtml.Should().Be("<p>grade</p>");
    }

    [TestMethod]
    public async Task PureEvaluation_WithNothingApplicable_UsesTheBuiltInDefault()
    {
        var (db, _) = BuildDb();
        var assignment = SeedAssignment(db);
        var recipient = EmailRecipientWithToken(assignment.Id);
        db.AssignmentRecipients.Add(recipient);
        await db.SaveChangesAsync();

        var otherGrade = new NotificationTemplateCandidate(
            Guid.NewGuid(), "Other grade", "<p>other</p>",
            [new NotificationTemplateConstraint("GradeLevel", Guid.NewGuid().ToString("D"))], TemplateCreatedAt);

        await SweepNotificationQueuer.QueueAsync(
            db, new FixedAddressResolver("guardian@example.com"), NullLogger.Instance, TimeProvider.System,
            Dependencies(new FixedTemplateResolver(otherGrade)), TenantId, assignment.Id, "Math",
            recipient.Id, ContactId, ContactOwnerType.Guardian, RecipientId, NotificationChannel.Email,
            recipient.DeepLinkToken, recipient.DeepLinkExpiresAt,
            NotificationKind.Reminder, CancellationToken.None);

        var row = await db.NotificationLogs.SingleAsync();
        row.TemplateId.Should().BeNull("a constrained candidate the assignment cannot satisfy never renders");
        row.Subject.Should().NotBeNullOrWhiteSpace();
    }

    private sealed class FixedTemplateResolver(params NotificationTemplateCandidate[] candidates)
        : INotificationTemplateResolver
    {
        public Task<NotificationTemplateCandidatesRead> ReadCandidatesAsync(
            NotificationKind kind, NotificationChannel channel, Guid tenantId, CancellationToken cancellationToken = default)
            => Task.FromResult(candidates.Length == 0
                ? NotificationTemplateCandidatesRead.NotSet
                : NotificationTemplateCandidatesRead.Found(candidates));
    }
}
