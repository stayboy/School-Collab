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
/// Rounds notification-templates-b2a/b2c (spec §3.7 / WS5) — the publish dialog's pre-select read:
/// per stamped channel the tenant DEFAULT row (mirroring the narrowed GET) plus the ordered list of
/// <b>applicable</b> candidates for this assignment, the effective policy's channels, and the
/// assignment's stored stamps. Tenant-scoped: a foreign assignment reads as absent, so the route
/// answers 404.
/// </summary>
[TestClass]
public class GetPublishOptionsHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherTenant = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000010");
    private static readonly Guid GradeId = Guid.Parse("00000000-0000-0000-0000-000000000011");
    private static readonly Guid OtherGradeId = Guid.Parse("00000000-0000-0000-0000-000000000012");

    private static readonly Guid EmailTemplate = Guid.Parse("e0000000-0000-0000-0000-00000000000e");
    private static readonly Guid SmsTemplate = Guid.Parse("e0000000-0000-0000-0000-000000000055");
    private static readonly Guid GradeEmailTemplate = Guid.Parse("e0000000-0000-0000-0000-000000000066");
    private static readonly Guid TwoDimEmailTemplate = Guid.Parse("e0000000-0000-0000-0000-000000000077");
    private static readonly Guid OtherGradeEmailTemplate = Guid.Parse("e0000000-0000-0000-0000-000000000088");
    private static readonly Guid NewerGradeEmailTemplate = Guid.Parse("e0000000-0000-0000-0000-000000000099");

    private static readonly DateTimeOffset Oldest = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    private static NotificationTemplateCandidate Candidate(
        Guid id, string subject, DateTimeOffset createdAt, params NotificationTemplateConstraint[] constraints) =>
        new(id, subject, "<p>body</p>", constraints, createdAt);

    /// <summary>
    /// A tenant whose email slot carries a default plus three constrained candidates (one applicable
    /// with two dimensions, one applicable with one, one for another grade) and whose SMS slot carries
    /// only its default. The assignment targets <see cref="GradeId"/> on a topic, type Digital.
    /// </summary>
    private sealed class SeveralCandidateResolver(bool unreadableEmail = false) : INotificationTemplateResolver
    {
        public Task<NotificationTemplateCandidatesRead> ReadCandidatesAsync(
            NotificationKind kind, NotificationChannel channel, Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(channel switch
            {
                NotificationChannel.Email when unreadableEmail => NotificationTemplateCandidatesRead.Unreadable,
                NotificationChannel.Email => NotificationTemplateCandidatesRead.Found(
                [
                    Candidate(EmailTemplate, "New task: {{Title}}", Oldest),
                    Candidate(GradeEmailTemplate, "grade only", Oldest.AddDays(1),
                        new NotificationTemplateConstraint("GradeLevel", GradeId.ToString("D"))),
                    Candidate(TwoDimEmailTemplate, "grade + type", Oldest.AddDays(2),
                        new NotificationTemplateConstraint("GradeLevel", GradeId.ToString("D")),
                        new NotificationTemplateConstraint("AssignmentType", "Digital")),
                    Candidate(OtherGradeEmailTemplate, "other grade", Oldest.AddDays(3),
                        new NotificationTemplateConstraint("GradeLevel", OtherGradeId.ToString("D"))),
                    // Same specificity as GradeEmailTemplate — the Q67 tie-break must order the two
                    // by CreatedAt, exactly as the render path does.
                    Candidate(NewerGradeEmailTemplate, "grade, newer", Oldest.AddDays(4),
                        new NotificationTemplateConstraint("GradeLevel", GradeId.ToString("D"))),
                ]),
                NotificationChannel.SMS => NotificationTemplateCandidatesRead.Found(
                    [Candidate(SmsTemplate, subject: null, createdAt: Oldest)]),
                _ => NotificationTemplateCandidatesRead.NotSet,
            });
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

    /// <summary>
    /// Seeds one assignment (targeting <see cref="GradeId"/> on <see cref="TopicId"/>, type Digital)
    /// under <paramref name="seedTenant"/> and returns a handler whose tenant context is
    /// <paramref name="runTenant"/> — equal for the same-tenant cases, different for the foreign case.
    /// Uses the repo's standard in-memory harness (synchronous <c>EnsureCreated</c> +
    /// <c>TenantProvider.SetTenant</c>) so the query filter and save guard agree with the active tenant.
    /// </summary>
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
        assignment.SetTargets([(TargetKind.GradeLevel, (Guid?)GradeId)], seedTenant);
        db.Assignments.Add(assignment);
        db.SaveChanges();

        tenants.SetTenant(new TenantContext(runTenant ?? seedTenant, "SchoolA", TenantType.School));

        var handler = new GetPublishOptionsHandler(
            new AssignmentRepository(db), templates ?? new SeveralCandidateResolver(),
            new PolicyResolver(policy ?? Policy()), tenants);

        return (db, handler, assignment, tenants);
    }

    [TestMethod]
    public async Task EmailSlot_ListsOnlyTheApplicableCandidates_MostSpecificFirst()
    {
        var (db, handler, assignment, _) = Build(TenantId);

        var options = await handler.HandleAsync(new GetPublishOptions(assignment.Id));

        options.Channels.Should().HaveCount(2, "§3.7 pins two stamped slots");
        var email = options.Channels.Single(c => c.Channel == ContactChannelDto.Email);

        email.Templates.Select(t => t.TemplateId).Should().ContainInOrder(
            TwoDimEmailTemplate, GradeEmailTemplate, NewerGradeEmailTemplate, EmailTemplate);
        email.Templates.Should().NotContain(t => t.TemplateId == OtherGradeEmailTemplate,
            "a candidate constrained in a value the assignment does not have is not applicable");

        email.Templates[0].IsDefault.Should().BeFalse();
        email.Templates.Should().OnlyContain(t => t.Constraints.Count > 0 || t.IsDefault);
        email.Templates[^1].IsDefault.Should().BeTrue("the unconstrained row is the tenant default");
        email.Templates[^1].Constraints.Should().BeEmpty();

        await db.DisposeAsync();
    }

    [TestMethod]
    public async Task SingleSlotFields_ArePinnedToTheTenantDefaultRow()
    {
        // Round b2c: the legacy fields stay meaningful with candidates by meaning "the default" —
        // the same row the narrowed GET …/{kind}/{channel} returns.
        var (db, handler, assignment, _) = Build(TenantId);

        var options = await handler.HandleAsync(new GetPublishOptions(assignment.Id));

        var email = options.Channels.Single(c => c.Channel == ContactChannelDto.Email);
        email.Kind.Should().Be(NotificationKindDto.Publish);
        email.TemplateId.Should().Be(EmailTemplate, "not the most specific applicable candidate");
        email.Subject.Should().Be("New task: {{Title}}");
        email.Constraints.Should().BeEmpty("the default row carries no constraints");

        var sms = options.Channels.Single(c => c.Channel == ContactChannelDto.SMS);
        sms.TemplateId.Should().Be(SmsTemplate);
        sms.Subject.Should().BeNull("the subject is ignored for SMS (§5)");

        await db.DisposeAsync();
    }

    [TestMethod]
    public async Task Response_ReportsThePolicyChannelsAndTheStoredStamps()
    {
        var (db, handler, assignment, _) = Build(
            TenantId,
            policy: Policy(preferred: [NotificationChannel.Email], blocked: [NotificationChannel.WhatsApp]));
        assignment.StampPublishTemplates(GradeEmailTemplate, null);
        await db.SaveChangesAsync();

        var options = await handler.HandleAsync(new GetPublishOptions(assignment.Id));

        options.PolicyPreferredChannelOrder.Should().ContainSingle().Which.Should().Be(ContactChannelDto.Email);
        options.PolicyBlockedChannels.Should().ContainSingle().Which.Should().Be(ContactChannelDto.WhatsApp);
        options.PublishEmailTemplateId.Should().Be(GradeEmailTemplate, "stored stamps are echoed unchanged");
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
        options.Channels.Should().OnlyContain(c => c.Templates.Count == 0);

        await db.DisposeAsync();
    }

    [TestMethod]
    public async Task UnreadableSettings_OffersNoTemplates_SoTheDialogShowsTheDefault()
    {
        var (db, handler, assignment, _) = Build(TenantId, templates: new SeveralCandidateResolver(unreadableEmail: true));

        var options = await handler.HandleAsync(new GetPublishOptions(assignment.Id));

        options.Channels.Single(c => c.Channel == ContactChannelDto.Email).Templates.Should().BeEmpty();
        options.Channels.Single(c => c.Channel == ContactChannelDto.Email).TemplateId.Should().BeNull();
        options.Channels.Single(c => c.Channel == ContactChannelDto.SMS).Templates.Should().NotBeEmpty(
            "the failure is per channel — the readable slot still offers its candidates");

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
            new AssignmentRepository(db), new SeveralCandidateResolver(),
            new PolicyResolver(Policy()), tenants);
        var ownTenantOptions = await ownHandler.HandleAsync(new GetPublishOptions(assignment.Id));
        ownTenantOptions.Should().NotBeNull("the same row must be visible under its own tenant");

        tenants.SetTenant(new TenantContext(TenantId, "SchoolA", TenantType.School));
        var act = () => handler.HandleAsync(new GetPublishOptions(assignment.Id));
        await act.Should().ThrowAsync<AssignmentNotFoundException>();

        await db.DisposeAsync();
    }
}
