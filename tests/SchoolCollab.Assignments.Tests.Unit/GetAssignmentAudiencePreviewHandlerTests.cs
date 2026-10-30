using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetAssignmentAudiencePreview;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// Round <c>assignment-view-page</c> (spec §4.2 — Q152) — the DRAFT audience read. The view page
/// draws one table in both states, so the draft needs rows before anything has been published: the
/// persisted targets resolved through the same chain publish runs, policy-filtered, carrying
/// <see cref="AudienceDeliveryStateDto.NotPublishingYet"/> and no delivery stamp — and it must write
/// nothing at all.
/// </summary>
[TestClass]
public class GetAssignmentAudiencePreviewHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-0000000000aa");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000042");
    private static readonly Guid GradeId = Guid.Parse("00000000-0000-0000-0000-0000000000d1");
    private static readonly Guid StudentId = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    private static (AssignmentsDbContext Db, ServiceProvider Provider) Build(string database)
    {
        var services = new ServiceCollection();
        services.AddTenancy();
        services.AddDbContext<AssignmentsDbContext>(options => options.UseInMemoryDatabase(database));
        var provider = services.BuildServiceProvider();

        var db = provider.GetRequiredService<AssignmentsDbContext>();
        ((TenantProvider)provider.GetRequiredService<ITenantProvider>())
            .SetTenant(new TenantContext(TenantId, "SchoolA", TenantType.School));
        db.Database.EnsureCreated();
        return (db, provider);
    }

    private static Assignment DraftWithGradeTarget(string title)
    {
        var assignment = Assignment.Create(title, null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, null, null, TeacherId)
            .WithTenant(TenantId);
        return assignment.WithGradeTarget(GradeId);
    }

    private static GetAssignmentAudiencePreviewHandler Handler(
        AssignmentsDbContext db, Guid[] cohort, params SubscriberInfo[] subscribers) =>
        Build(db, cohort, new FakeNotificationPolicyResolver(), subscribers);

    private static GetAssignmentAudiencePreviewHandler HandlerWithPolicy(
        AssignmentsDbContext db, Guid[] cohort, INotificationPolicyResolver policyResolver,
        params SubscriberInfo[] subscribers) =>
        Build(db, cohort, policyResolver, subscribers);

    private static GetAssignmentAudiencePreviewHandler Build(
        AssignmentsDbContext db, Guid[] cohort, INotificationPolicyResolver policyResolver,
        SubscriberInfo[] subscribers) =>
        new(db,
            new FakeAssignmentTargetResolver { StudentIds = cohort },
            new FixedSubscriberContactResolver(subscribers),
            policyResolver,
            new FakeAssignmentPolicyResolver(),
            NullLogger<GetAssignmentAudiencePreviewHandler>.Instance);

    private static SubscriberInfo Subscriber(Guid contactId, ContactChannel channel, string studentName) =>
        new(contactId, ContactOwnerType.Guardian, contactId, StudentId, channel, GuardianRole.Primary,
            "Guardian Name", studentName);

    [TestMethod]
    public async Task ADraft_WithTargets_RendersRowsWithNoDeliveryState_AndWritesNothing()
    {
        var (db, _) = Build(nameof(ADraft_WithTargets_RendersRowsWithNoDeliveryState_AndWritesNothing));
        var assignment = DraftWithGradeTarget("Yeast respiration write-up");
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();

        var before = db.AssignmentRecipients.Count();

        var result = await Handler(db, [StudentId],
            Subscriber(Guid.NewGuid(), ContactChannel.Email, "Okafor, Ngozi"))
            .HandleAsync(new GetAssignmentAudiencePreview(assignment.Id));

        var row = result.Rows.Should().ContainSingle().Subject;
        row.StudentName.Should().Be("Okafor, Ngozi", "the name rides the same batched subscriber read");
        row.Reached.Should().BeTrue();

        var contact = row.Contacts.Should().ContainSingle().Subject;
        contact.DeliveryState.Should().Be(AudienceDeliveryStateDto.NotPublishingYet,
            "nothing has been sent, and the panel says so rather than inventing a state");
        contact.SentAt.Should().BeNull();
        contact.OpenedAt.Should().BeNull();
        contact.Channel.Should().Be(ContactChannelDto.Email);

        result.Degraded.Should().BeFalse();
        db.AssignmentRecipients.Count().Should().Be(before, "AC7 — the preview persists nothing");
        db.NotificationLogs.Count().Should().Be(0, "AC7 — the preview writes no notification row");
    }

    [TestMethod]
    public async Task AStudentWithNoSubscriber_IsUnreachedAndContactless()
    {
        var (db, _) = Build(nameof(AStudentWithNoSubscriber_IsUnreachedAndContactless));
        var assignment = DraftWithGradeTarget("Yeast respiration write-up");
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();

        var result = await Handler(db, [StudentId]).HandleAsync(new GetAssignmentAudiencePreview(assignment.Id));

        var row = result.Rows.Should().ContainSingle().Subject;
        row.Reached.Should().BeFalse("AC7 — a student with no subscriber renders Reached = false");
        row.Contacts.Should().BeEmpty();
        row.StudentName.Should().BeEmpty("no name resolved is empty, never invented");
    }

    [TestMethod]
    public async Task AContactThePublishFilterWouldDrop_IsReportedBlockedByPolicy()
    {
        var (db, _) = Build(nameof(AContactThePublishFilterWouldDrop_IsReportedBlockedByPolicy));
        var assignment = DraftWithGradeTarget("Yeast respiration write-up");
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();

        var result = await HandlerWithPolicy(db, [StudentId], new BlockedEmailPolicyResolver(),
            Subscriber(Guid.NewGuid(), ContactChannel.Email, "Okafor, Ngozi"))
            .HandleAsync(new GetAssignmentAudiencePreview(assignment.Id));

        var row = result.Rows.Should().ContainSingle().Subject;
        row.Contacts.Should().ContainSingle("a blocked contact is shown with its verdict, not silently omitted");
        row.Contacts[0].DeliveryState.Should().Be(AudienceDeliveryStateDto.BlockedByPolicy);
        row.Reached.Should().BeFalse("a contact the filter drops will not be notified");
    }

    /// <summary>A policy-service outage: the effective policy read throws, as the HTTP resolver does
    /// when settings answers 5xx.</summary>
    private sealed class ThrowingPolicyResolver : INotificationPolicyResolver
    {
        public Task<EffectiveNotificationPolicy> ResolveEffectiveAsync(
            Guid tenantId, Guid? gradeLevelId, CancellationToken cancellationToken = default) =>
            Task.FromException<EffectiveNotificationPolicy>(new HttpRequestException("Policy service unavailable"));
    }

    [TestMethod]
    public async Task APolicyOutage_DegradesThePayload_InsteadOfPropagating()
    {
        var (db, _) = Build(nameof(APolicyOutage_DegradesThePayload_InsteadOfPropagating));
        var assignment = DraftWithGradeTarget("Yeast respiration write-up");
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();

        var result = await HandlerWithPolicy(db, [StudentId], new ThrowingPolicyResolver(),
            Subscriber(Guid.NewGuid(), ContactChannel.Email, "Okafor, Ngozi"))
            .HandleAsync(new GetAssignmentAudiencePreview(assignment.Id));

        result.Degraded.Should().BeTrue("the panel degrades to its stated empty state instead of the endpoint 500ing");
        result.Rows.Should().BeEmpty();
    }

    /// <summary>The effective policy with the email medium blocked — the one filter input that
    /// turns a resolved contact into "will not be notified".</summary>
    private sealed class BlockedEmailPolicyResolver : INotificationPolicyResolver
    {
        public Task<EffectiveNotificationPolicy> ResolveEffectiveAsync(
            Guid tenantId, Guid? gradeLevelId, CancellationToken cancellationToken = default) =>
            Task.FromResult(FakeNotificationPolicyResolver.Empty with
            {
                BlockedChannels = [NotificationChannel.Email],
                BlockedChannelsFromOverride = true
            });
    }

    [TestMethod]
    public async Task AnAssignmentWithNoTargets_RendersNoRows()
    {
        var (db, _) = Build(nameof(AnAssignmentWithNoTargets_RendersNoRows));
        var assignment = Assignment.Create("Bare draft", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, null, null, TeacherId).WithTenant(TenantId);
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();

        var result = await Handler(db, [StudentId]).HandleAsync(new GetAssignmentAudiencePreview(assignment.Id));

        result.Rows.Should().BeEmpty("nothing is targeted, so nothing resolves");
        result.Degraded.Should().BeFalse();
    }
}
