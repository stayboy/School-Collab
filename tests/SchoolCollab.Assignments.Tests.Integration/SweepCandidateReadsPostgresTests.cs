using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Assignments.Tests.Integration;

/// <summary>
/// P2-3 (re-verify): the three E3 candidate reads translate and execute on REAL
/// Postgres/Npgsql. The in-memory provider evaluates the whole expression tree
/// locally — which is exactly how an untranslatable helper call survived the unit
/// suite during the P1-1 rework until the parent caught it. Seeding mirrors the
/// unit suite's t1/t2 shapes (incomplete = no submission, unsigned =
/// awaiting-signature, signed, passing, and a null-ward recipient), so this test
/// proves BOTH the Npgsql translation of the inlined ward-completeness predicates,
/// the <c>Distinct()</c> projection and the correlated <c>FirstOrDefault()</c>
/// anchor projection, AND the ward-state semantics.
/// </summary>
[TestClass]
public sealed class SweepCandidateReadsPostgresTests
{
    private static readonly Guid TenantId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000002");

    private static readonly Guid WardUnsigned = Guid.Parse("00000000-0000-0000-0000-000000000010");
    private static readonly Guid WardIncomplete = Guid.Parse("00000000-0000-0000-0000-000000000011");
    private static readonly Guid WardSigned = Guid.Parse("00000000-0000-0000-0000-000000000012");
    private static readonly Guid WardPassing = Guid.Parse("00000000-0000-0000-0000-000000000013");

    /// <summary>One past-due published assignment with the full ward-state matrix;
    /// asserts all three reads in one seeded database (each test gets its own
    /// ephemeral container database via <see cref="AssignmentsDbFactory"/>).</summary>
    [TestMethod]
    public async Task CandidateReads_TranslateAndGate_OnRealPostgres()
    {
        var connectionString = await AssignmentsDbFactory.CreateMigratedDatabaseAsync(Guid.NewGuid().ToString("N"));

        Guid rUnsignedId, rIncompleteId;
        await using (var seed = AssignmentsDbFactory.CreateContext(connectionString, tenantId: TenantId))
        {
            var assignment = Assignment.Create(
                "Math — Postgres translation", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
                TargetAudienceType.AllStudents, TopicId, null, DateTimeOffset.UtcNow.AddHours(-5), null, TeacherId)
                .WithTenant(TenantId);
            assignment.Publish(approvalRequired: false);
            seed.Assignments.Add(assignment);
            await seed.SaveChangesAsync();

            var rUnsigned = Recipient(assignment.Id, WardUnsigned, 0x10, 0x11);
            var rIncomplete = Recipient(assignment.Id, WardIncomplete, 0x14, 0x15);
            seed.AssignmentRecipients.AddRange(
                rUnsigned,
                rIncomplete,
                Recipient(assignment.Id, WardSigned, 0x16, 0x17),
                Recipient(assignment.Id, WardPassing, 0x18, 0x19),
                // P2-2: null-ward recipient — subscribed + broadcast-enabled, no ward scope.
                AssignmentRecipient.Create(
                    TenantId, assignment.Id, ContactOwnerType.Guardian,
                    Guid.Parse("00000000-0000-0000-0000-00000000001a"), wardStudentId: null,
                    Guid.Parse("00000000-0000-0000-0000-00000000001b"), ContactChannel.Email,
                    role: null, notifyOnBroadcast: true, subscriptionActive: true));
            rUnsignedId = rUnsigned.Id;
            rIncompleteId = rIncomplete.Id;

            var unsignedSub = SubmissionFor(assignment.Id, WardUnsigned);
            unsignedSub.MarkAwaitingSignature();

            var passingSub = SubmissionFor(assignment.Id, WardPassing);
            passingSub.RecordSubmission(1, SubmissionSource.Student, null, DateTimeOffset.UtcNow);

            var signedSub = SubmissionFor(assignment.Id, WardSigned);
            signedSub.MarkAwaitingSignature();
            signedSub.MarkSigned();

            seed.AssignmentSubmissions.AddRange(unsignedSub, passingSub, signedSub);
            seed.AssignmentSubmissionVersions.Add(AssignmentSubmissionVersion.Create(
                TenantId, passingSub.Id, assignment.Id, WardPassing, versionNumber: 1,
                SubmissionSource.Student, null, DateTimeOffset.UtcNow,
                content: null, score: 95m, passed: true));
            await seed.SaveChangesAsync();
        }

        await using var read = AssignmentsDbFactory.CreateContext(connectionString, tenantId: TenantId);
        var repo = new AssignmentRepository(read);

        // Reminder read: unsigned + incomplete wards only (never signed/passing/null-ward).
        (await repo.ListReminderSweepCandidatesAsync())
            .Select(c => c.RecipientId).Should().BeEquivalentTo(new[] { rUnsignedId, rIncompleteId },
            "on real Postgres the reminder read keeps only the unsigned and incomplete wards");
        (await repo.ListReminderSweepCandidatesAsync())
            .Single(c => c.RecipientId == rUnsignedId).AwaitingSignatureSince.Should().NotBeNull(
                "the correlated awaiting-signature anchor projection translates on Npgsql");

        // Overdue read (assignment is past due): incomplete wards — unsigned has no
        // passing version either, so both unsigned + incomplete are overdue targets.
        (await repo.ListOverdueSweepCandidatesAsync(DateTimeOffset.UtcNow))
            .Select(c => c.RecipientId).Should().BeEquivalentTo(new[] { rUnsignedId, rIncompleteId },
            "the overdue read gates the same ward-completeness predicate on real Postgres");

        // Completion read: awaiting-signature wards of published assignments only.
        (await repo.ListCompletionSweepCandidatesAsync())
            .Select(c => c.RecipientId).Should().ContainSingle().Which.Should().Be(rUnsignedId,
                "only the unsigned ward's guardian gets the completion nudge");
    }

    private static AssignmentRecipient Recipient(Guid assignmentId, Guid ward, int recipientTail, int contactTail) =>
        AssignmentRecipient.Create(
            TenantId, assignmentId, ContactOwnerType.Guardian, ward, ward,
            Guid.Parse($"00000000-0000-0000-0000-{contactTail:x12}"), ContactChannel.Email,
            role: null, notifyOnBroadcast: true, subscriptionActive: true);

    private static AssignmentSubmission SubmissionFor(Guid assignmentId, Guid wardStudentId) =>
        AssignmentSubmission.Create(TenantId, assignmentId, wardStudentId, null);
}
