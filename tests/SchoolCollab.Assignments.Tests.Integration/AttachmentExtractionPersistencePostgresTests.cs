using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.UpdateAssignmentCommand;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.AssignmentPolicies;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Tests.Integration;

/// <summary>
/// R3 (acceptance criterion 3; plan-review P1-3) — the attachment extraction outcome survives the
/// <b>full-replacement</b> save, on real Postgres.
/// <para>The trap this closes is specific: the update handler re-mints every attachment row
/// (<c>RemoveAttachment</c> → <c>AddAttachment</c>), so the row the author loaded is gone and a value
/// that did not ride <see cref="NewAttachmentDto"/> is <b>silently wiped</b>. The test therefore asserts
/// on the row id CHANGING while the extraction columns do not — a test that only checked "the value is
/// present" would pass against a partial fix that happened to keep the same row.</para>
/// <para>The in-memory EF provider cannot prove this: it does not enforce the column caps and, unlike
/// the Npgsql provider, it can mask an owned-collection replacement that never reaches SQL.</para>
/// </summary>
[TestClass]
public sealed class AttachmentExtractionPersistencePostgresTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000099");
    private static readonly DateTimeOffset ExtractedAt = new(2026, 10, 3, 9, 30, 0, TimeSpan.Zero);

    private static HybridCache NewCache()
    {
        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
        services.AddHybridCache();
        return services.BuildServiceProvider().GetRequiredService<HybridCache>();
    }

    private static ITenantProvider NewTenantProvider()
    {
        var provider = new TenantProvider();
        ((TenantProvider)provider).SetTenant(new TenantContext(TenantId, "TestTenant", TenantType.School));
        return provider;
    }

    private static UpdateAssignmentCommandHandler NewUpdateHandler(AssignmentsDbContext db, HybridCache cache) =>
        new(new AssignmentRepository(db),
            new NullEventPublisher(),
            cache,
            Options.Create(new AttachmentUploadOptions()),
            new EmptyActivityGroupLookup(),
            new UnsetAssignmentPolicyResolver(),
            NullLogger<UpdateAssignmentCommandHandler>.Instance);

    /// <summary>Round <c>assignment-rules-policy-rework</c> D6/D10: the update path now snapshots the
    /// signature / guardian-review / archive-window terms from the resolved effective policy. This
    /// suite exercises the attachment round-trip, not policy resolution, so an all-unset policy (the
    /// built-in defaults) is the right double.</summary>
    private sealed class UnsetAssignmentPolicyResolver : IAssignmentPolicyResolver
    {
        public Task<EffectiveAssignmentPolicy> ResolveAsync(
            Guid? gradeLevelId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new EffectiveAssignmentPolicyResolver().Resolve(
                tenantDefault: null, gradeOverride: null));
    }

    private static Assignment NewAssignment(ITenantProvider tenants) =>
        Assignment.Create(
                "Attachment round-trip", null, AssignmentType.Digital, GradingFormat.AutoGraded,
                TargetAudienceType.AllStudents, TopicId, null, null,
                createdByTeacherId: Guid.Empty)
            .WithTenant(tenants);

    /// <summary>The update payload the Edit surface would send for one attachment, carrying exactly the
    /// outcome the browser was handed by the stage response.</summary>
    private static UpdateAssignmentCommand CommandWithAttachment(
        Guid assignmentId,
        Guid originalAttachmentId,
        string storagePath,
        AttachmentExtractionStatusDto status,
        string? text,
        string? error = null) =>
        new(assignmentId, "Attachment round-trip", null,
            AssignmentType.Digital, GradingFormat.AutoGraded, TargetAudienceType.AllStudents,
            TopicId, null, null, MandatoryReview: true,
            Attachments:
            [
                new NewAttachmentDto(
                    "syllabus.pdf", "application/pdf", 2048, storagePath,
                    status, text, ExtractedAt, error),
            ]);

    [TestMethod]
    public async Task SucceededExtraction_SurvivesAReMintingSave()
    {
        var connectionString = await AssignmentsDbFactory.CreateMigratedDatabaseAsync(Guid.NewGuid().ToString("N"));
        var tenants = NewTenantProvider();
        var cache = NewCache();

        Guid assignmentId;
        Guid originalAttachmentId;
        await using (var db = AssignmentsDbFactory.CreateContext(connectionString, TenantId))
        {
            var assignment = NewAssignment(tenants);
            var attachment = assignment.AddAttachment(
                "syllabus.pdf", "application/pdf", 2048, "tenants/t/staging/syllabus.pdf");
            db.Assignments.Add(assignment);
            await db.SaveChangesAsync();
            assignmentId = assignment.Id;
            originalAttachmentId = attachment.Id;
        }

        await using (var db = AssignmentsDbFactory.CreateContext(connectionString, TenantId))
        {
            await NewUpdateHandler(db, cache).HandleAsync(CommandWithAttachment(
                assignmentId, originalAttachmentId, "tenants/t/staging/syllabus.pdf",
                AttachmentExtractionStatusDto.Succeeded, "photosynthesis converts light into sugar"));
        }

        await using var read = AssignmentsDbFactory.CreateContext(connectionString, TenantId);
        var reloaded = await read.Assignments.AsNoTracking().SingleAsync(a => a.Id == assignmentId);
        var attachmentRow = reloaded.Attachments.Single();

        attachmentRow.Id.Should().NotBe(
            originalAttachmentId, "the save really did re-mint the row — this is the trap being closed");
        attachmentRow.ExtractionStatus.Should().Be(AttachmentExtractionStatus.Succeeded);
        attachmentRow.ExtractedText.Should().Be("photosynthesis converts light into sugar");
        attachmentRow.ExtractedAt.Should().Be(ExtractedAt);
        attachmentRow.ExtractionError.Should().BeNull();
    }

    [TestMethod]
    public async Task FailedExtraction_SurvivesAReMintingSave_WithItsReasonAndNoText()
    {
        var connectionString = await AssignmentsDbFactory.CreateMigratedDatabaseAsync(Guid.NewGuid().ToString("N"));
        var tenants = NewTenantProvider();
        var cache = NewCache();

        Guid assignmentId;
        await using (var db = AssignmentsDbFactory.CreateContext(connectionString, TenantId))
        {
            var assignment = NewAssignment(tenants);
            assignment.AddAttachment("broken.pdf", "application/pdf", 2048, "tenants/t/staging/broken.pdf");
            db.Assignments.Add(assignment);
            await db.SaveChangesAsync();
            assignmentId = assignment.Id;
        }

        await using (var db = AssignmentsDbFactory.CreateContext(connectionString, TenantId))
        {
            await NewUpdateHandler(db, cache).HandleAsync(CommandWithAttachment(
                assignmentId, Guid.NewGuid(), "tenants/t/staging/broken.pdf",
                AttachmentExtractionStatusDto.Failed, text: "text that must not survive",
                error: "The file does not look like a valid PDF document."));
        }

        await using var read = AssignmentsDbFactory.CreateContext(connectionString, TenantId);
        var attachmentRow = (await read.Assignments.AsNoTracking().SingleAsync(a => a.Id == assignmentId))
            .Attachments.Single();

        attachmentRow.ExtractionStatus.Should().Be(AttachmentExtractionStatus.Failed);
        attachmentRow.ExtractedText.Should().BeNull("a non-succeeded row must not carry text");
        attachmentRow.ExtractionError.Should().Be("The file does not look like a valid PDF document.");
    }

    [TestMethod]
    public async Task PreR3Attachment_ReadsBackAsNotAttempted()
    {
        // The additive migration's default: a row written before R3 (or by a save that carried no
        // extraction outcome at all) must not read as Succeeded-with-nothing.
        var connectionString = await AssignmentsDbFactory.CreateMigratedDatabaseAsync(Guid.NewGuid().ToString("N"));
        var tenants = NewTenantProvider();
        var cache = NewCache();

        Guid assignmentId;
        await using (var db = AssignmentsDbFactory.CreateContext(connectionString, TenantId))
        {
            var assignment = NewAssignment(tenants);
            assignment.AddAttachment("old.pdf", "application/pdf", 2048, "tenants/t/staging/old.pdf");
            db.Assignments.Add(assignment);
            await db.SaveChangesAsync();
            assignmentId = assignment.Id;
        }

        await using (var db = AssignmentsDbFactory.CreateContext(connectionString, TenantId))
        {
            // A save whose payload omits the extraction fields entirely — the pre-R3 client shape.
            var command = new UpdateAssignmentCommand(
                assignmentId, "Attachment round-trip", null,
                AssignmentType.Digital, GradingFormat.AutoGraded, TargetAudienceType.AllStudents,
                TopicId, null, null, MandatoryReview: true,
                Attachments: [new NewAttachmentDto("old.pdf", "application/pdf", 2048, "tenants/t/staging/old.pdf")]);
            await NewUpdateHandler(db, cache).HandleAsync(command);
        }

        await using var read = AssignmentsDbFactory.CreateContext(connectionString, TenantId);
        var attachmentRow = (await read.Assignments.AsNoTracking().SingleAsync(a => a.Id == assignmentId))
            .Attachments.Single();

        attachmentRow.ExtractionStatus.Should().Be(AttachmentExtractionStatus.NotAttempted);
        attachmentRow.ExtractedText.Should().BeNull();
        attachmentRow.ExtractedAt.Should().BeNull();
        attachmentRow.ExtractionError.Should().BeNull();
    }

    private sealed class NullEventPublisher : IIntegrationEventPublisher
    {
        public Task EnqueueAsync<T>(T message, CancellationToken cancellationToken = default) where T : class =>
            Task.CompletedTask;

        public Task EnqueueAsync<T>(T message, Guid? tenantStamp, CancellationToken cancellationToken = default)
            where T : class => Task.CompletedTask;
    }

    private sealed class EmptyActivityGroupLookup : IActivityGroupLookup
    {
        public Task<Core.DTOs.ActivityGroupRefDto[]> GetByIdsAsync(
            IReadOnlyList<Guid> activityGroupIds, CancellationToken cancellationToken = default) =>
            Task.FromResult(Array.Empty<Core.DTOs.ActivityGroupRefDto>());

        public Task<Guid[]> GetActiveMemberIdsAsync(
            IReadOnlyList<Guid> activityGroupIds, CancellationToken cancellationToken = default) =>
            Task.FromResult(Array.Empty<Guid>());
    }
}
