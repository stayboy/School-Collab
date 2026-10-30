using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetAssignmentAudience;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// Round <c>assignment-view-page</c> (spec §4.2 — Q130/Q145/Q146) — the published audience read.
/// Pins the join rule rather than assuming it: the notification log is authoritative for the
/// delivery state, <see cref="AssignmentRecipient.OpenedAt"/> supplies "opened" only, a missing log
/// row is <see cref="AudienceDeliveryStateDto.Unknown"/> (never a fabricated <c>Sent</c>), an
/// unreached student is a visible row, and the serialized payload carries no address at all (§4.9).
/// </summary>
[TestClass]
public class GetAssignmentAudienceHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-0000000000aa");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000042");

    private static readonly Guid StudentAdeyemi = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid StudentOkafor = Guid.Parse("00000000-0000-0000-0000-00000000000b");

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

    /// <summary>An assignment whose only target is the all-students leg — the cohort then comes
    /// from the (faked) resolver rather than from an authored grade list.</summary>
    private static Assignment NewAssignment(string title)
    {
        var assignment = Assignment.Create(title, null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, null, null, TeacherId)
            .WithTenant(TenantId);
        return assignment.WithAllStudentsTarget();
    }

    private static AssignmentRecipient Recipient(
        Guid assignmentId, Guid studentId, Guid contactId, ContactOwnerType ownerType, ContactChannel channel,
        GuardianRole? role, bool subscriptionActive = true)
    {
        var recipient = AssignmentRecipient.Create(
            TenantId, assignmentId, ownerType, contactId, studentId, contactId, channel, role,
            notifyOnBroadcast: true, subscriptionActive: subscriptionActive);
        recipient.AttachDisplayNames("Contact", "Student");
        return recipient;
    }

    private static NotificationLog PublishLog(
        Guid assignmentId, Guid recipientId, Guid contactId, NotificationDeliveryStatus status, NotificationChannel channel = NotificationChannel.Email)
    {
        var now = DateTimeOffset.UtcNow;
        var log = NotificationLog.Queue(
            TenantId, assignmentId, recipientId, contactId, channel, NotificationKind.Publish,
            "guardian@example.com", "New assignment", "<p>body</p>", templateId: null, now);

        switch (status)
        {
            case NotificationDeliveryStatus.Sent:
                log.MarkSent(now);
                break;
            case NotificationDeliveryStatus.Failed:
                log.MarkFailed("mailbox full", now.AddMinutes(30), now);
                break;
            case NotificationDeliveryStatus.Skipped:
                log.MarkSkipped("guardian opted out", now);
                break;
        }

        return log;
    }

    private static GetAssignmentAudienceHandler Handler(
        AssignmentsDbContext db, Guid[] cohort, params SubscriberInfo[] subscribers) =>
        new(db,
            new FakeAssignmentTargetResolver { StudentIds = cohort },
            new FixedSubscriberContactResolver(subscribers),
            NullLogger<GetAssignmentAudienceHandler>.Instance);

    private static SubscriberInfo Subscriber(Guid studentId, string studentName, Guid contactId) =>
        new(contactId, ContactOwnerType.Guardian, contactId, studentId, ContactChannel.Email, GuardianRole.Primary,
            "Guardian Name", studentName);

    [TestMethod]
    public async Task Rows_CarryTheCohort_WithTheLogAuthoritativeForDelivery_AndOpenedFromTheRecipient()
    {
        var (db, _) = Build(nameof(Rows_CarryTheCohort_WithTheLogAuthoritativeForDelivery_AndOpenedFromTheRecipient));
        var assignment = NewAssignment("Photosynthesis lab report");
        db.Assignments.Add(assignment);

        var sentContact = Guid.NewGuid();
        var sentRecipient = Recipient(assignment.Id, StudentOkafor, sentContact,
            ContactOwnerType.Guardian, ContactChannel.Email, GuardianRole.Primary);
        sentRecipient.MarkOpened();

        var unreachedRecipient = Recipient(assignment.Id, StudentAdeyemi, Guid.NewGuid(),
            ContactOwnerType.Student, ContactChannel.Email, role: null, subscriptionActive: false);

        db.AssignmentRecipients.AddRange(sentRecipient, unreachedRecipient);
        db.NotificationLogs.Add(PublishLog(assignment.Id, sentRecipient.Id, sentContact, NotificationDeliveryStatus.Sent));
        await db.SaveChangesAsync();

        var result = await Handler(db, [StudentAdeyemi, StudentOkafor],
            Subscriber(StudentAdeyemi, "Adeyemi, Tolu", unreachedRecipient.ContactId),
            Subscriber(StudentOkafor, "Okafor, Ngozi", sentContact))
            .HandleAsync(new GetAssignmentAudience(assignment.Id));

        result.Degraded.Should().BeFalse();
        result.Rows.Select(row => row.StudentName).Should().Equal("Adeyemi, Tolu", "Okafor, Ngozi");

        var unreached = result.Rows[0];
        unreached.Reached.Should().BeFalse("AC2 — a student with no subscribed contact is a visible row");
        unreached.Contacts.Should().ContainSingle("the persisted recipient is still its one contact");
        unreached.Contacts[0].DeliveryState.Should().Be(AudienceDeliveryStateDto.Unknown,
            "no publish log row exists, and a state nobody wrote is never fabricated as Sent");

        var sent = result.Rows[1].Contacts.Single();
        sent.DeliveryState.Should().Be(AudienceDeliveryStateDto.Sent, "the notification log is authoritative");
        sent.SentAt.Should().NotBeNull("the log carries the sent stamp");
        sent.OpenedAt.Should().NotBeNull("AssignmentRecipient.OpenedAt supplies 'opened' only");
        sent.SubscriptionActive.Should().BeTrue();
        result.Rows[1].Reached.Should().BeTrue();
    }

    [TestMethod]
    public async Task AFailedRow_CarriesItsReasonAndRetryFlag_AndSkippedStaysSkipped()
    {
        var (db, _) = Build(nameof(AFailedRow_CarriesItsReasonAndRetryFlag_AndSkippedStaysSkipped));
        var assignment = NewAssignment("Yeast respiration write-up");
        db.Assignments.Add(assignment);

        var failedContact = Guid.NewGuid();
        var skippedContact = Guid.NewGuid();
        var failed = Recipient(assignment.Id, StudentOkafor, failedContact,
            ContactOwnerType.Guardian, ContactChannel.SMS, GuardianRole.Primary);
        var skipped = Recipient(assignment.Id, StudentAdeyemi, skippedContact,
            ContactOwnerType.Guardian, ContactChannel.Email, GuardianRole.Primary);

        db.AssignmentRecipients.AddRange(failed, skipped);
        db.NotificationLogs.AddRange(
            PublishLog(assignment.Id, failed.Id, failedContact, NotificationDeliveryStatus.Failed, NotificationChannel.SMS),
            PublishLog(assignment.Id, skipped.Id, skippedContact, NotificationDeliveryStatus.Skipped));
        await db.SaveChangesAsync();

        var result = await Handler(db, [StudentAdeyemi, StudentOkafor]).HandleAsync(new GetAssignmentAudience(assignment.Id));

        var failedRow = result.Rows.Single(row => row.StudentId == StudentOkafor).Contacts.Single();
        failedRow.DeliveryState.Should().Be(AudienceDeliveryStateDto.Failed);
        failedRow.FailureReason.Should().Be("mailbox full");
        failedRow.RetryScheduled.Should().BeTrue();

        var skippedRow = result.Rows.Single(row => row.StudentId == StudentAdeyemi).Contacts.Single();
        skippedRow.DeliveryState.Should().Be(AudienceDeliveryStateDto.Skipped);
        skippedRow.RetryScheduled.Should().BeFalse();
    }

    [TestMethod]
    public async Task TheSerializedPayload_CarriesNoAddressOfAnyKind()
    {
        var (db, _) = Build(nameof(TheSerializedPayload_CarriesNoAddressOfAnyKind));
        var assignment = NewAssignment("Photosynthesis lab report");
        db.Assignments.Add(assignment);

        var contactId = Guid.NewGuid();
        var recipient = Recipient(assignment.Id, StudentOkafor, contactId,
            ContactOwnerType.Guardian, ContactChannel.Email, GuardianRole.Primary);
        db.NotificationLogs.Add(PublishLog(assignment.Id, recipient.Id, contactId, NotificationDeliveryStatus.Sent));
        db.AssignmentRecipients.Add(recipient);
        await db.SaveChangesAsync();

        var result = await Handler(db, [StudentOkafor], Subscriber(StudentOkafor, "Okafor, Ngozi", contactId))
            .HandleAsync(new GetAssignmentAudience(assignment.Id));

        // Spec §4.9 / AC1: asserted on the PAYLOAD, not the view — a field a later template starts
        // rendering cannot then reintroduce the value. Guid ids and ISO timestamps are structural
        // identifiers, not addresses, so they are removed before the phone-shaped sweep.
        var payload = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        payload.Should().NotContain("@", "no address field exists in the contract");

        var withoutIdentifiers = Regex.Replace(
            payload,
            @"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}|\d{4}-\d{2}-\d{2}T[\d:.]+(?:[+-]\d{2}:\d{2})?",
            string.Empty,
            RegexOptions.IgnoreCase);
        Regex.IsMatch(withoutIdentifiers, @"\+?\d[\d\s().-]{6,}").Should()
            .BeFalse("no phone-shaped string enters the payload");
    }

    [TestMethod]
    public async Task TheCohort_IsCapped_AndSaysSo()
    {
        var (db, _) = Build(nameof(TheCohort_IsCapped_AndSaysSo));
        var assignment = NewAssignment("Whole-school assembly");
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();

        var cohort = Enumerable.Range(0, AudienceLimits.AudienceCap + 1).Select(_ => Guid.NewGuid()).ToArray();
        var subscribers = cohort
            .Select((studentId, index) => Subscriber(studentId, $"Student {index:D3}", Guid.NewGuid()))
            .ToArray();

        var result = await Handler(db, cohort, subscribers).HandleAsync(new GetAssignmentAudience(assignment.Id));

        result.Rows.Should().HaveCount(AudienceLimits.AudienceCap, "Q146 fixes the read at 200 rows");
        result.Truncated.Should().BeTrue("the panel must be able to state the cap");
    }

    [TestMethod]
    public async Task ADirectoryFailure_LeavesTheRowsAndTheDeliveryIntact()
    {
        var (db, _) = Build(nameof(ADirectoryFailure_LeavesTheRowsAndTheDeliveryIntact));
        var assignment = NewAssignment("Photosynthesis lab report");
        db.Assignments.Add(assignment);

        var contactId = Guid.NewGuid();
        var recipient = Recipient(assignment.Id, StudentOkafor, contactId,
            ContactOwnerType.Guardian, ContactChannel.Email, GuardianRole.Primary);
        db.AssignmentRecipients.Add(recipient);
        db.NotificationLogs.Add(PublishLog(assignment.Id, recipient.Id, contactId, NotificationDeliveryStatus.Sent));
        await db.SaveChangesAsync();

        var handler = new GetAssignmentAudienceHandler(
            db,
            new FakeAssignmentTargetResolver { StudentIds = [StudentOkafor] },
            new ThrowingContactResolver(),
            NullLogger<GetAssignmentAudienceHandler>.Instance);

        var result = await handler.HandleAsync(new GetAssignmentAudience(assignment.Id));

        result.Degraded.Should().BeTrue("the split degradation is reported");
        result.Rows.Should().ContainSingle("a name outage never empties the panel");
        result.Rows[0].StudentName.Should().BeEmpty("a name that could not be read is not invented");
        result.Rows[0].Contacts.Single().DeliveryState.Should().Be(AudienceDeliveryStateDto.Sent,
            "a directory failure does not touch delivery — the log still answers");
    }

    [TestMethod]
    public async Task ADirectoryThatReturnsNoName_DegradesTheRead_AndKeepsTheRowsAndDelivery()
    {
        var (db, _) = Build(nameof(ADirectoryThatReturnsNoName_DegradesTheRead_AndKeepsTheRowsAndDelivery));
        var assignment = NewAssignment("Photosynthesis lab report");
        db.Assignments.Add(assignment);

        var contactId = Guid.NewGuid();
        var recipient = Recipient(assignment.Id, StudentOkafor, contactId,
            ContactOwnerType.Guardian, ContactChannel.Email, GuardianRole.Primary);
        db.AssignmentRecipients.Add(recipient);
        db.NotificationLogs.Add(PublishLog(assignment.Id, recipient.Id, contactId, NotificationDeliveryStatus.Sent));
        await db.SaveChangesAsync();

        // The production StudentsContactResolver swallows a per-owner HttpRequestException and degrades
        // that owner to an EMPTY result — this read therefore succeeds and carries no name at all,
        // which is the shape a directory outage actually reaches the handler in.
        var handler = new GetAssignmentAudienceHandler(
            db,
            new FakeAssignmentTargetResolver { StudentIds = [StudentOkafor] },
            new FixedSubscriberContactResolver([]),
            NullLogger<GetAssignmentAudienceHandler>.Instance);

        var result = await handler.HandleAsync(new GetAssignmentAudience(assignment.Id));

        result.Degraded.Should().BeTrue(
            "a swallowed directory failure is observable only as a cohort student with no name, and it is never reported as healthy");
        result.Rows.Should().ContainSingle("the rows survive the name outage");
        result.Rows[0].StudentName.Should().BeEmpty("a name that could not be read is not invented");
        result.Rows[0].Contacts.Single().DeliveryState.Should().Be(AudienceDeliveryStateDto.Sent,
            "a name outage does not touch delivery — the log still answers");
    }

    [TestMethod]
    public async Task AnUnknownAssignment_IsNotFound()
    {
        var (db, _) = Build(nameof(AnUnknownAssignment_IsNotFound));

        var act = () => Handler(db, []).HandleAsync(new GetAssignmentAudience(Guid.NewGuid()));

        await act.Should().ThrowAsync<AssignmentNotFoundException>();
    }

    /// <summary>A directory outage: every subscriber read fails.</summary>
    private sealed class ThrowingContactResolver : IContactResolver
    {
        public Task<IReadOnlyList<SubscriberInfo>> ResolveSubscribersAsync(
            ResolveSubscribersRequest request, CancellationToken cancellationToken = default) =>
            Task.FromException<IReadOnlyList<SubscriberInfo>>(new HttpRequestException("Students directory unavailable"));
    }
}

/// <summary>A subscriber read over a fixed set of already-resolved contacts — the batched
/// Students read the publish path and both audience reads share.</summary>
internal sealed class FixedSubscriberContactResolver(IReadOnlyList<SubscriberInfo> subscribers) : IContactResolver
{
    public Task<IReadOnlyList<SubscriberInfo>> ResolveSubscribersAsync(
        ResolveSubscribersRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(subscribers);
}
