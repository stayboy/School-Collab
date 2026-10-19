using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetPublishOptions;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// Round notification-templates-b2a (spec §3.7 / WS5) — the publish dialog's pre-select read:
/// per stamped channel the tenant's single live Publish template (id, kind, channel and the
/// display-only constraint metadata), the effective policy's channels, and the assignment's stored
/// stamps. Tenant-scoped: a foreign assignment reads as absent, so the route answers 404.
/// </summary>
[TestClass]
public class GetPublishOptionsHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherTenant = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000010");

    private static readonly Guid EmailTemplate = Guid.Parse("e0000000-0000-0000-0000-00000000000e");
    private static readonly Guid SmsTemplate = Guid.Parse("e0000000-0000-0000-0000-000000000055");

    /// <summary>A tenant with BOTH stamped slots live, the email slot carrying constraints.</summary>
    private sealed class TwoSlotResolver : INotificationTemplateResolver
    {
        public Task<ResolvedNotificationTemplate?> ResolveAsync(
            NotificationKind kind, NotificationChannel channel, Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ResolvedNotificationTemplate?>(null);

        public Task<NotificationTemplateSlot?> ResolveSlotAsync(
            NotificationKind kind, NotificationChannel channel, Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<NotificationTemplateSlot?>(channel switch
            {
                NotificationChannel.Email => new NotificationTemplateSlot(
                    EmailTemplate, kind, channel, "New task: {{Title}}", "<p>email</p>",
                    [new NotificationTemplateConstraint("GradeLevel", "Grade 3")]),
                NotificationChannel.SMS => new NotificationTemplateSlot(
                    SmsTemplate, kind, channel, null, "plain text", []),
                _ => null,
            });

        public async Task<NotificationTemplateSlotRead> ReadSlotAsync(
            NotificationKind kind, NotificationChannel channel, Guid tenantId, CancellationToken cancellationToken = default)
        {
            var slot = await ResolveSlotAsync(kind, channel, tenantId, cancellationToken);
            return new NotificationTemplateSlotRead(
                slot is null ? NotificationTemplateSlotState.NotSet : NotificationTemplateSlotState.Found, slot);
        }
    }

    private sealed class PolicyResolver(EffectiveNotificationPolicy policy) : INotificationPolicyResolver
    {
        public Task<EffectiveNotificationPolicy> ResolveEffectiveAsync(
            Guid tenantId, Guid? gradeLevelId, CancellationToken ct = default) => Task.FromResult(policy);
    }

    private static EffectiveNotificationPolicy Policy(
        NotificationChannel[]? preferred = null, NotificationChannel[]? blocked = null) =>
        new(preferred ?? [], blocked ?? [], null, null, null, null, null, null,
            PreferredChannelOrderFromOverride: false, BlockedChannelsFromOverride: false,
            MaxNotificationsFromOverride: false, MaxRemindersFromOverride: false,
            ReminderIntervalHoursFromOverride: false, LinkValidityDaysFromOverride: false,
            SendoutTimeOfDayFromOverride: false, SendoutIntervalMinutesFromOverride: false);

    /// <summary>Seeds one assignment under <paramref name="seedTenant"/> and returns a handler whose
    /// tenant context is <paramref name="runTenant"/> — equal for the same-tenant cases, different
    /// for the foreign-assignment case. Uses the repo's standard in-memory harness
    /// (synchronous <c>EnsureCreated</c> + <c>TenantProvider.SetTenant</c>) so the query filter and
    /// save guard agree with the active tenant.</summary>
    private static (AssignmentsDbContext Db, GetPublishOptionsHandler Handler, Assignment Assignment, TenantProvider Tenants)
        Build(
            Guid seedTenant,
            Guid? runTenant = null,
            INotificationTemplateResolver? templates = null,
            EffectiveNotificationPolicy? policy = null)
    {
        var services = new ServiceCollection();
        services.AddTenancy();
        services.AddDbContext<AssignmentsDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));
        var provider = services.BuildServiceProvider();

        var db = provider.GetRequiredService<AssignmentsDbContext>();
        var tenants = (TenantProvider)provider.GetRequiredService<ITenantProvider>();
        tenants.SetTenant(new TenantContext(seedTenant, "SchoolA", TenantType.School));
        db.Database.EnsureCreated();

        var assignment = Assignment.Create("Math", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, null, null, TeacherId).WithTenant(seedTenant);
        db.Assignments.Add(assignment);
        db.SaveChanges();

        tenants.SetTenant(new TenantContext(runTenant ?? seedTenant, "SchoolA", TenantType.School));

        var handler = new GetPublishOptionsHandler(
            new AssignmentRepository(db), templates ?? new TwoSlotResolver(),
            new PolicyResolver(policy ?? Policy()), tenants);

        return (db, handler, assignment, tenants);
    }

    [TestMethod]
    public async Task Response_CarriesBothStampedSlots_WithTheLiveTemplateAndConstraints()
    {
        var (db, handler, assignment, _) = Build(TenantId);

        var options = await handler.HandleAsync(new GetPublishOptions(assignment.Id));

        options.Channels.Should().HaveCount(2, "§3.7 pins two stamped slots");
        var email = options.Channels.Single(c => c.Channel == ContactChannelDto.Email);
        email.Kind.Should().Be(NotificationKindDto.Publish);
        email.TemplateId.Should().Be(EmailTemplate);
        email.Subject.Should().Be("New task: {{Title}}");
        email.Constraints.Should().ContainSingle()
            .Which.Should().Be(new PublishTemplateConstraintDto("GradeLevel", "Grade 3"));

        var sms = options.Channels.Single(c => c.Channel == ContactChannelDto.SMS);
        sms.TemplateId.Should().Be(SmsTemplate);
        sms.Subject.Should().BeNull("the subject is ignored for SMS (§5)");
        sms.Constraints.Should().BeEmpty();

        await db.DisposeAsync();
    }

    [TestMethod]
    public async Task Response_ReportsThePolicyChannelsAndTheStoredStamps()
    {
        var (db, handler, assignment, tenants) = Build(
            TenantId,
            policy: Policy(preferred: [NotificationChannel.Email], blocked: [NotificationChannel.WhatsApp]));
        assignment.StampPublishTemplates(EmailTemplate, null);
        await db.SaveChangesAsync();

        var options = await handler.HandleAsync(new GetPublishOptions(assignment.Id));

        options.PolicyPreferredChannelOrder.Should().ContainSingle().Which.Should().Be(ContactChannelDto.Email);
        options.PolicyBlockedChannels.Should().ContainSingle().Which.Should().Be(ContactChannelDto.WhatsApp);
        options.PublishEmailTemplateId.Should().Be(EmailTemplate);
        options.PublishSmsTemplateId.Should().BeNull("a cleared stamp is the \"no author choice\" state");

        await db.DisposeAsync();
    }

    [TestMethod]
    public async Task NoConfiguredTemplate_ReportsAnEmptySlot_SoTheBuiltInDefaultApplies()
    {
        var (db, handler, assignment, _) = Build(TenantId, templates: new NullNotificationTemplateResolver());

        var options = await handler.HandleAsync(new GetPublishOptions(assignment.Id));

        options.Channels.Should().HaveCount(2).And.OnlyContain(c => c.TemplateId == null);
        options.Channels.Should().OnlyContain(c => c.Constraints.Count == 0);

        await db.DisposeAsync();
    }

    [TestMethod]
    public async Task ForeignAssignment_ReadsAsAbsent()
    {
        // Discriminating: the same row is present when read under its own tenant and absent when
        // read under another tenant — so the test cannot pass vacuously because the row was never
        // seeded or because the tenant filter is not applied.
        var (db, handler, assignment, tenants) = Build(OtherTenant, runTenant: TenantId);

        tenants.SetTenant(new TenantContext(OtherTenant, "SchoolB", TenantType.School));
        var ownHandler = new GetPublishOptionsHandler(
            new AssignmentRepository(db), new TwoSlotResolver(),
            new PolicyResolver(Policy()), tenants);
        var ownTenantOptions = await ownHandler.HandleAsync(new GetPublishOptions(assignment.Id));
        ownTenantOptions.Should().NotBeNull("the same row must be visible under its own tenant");

        tenants.SetTenant(new TenantContext(TenantId, "SchoolA", TenantType.School));
        var act = () => handler.HandleAsync(new GetPublishOptions(assignment.Id));
        await act.Should().ThrowAsync<AssignmentNotFoundException>();

        await db.DisposeAsync();
    }
}
