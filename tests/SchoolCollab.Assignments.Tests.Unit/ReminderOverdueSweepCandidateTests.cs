using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.DTOs;
using SchoolCollab.Assignments.Core.Services.Delivery;
using SchoolCollab.Assignments.Worker.Services;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Core.Notifications;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// P1-1 discriminating tests for the ward-state gating in the reminder + overdue
/// candidate reads, plus P1-2's terminal-<c>Skipped</c> idempotency (t1–t3) and the
/// later-of publish / awaiting-signature cadence anchor (t4). The reads previously
/// filtered only on publish/archive status and returned <b>every</b> subscribed
/// recipient — completed (guardian-signed) and passing wards were reminded and
/// overdued for the assignment's lifetime. These tests pin the incompleteness gate.
/// </summary>
[TestClass]
public class ReminderOverdueSweepCandidateTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000002");

    private static readonly Guid WardUnsigned = Guid.Parse("00000000-0000-0000-0000-000000000010");
    private static readonly Guid WardIncomplete = Guid.Parse("00000000-0000-0000-0000-000000000011");
    private static readonly Guid WardSigned = Guid.Parse("00000000-0000-0000-0000-000000000012");
    private static readonly Guid WardPassing = Guid.Parse("00000000-0000-0000-0000-000000000013");

    private static (AssignmentsDbContext db, AssignmentRepository repo) Build(string database)
    {
        var services = new ServiceCollection();
        services.AddTenancy();
        services.AddDbContext<AssignmentsDbContext>(o => o.UseInMemoryDatabase(database));
        var provider = services.BuildServiceProvider();

        var db = provider.GetRequiredService<AssignmentsDbContext>();
        var tenants = (TenantProvider)provider.GetRequiredService<ITenantProvider>();
        tenants.SetTenant(new TenantContext(TenantA, "SchoolA", TenantType.School));
        db.Database.EnsureCreated();

        return (db, new AssignmentRepository(db));
    }

    private static Assignment PublishedAssignment(string title = "Math", DateTimeOffset? dueDate = null)
    {
        var assignment = Assignment.Create(title, null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, null, dueDate, null, TeacherId)
            .WithTenant(TenantA);
        assignment.Publish(approvalRequired: false);
        return assignment;
    }

    private static AssignmentRecipient GuardianOf(Guid assignmentId, Guid wardStudentId, Guid recipientId, Guid contactId) =>
        AssignmentRecipient.Create(
            TenantA, assignmentId, ContactOwnerType.Guardian, wardStudentId, wardStudentId,
            contactId, ContactChannel.Email, role: null,
            notifyOnBroadcast: true, subscriptionActive: true);

    private static AssignmentSubmission SubmissionFor(Guid assignmentId, Guid wardStudentId)
        => AssignmentSubmission.Create(TenantA, assignmentId, wardStudentId, null);

    private static AssignmentSubmissionVersion PassingVersion(
        Guid submissionId, Guid assignmentId, Guid wardStudentId)
        => AssignmentSubmissionVersion.Create(
            TenantA, submissionId, assignmentId, wardStudentId, versionNumber: 1,
            SubmissionSource.Student, null, DateTimeOffset.UtcNow,
            content: null, score: 95m, passed: true);

    // ── t1: reminder read gates on ward incompleteness / unsigned-state ───────────────

    [TestMethod]
    public async Task ReminderRead_ExcludesPassingAndSignedWards_IncludesUnsignedAndIncomplete()
    {
        var (db, repo) = Build(nameof(ReminderRead_ExcludesPassingAndSignedWards_IncludesUnsignedAndIncomplete));
        using var _db = db;

        var assignment = PublishedAssignment();
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();

        var rUnsigned = GuardianOf(assignment.Id, WardUnsigned, Guid.Parse("00000000-0000-0000-0000-000000000010"), Guid.Parse("00000000-0000-0000-0000-000000000011"));
        var rIncomplete = GuardianOf(assignment.Id, WardIncomplete, Guid.Parse("00000000-0000-0000-0000-000000000014"), Guid.Parse("00000000-0000-0000-0000-000000000015"));
        var rSigned = GuardianOf(assignment.Id, WardSigned, Guid.Parse("00000000-0000-0000-0000-000000000016"), Guid.Parse("00000000-0000-0000-0000-000000000017"));
        var rPassing = GuardianOf(assignment.Id, WardPassing, Guid.Parse("00000000-0000-0000-0000-000000000018"), Guid.Parse("00000000-0000-0000-0000-000000000019"));
        // P2-2 (re-verify): a null-ward recipient has no ward scope — never a reminder
        // target, even though it is subscribed and broadcast-enabled.
        var rNoWard = AssignmentRecipient.Create(
            TenantA, assignment.Id, ContactOwnerType.Guardian,
            Guid.Parse("00000000-0000-0000-0000-00000000001a"), wardStudentId: null,
            Guid.Parse("00000000-0000-0000-0000-00000000001b"), ContactChannel.Email,
            role: null, notifyOnBroadcast: true, subscriptionActive: true);
        db.AssignmentRecipients.AddRange(rUnsigned, rIncomplete, rSigned, rPassing, rNoWard);

        // unsigned ward → AwaitingSignature submission.
        var unsignedSub = SubmissionFor(assignment.Id, WardUnsigned);
        unsignedSub.MarkAwaitingSignature();

        // passing ward → current version passed.
        var passingSub = SubmissionFor(assignment.Id, WardPassing);
        passingSub.RecordSubmission(1, SubmissionSource.Student, null, DateTimeOffset.UtcNow);
        var passingVersion = PassingVersion(passingSub.Id, assignment.Id, WardPassing);

        // signed ward → guardian signed (terminal).
        var signedSub = SubmissionFor(assignment.Id, WardSigned);
        signedSub.MarkAwaitingSignature();
        signedSub.MarkSigned();

        db.AssignmentSubmissions.AddRange(unsignedSub, passingSub, signedSub);
        db.AssignmentSubmissionVersions.Add(passingVersion);
        await db.SaveChangesAsync();

        var candidates = await repo.ListReminderSweepCandidatesAsync();

        candidates.Select(c => c.RecipientId).Should().BeEquivalentTo(new[] { rUnsigned.Id, rIncomplete.Id },
            "only unsigned / incomplete wards are reminder candidates, never signed-off or passing");
        candidates.Single(c => c.RecipientId == rUnsigned.Id).AwaitingSignatureSince.Should().NotBeNull(
            "an unsigned ward carries its awaiting-signature moment so the sweeper applies the later-of anchor");
        candidates.Single(c => c.RecipientId == rIncomplete.Id).AwaitingSignatureSince.Should().BeNull(
            "a ward with no submission has no awaiting-signature anchor → the publish anchor applies");
    }

    // ── t2: overdue read gates on incomplete wards ────────────────────────────────────

    [TestMethod]
    public async Task OverdueRead_ExcludesCompleteWards_IncludesIncompleteWards()
    {
        var (db, repo) = Build(nameof(OverdueRead_ExcludesCompleteWards_IncludesIncompleteWards));
        using var _db = db;

        var due = DateHelpers.PastDueAssignment();
        var assignment = PublishedAssignment(dueDate: due);
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();

        var rIncomplete = GuardianOf(assignment.Id, WardIncomplete, Guid.Parse("00000000-0000-0000-0000-000000000020"), Guid.Parse("00000000-0000-0000-0000-000000000021"));
        var rSigned = GuardianOf(assignment.Id, WardSigned, Guid.Parse("00000000-0000-0000-0000-000000000022"), Guid.Parse("00000000-0000-0000-0000-000000000023"));
        var rPassing = GuardianOf(assignment.Id, WardPassing, Guid.Parse("00000000-0000-0000-0000-000000000024"), Guid.Parse("00000000-0000-0000-0000-000000000025"));
        db.AssignmentRecipients.AddRange(rIncomplete, rSigned, rPassing);

        var passingSub = SubmissionFor(assignment.Id, WardPassing);
        passingSub.RecordSubmission(1, SubmissionSource.Student, null, DateTimeOffset.UtcNow);
        var passingVersion = PassingVersion(passingSub.Id, assignment.Id, WardPassing);

        var signedSub = SubmissionFor(assignment.Id, WardSigned);
        signedSub.MarkAwaitingSignature();
        signedSub.MarkSigned();

        db.AssignmentSubmissions.AddRange(passingSub, signedSub);
        db.AssignmentSubmissionVersions.Add(passingVersion);
        await db.SaveChangesAsync();

        var candidates = await repo.ListOverdueSweepCandidatesAsync(DateTimeOffset.UtcNow);

        candidates.Select(c => c.RecipientId).Should().ContainSingle().Which.Should().Be(rIncomplete.Id,
            "a past-due assignment notifies only its incomplete wards — signed/passing wards are complete");
    }

    // ── t3: a skipped recipient is NOT re-skipped on a second sweep pass ─────────────

    [TestMethod]
    public async Task Skip_IsTerminal_SecondPassDoesNotInsertAnotherRow()
    {
        var (db, _) = Build(nameof(Skip_IsTerminal_SecondPassDoesNotInsertAnotherRow));
        using var _db = db;

        var tenantId = TenantA;
        var assignmentId = Guid.Parse("00000000-0000-0000-0000-000000000030");
        var recipientId = Guid.Parse("00000000-0000-0000-0000-000000000031");
        var contactId = Guid.Parse("00000000-0000-0000-0000-000000000032");

        // Pass 1 and pass 2 simulate two 15-min sweeps for an unresolvable recipient
        // (no deep-link token → the skip path). The key (tenant, assignment, recipient,
        // kind) must hold exactly one Skipped row, never a fresh row per pass.
        var queuer = new QueueArgs(db, assignmentId, recipientId, contactId);
        await queuer.RunAsync(tenantId);
        await queuer.RunAsync(tenantId);

        var skipped = await db.NotificationLogs.CountAsync(x =>
            x.TenantId == tenantId
            && x.AssignmentId == assignmentId
            && x.RecipientId == recipientId
            && x.Kind == NotificationKind.Reminder
            && x.DeliveryStatus == NotificationDeliveryStatus.Skipped);
        skipped.Should().Be(1,
            "a Skipped row is terminal coverage — a second sweep pass must reuse it, not double-insert");
    }

    // ── t4: later-of publish / awaiting-signature anchor drives IsReminderDue ─────────

    [TestMethod]
    public void LaterOfAnchor_WhenPublishedLater_SuppressesUntilPublishPlusInterval()
    {
        var now = new DateTimeOffset(2025, 1, 10, 12, 0, 0, TimeSpan.Zero);
        var published = now.AddHours(-12); // publish is the later anchor
        var awaitingSince = now.AddDays(-10); // awaiting-signature long before publish

        ReminderSweeper.IsReminderDue(now, published, awaitingSince, null, 0, 24, 3)
            .Should().BeFalse("the later-of publish/awaiting-signature anchor is publish, and its interval has not elapsed");
    }

    // ── t5 (P2-1, re-verify): completion nudges never fire for archived assignments ─────

    [TestMethod]
    public async Task CompletionRead_ExcludesArchivedAssignments()
    {
        var (db, repo) = Build(nameof(CompletionRead_ExcludesArchivedAssignments));
        using var _db = db;

        var assignment = PublishedAssignment(dueDate: DateHelpers.PastDueAssignment());
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();

        var rUnsigned = GuardianOf(assignment.Id, WardUnsigned, Guid.Parse("00000000-0000-0000-0000-000000000040"), Guid.Parse("00000000-0000-0000-0000-000000000041"));
        db.AssignmentRecipients.Add(rUnsigned);
        var unsignedSub = SubmissionFor(assignment.Id, WardUnsigned);
        unsignedSub.MarkAwaitingSignature();
        db.AssignmentSubmissions.Add(unsignedSub);
        await db.SaveChangesAsync();

        (await repo.ListCompletionSweepCandidatesAsync())
            .Should().ContainSingle("a published assignment with an awaiting-signature ward queues a completion nudge");

        assignment.Archive();
        await db.SaveChangesAsync();

        (await repo.ListCompletionSweepCandidatesAsync())
            .Should().BeEmpty("an archived assignment must never re-nudge a signer (P2-1, re-verify)");
    }

    private sealed class QueueArgs
    {
        private readonly AssignmentsDbContext _db;
        private readonly Guid _assignmentId;
        private readonly Guid _recipientId;
        private readonly Guid _contactId;

        public QueueArgs(AssignmentsDbContext db, Guid assignmentId, Guid recipientId, Guid contactId)
        {
            _db = db;
            _assignmentId = assignmentId;
            _recipientId = recipientId;
            _contactId = contactId;
        }

        public async Task RunAsync(Guid tenantId)
        {
            await SweepNotificationQueuer.QueueAsync(
                _db, new NoAddressResolver(), NullLogger.Instance, TimeProvider.System,
                tenantId, _assignmentId, "Math", _recipientId, _contactId,
                ContactOwnerType.Guardian, Guid.Empty, NotificationChannel.Email,
                deepLinkToken: null, deepLinkExpiresAt: null,
                NotificationKind.Reminder, CancellationToken.None);
        }
    }

    private sealed class NoAddressResolver : IContactAddressResolver
    {
        public Task<string?> ResolveAddressAsync(
            Guid contactId, ContactOwnerType ownerType, Guid ownerId, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);
    }
}

/// <summary>Small helper so the overdue fixture can carry a clearly-past due date.</summary>
internal static class DateHelpers
{
    public static DateTimeOffset PastDueAssignment() => DateTimeOffset.UtcNow.AddHours(-5);
}
