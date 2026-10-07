using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.QuestionGeneration;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.RegenerateAttachmentExtraction;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.UpdateAssignmentCommand;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.AssignmentPolicies;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;
using Npgsql;

namespace SchoolCollab.Assignments.Tests.Integration;

/// <summary>
/// R3 (acceptance criteria 4, 5 and 8; plan-review P1-2/P1-4) — the generation header, the
/// generation id surviving a full-replacement save, the user-triggered re-extraction, and tenancy.
/// All on real Postgres, because every one of these is a claim about ROWS: how many were written, which
/// tenant can see them, and whether the owned-collection replacement carried a value into SQL.
/// <para>Nothing here exists against <c>0c8912da</c>: there is no header table, no <c>GenerationId</c>
/// column, no extraction columns and no re-extraction command.</para>
/// </summary>
[TestClass]
public sealed class QuestionGenerationPersistencePostgresTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid OtherTenantId = Guid.Parse("99999999-8888-7777-6666-555555555555");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000099");

    private static HybridCache NewCache()
    {
        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
        services.AddHybridCache();
        return services.BuildServiceProvider().GetRequiredService<HybridCache>();
    }

    private static ITenantProvider NewTenantProvider() => NewTenantProvider(TenantId);

    private static ITenantProvider NewTenantProvider(Guid tenantId)
    {
        var provider = new TenantProvider();
        ((TenantProvider)provider).SetTenant(new TenantContext(tenantId, "TestTenant", TenantType.School));
        return provider;
    }

    private static Assignment NewAssignment(ITenantProvider tenants) =>
        Assignment.Create(
                "Generation provenance", null, AssignmentType.Digital, GradingFormat.AutoGraded,
                TargetAudienceType.AllStudents, TopicId, null, null,
                createdByTeacherId: Guid.Empty)
            .WithTenant(tenants);

    private static RecordQuestionGenerationCommandHandler NewRecordHandler(AssignmentsDbContext db) =>
        new(db, new AssignmentQuestionGenerationRepository(db),
            NullLogger<RecordQuestionGenerationCommandHandler>.Instance);

    private static UpdateAssignmentCommandHandler NewUpdateHandler(
        AssignmentsDbContext db, HybridCache cache) =>
        new(new AssignmentRepository(db),
            new NullEventPublisher(),
            cache,
            Options.Create(new AttachmentUploadOptions()),
            new EmptyActivityGroupLookup(),
            new UnsetAssignmentPolicyResolver(),
            NullLogger<UpdateAssignmentCommandHandler>.Instance);

    /// <summary>Round <c>assignment-rules-policy-rework</c> D6/D10: the update path now snapshots the
    /// signature / guardian-review / archive-window terms from the resolved effective policy. This
    /// suite exercises the generation/attachment provenance round-trip, not policy resolution, so an
    /// all-unset policy (the built-in defaults) is the right double.</summary>
    private sealed class UnsetAssignmentPolicyResolver : IAssignmentPolicyResolver
    {
        public Task<EffectiveAssignmentPolicy> ResolveAsync(
            Guid? gradeLevelId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new EffectiveAssignmentPolicyResolver().Resolve(
                tenantDefault: null, gradeOverride: null));
    }

    private static RegenerateAttachmentExtractionCommandHandler NewRegenerateHandler(
        AssignmentsDbContext db, HybridCache cache, IFileStore fileStore, IAttachmentTextExtractor extractor) =>
        new(new AssignmentRepository(db), fileStore, extractor, cache,
            NullLogger<RegenerateAttachmentExtractionCommandHandler>.Instance);

    // ── Criterion 4: one header per generation, and the id survives the save ──

    [TestMethod]
    public async Task EveryGenerationWritesOneHeader_AndItsQuestionsCarryTheGenerationIdAfterASave()
    {
        var connectionString = await AssignmentsDbFactory.CreateMigratedDatabaseAsync(Guid.NewGuid().ToString("N"));
        var tenants = NewTenantProvider();
        var cache = NewCache();

        Guid assignmentId;
        await using (var db = AssignmentsDbFactory.CreateContext(connectionString, TenantId))
        {
            var assignment = NewAssignment(tenants);
            db.Assignments.Add(assignment);
            await db.SaveChangesAsync();
            assignmentId = assignment.Id;
        }

        Guid firstGenerationId;
        Guid secondGenerationId;
        await using (var db = AssignmentsDbFactory.CreateContext(connectionString, TenantId))
        {
            var handler = NewRecordHandler(db);
            firstGenerationId = await handler.HandleAsync(new RecordQuestionGenerationCommand(
                assignmentId, QuestionCount: 12,
                Types: [QuestionTypeDto.MultipleChoice, QuestionTypeDto.ShortAnswer],
                DifficultyEasyCount: 4, DifficultyMediumCount: 5, DifficultyHardCount: 3,
                Provider: "openrouter", Model: "google/gemma-4-31b-it"));
            // A second generation is a SECOND row — never an update of the first.
            secondGenerationId = await handler.HandleAsync(new RecordQuestionGenerationCommand(
                assignmentId, QuestionCount: 3, Types: null,
                DifficultyEasyCount: null, DifficultyMediumCount: null, DifficultyHardCount: null,
                Provider: "ollama", Model: "gemma4:31b-cloud"));
        }

        Guid firstQuestionId;
        await using (var db = AssignmentsDbFactory.CreateContext(connectionString, TenantId))
        {
            var command = new UpdateAssignmentCommand(
                assignmentId, "Generation provenance", null,
                AssignmentType.Digital, GradingFormat.AutoGraded, TargetAudienceType.AllStudents,
                TopicId, null, null, MandatoryReview: true,
                Questions:
                [
                    new NewQuestionDto("Generated from attachment A?", QuestionTypeDto.ShortAnswer, 0, null,
                        ModelAnswer: "yes", GenerationId: firstGenerationId),
                    new NewQuestionDto("Hand-written?", QuestionTypeDto.ShortAnswer, 1, null,
                        ModelAnswer: "no", GenerationId: null),
                ]);
            await NewUpdateHandler(db, cache).HandleAsync(command);
        }

        await using (var db = AssignmentsDbFactory.CreateContext(connectionString, TenantId))
        {
            var persisted = await db.Assignments.AsNoTracking().SingleAsync(a => a.Id == assignmentId);
            firstQuestionId = persisted.Questions.Single(q => q.QuestionText == "Generated from attachment A?").Id;
            // Re-save the SAME payload, exactly as a second edit-and-save would: the rows are re-minted
            // again and the provenance must ride along a second time.
            await NewUpdateHandler(db, cache).HandleAsync(new UpdateAssignmentCommand(
                assignmentId, "Generation provenance", null,
                AssignmentType.Digital, GradingFormat.AutoGraded, TargetAudienceType.AllStudents,
                TopicId, null, null, MandatoryReview: true,
                Questions:
                [
                    new NewQuestionDto("Generated from attachment A?", QuestionTypeDto.ShortAnswer, 0, null,
                        ModelAnswer: "yes", GenerationId: firstGenerationId),
                    new NewQuestionDto("Hand-written?", QuestionTypeDto.ShortAnswer, 1, null,
                        ModelAnswer: "no", GenerationId: null),
                ]));
        }

        await using var read = AssignmentsDbFactory.CreateContext(connectionString, TenantId);

        var headers = await read.AssignmentQuestionGenerations.AsNoTracking().ToListAsync();
        headers.Should().HaveCount(2, "one row per generation, appended");
        headers.Select(h => h.Id).Should().BeEquivalentTo([firstGenerationId, secondGenerationId]);
        var first = headers.Single(h => h.Id == firstGenerationId);
        first.AssignmentId.Should().Be(assignmentId);
        first.TenantId.Should().Be(TenantId);
        first.QuestionCount.Should().Be(12);
        first.Types.Should().Be("MultipleChoice,ShortAnswer");
        first.DifficultyEasyCount.Should().Be(4);
        first.DifficultyMediumCount.Should().Be(5);
        first.DifficultyHardCount.Should().Be(3);
        first.Provider.Should().Be("openrouter");
        first.Model.Should().Be("google/gemma-4-31b-it");

        var questions = (await read.Assignments.AsNoTracking().SingleAsync(a => a.Id == assignmentId)).Questions;
        questions.Should().HaveCount(2);
        questions.Single(q => q.QuestionText == "Generated from attachment A?")
            .GenerationId.Should().Be(
                firstGenerationId, "the id rode both re-mints; without it the first save would strip provenance");
        questions.Single(q => q.QuestionText == "Hand-written?")
            .GenerationId.Should().BeNull("a hand-written question belongs to no generation");
        questions.Select(q => q.Id).Should().NotContain(
            firstQuestionId, "sanity: the save really did re-mint the rows the id had to survive");
    }

    // ── Criterion 5: user-triggered re-extraction replaces, never duplicates ──

    [TestMethod]
    public async Task Regenerate_ReplacesTheStoredExtractionInPlace_AndIsTheOnlyWriter()
    {
        var connectionString = await AssignmentsDbFactory.CreateMigratedDatabaseAsync(Guid.NewGuid().ToString("N"));
        var tenants = NewTenantProvider();
        var cache = NewCache();

        Guid assignmentId;
        Guid attachmentId;
        await using (var db = AssignmentsDbFactory.CreateContext(connectionString, TenantId))
        {
            var assignment = NewAssignment(tenants);
            var attachment = assignment.AddAttachment(
                "syllabus.pdf", "application/pdf", 2048, "tenants/t/staging/syllabus.pdf",
                AttachmentExtractionStatus.Succeeded, "stale text from the first stage", DateTimeOffset.UtcNow);
            db.Assignments.Add(assignment);
            await db.SaveChangesAsync();
            assignmentId = assignment.Id;
            attachmentId = attachment.Id;
        }

        var fileStore = new InMemoryFileStore("tenants/t/staging/syllabus.pdf", "freshly read text");
        var extractor = new ScriptedExtractor(
            AttachmentExtractionResult.Succeeded("freshly read text", 17));

        await using (var db = AssignmentsDbFactory.CreateContext(connectionString, TenantId))
        {
            var updated = await NewRegenerateHandler(db, cache, fileStore, extractor).HandleAsync(
                new RegenerateAttachmentExtractionCommand(assignmentId, attachmentId));

            updated.Id.Should().Be(attachmentId);
            updated.ExtractionStatus.Should().Be(AttachmentExtractionStatusDto.Succeeded);
            updated.ExtractedText.Should().Be("freshly read text");
        }

        await using (var read = AssignmentsDbFactory.CreateContext(connectionString, TenantId))
        {
            var reloaded = await read.Assignments.AsNoTracking().SingleAsync(a => a.Id == assignmentId);
            reloaded.Attachments.Should().HaveCount(1, "re-extraction replaces the row's fields — it never appends");
            reloaded.Attachments.Single().ExtractedText.Should().Be("freshly read text");
        }

        // A second run converges on the same single row and transitions the status again.
        extractor.Next = AttachmentExtractionResult.Unsupported("'.pdf' with no text layer.");
        await using (var db = AssignmentsDbFactory.CreateContext(connectionString, TenantId))
        {
            await NewRegenerateHandler(db, cache, fileStore, extractor).HandleAsync(
                new RegenerateAttachmentExtractionCommand(assignmentId, attachmentId));
        }

        await using (var read = AssignmentsDbFactory.CreateContext(connectionString, TenantId))
        {
            var reloaded = await read.Assignments.AsNoTracking().SingleAsync(a => a.Id == assignmentId);
            reloaded.Attachments.Should().HaveCount(1);
            var row = reloaded.Attachments.Single();
            row.ExtractionStatus.Should().Be(AttachmentExtractionStatus.Unsupported);
            row.ExtractedText.Should().BeNull("the status transitioned, so the stale text is gone");
            row.ExtractionError.Should().Be("'.pdf' with no text layer.");
        }

        // …and a plain save (the ONLY other writer of the attachment row) must NOT re-extract: it writes
        // exactly the outcome the client handed back, leaving the blob untouched.
        var readsAfterRegenerate = fileStore.OpenReadCalls;
        await using (var db = AssignmentsDbFactory.CreateContext(connectionString, TenantId))
        {
            await NewUpdateHandler(db, cache).HandleAsync(new UpdateAssignmentCommand(
                assignmentId, "Generation provenance", null,
                AssignmentType.Digital, GradingFormat.AutoGraded, TargetAudienceType.AllStudents,
                TopicId, null, null, MandatoryReview: true,
                Attachments:
                [
                    new NewAttachmentDto(
                        "syllabus.pdf", "application/pdf", 2048, "tenants/t/staging/syllabus.pdf",
                        AttachmentExtractionStatusDto.Unsupported, null, null, "'.pdf' with no text layer."),
                ]));
        }

        fileStore.OpenReadCalls.Should().Be(
            readsAfterRegenerate, "a save re-writes the stored outcome, it does not re-read the file");
    }

    [TestMethod]
    public async Task Regenerate_WithASweptBlob_FailsOpenOnTheSameRow()
    {
        var connectionString = await AssignmentsDbFactory.CreateMigratedDatabaseAsync(Guid.NewGuid().ToString("N"));
        var tenants = NewTenantProvider();
        var cache = NewCache();

        Guid assignmentId;
        Guid attachmentId;
        await using (var db = AssignmentsDbFactory.CreateContext(connectionString, TenantId))
        {
            var assignment = NewAssignment(tenants);
            var attachment = assignment.AddAttachment(
                "gone.pdf", "application/pdf", 2048, "tenants/t/staging/gone.pdf",
                AttachmentExtractionStatus.Succeeded, "text from before the sweep", DateTimeOffset.UtcNow);
            db.Assignments.Add(assignment);
            await db.SaveChangesAsync();
            assignmentId = assignment.Id;
            attachmentId = attachment.Id;
        }

        var missing = new InMemoryFileStore("other/path.pdf", "irrelevant") { ThrowOnOpen = true };
        await using (var db = AssignmentsDbFactory.CreateContext(connectionString, TenantId))
        {
            var updated = await NewRegenerateHandler(db, cache, missing, new ScriptedExtractor(
                AttachmentExtractionResult.Succeeded("unused", 0))).HandleAsync(
                new RegenerateAttachmentExtractionCommand(assignmentId, attachmentId));

            updated.ExtractionStatus.Should().Be(AttachmentExtractionStatusDto.Failed);
        }

        await using var read = AssignmentsDbFactory.CreateContext(connectionString, TenantId);
        var row = (await read.Assignments.AsNoTracking().SingleAsync(a => a.Id == assignmentId)).Attachments.Single();
        row.Id.Should().Be(attachmentId, "the row is the same one, now carrying a Failed status");
        row.ExtractionStatus.Should().Be(AttachmentExtractionStatus.Failed);
        row.ExtractedText.Should().BeNull();
    }

    [TestMethod]
    public async Task Regenerate_UnknownAttachment_ThrowsNotFound()
    {
        var connectionString = await AssignmentsDbFactory.CreateMigratedDatabaseAsync(Guid.NewGuid().ToString("N"));
        var tenants = NewTenantProvider();
        var cache = NewCache();

        Guid assignmentId;
        await using (var db = AssignmentsDbFactory.CreateContext(connectionString, TenantId))
        {
            var assignment = NewAssignment(tenants);
            db.Assignments.Add(assignment);
            await db.SaveChangesAsync();
            assignmentId = assignment.Id;
        }

        await using var open = AssignmentsDbFactory.CreateContext(connectionString, TenantId);
        var act = async () => await NewRegenerateHandler(
                open, cache, new InMemoryFileStore("p", "t"), new ScriptedExtractor(
                    AttachmentExtractionResult.Succeeded("t", 1)))
            .HandleAsync(new RegenerateAttachmentExtractionCommand(assignmentId, Guid.NewGuid()));

        await act.Should().ThrowAsync<AttachmentNotFoundException>();
    }

    // ── Criterion 8: tenancy + the (TenantId, AssignmentId) index ──

    [TestMethod]
    public async Task GenerationHeader_CannotBeReadAcrossTenants()
    {
        var connectionString = await AssignmentsDbFactory.CreateMigratedDatabaseAsync(Guid.NewGuid().ToString("N"));
        var tenants = NewTenantProvider();

        Guid assignmentId;
        await using (var db = AssignmentsDbFactory.CreateContext(connectionString, TenantId))
        {
            var assignment = NewAssignment(tenants);
            db.Assignments.Add(assignment);
            await db.SaveChangesAsync();
            assignmentId = assignment.Id;
            await NewRecordHandler(db).HandleAsync(new RecordQuestionGenerationCommand(
                assignmentId, 5, null, null, null, null, "ollama", "gemma4:31b-cloud"));
        }

        await using var other = AssignmentsDbFactory.CreateContext(connectionString, OtherTenantId);
        (await other.AssignmentQuestionGenerations.CountAsync()).Should().Be(
            0, "another tenant's context must not see this header at all");

        await using var own = AssignmentsDbFactory.CreateContext(connectionString, TenantId);
        (await own.AssignmentQuestionGenerations.CountAsync()).Should().Be(1);
    }

    [TestMethod]
    public async Task GenerationHeader_CarriesTheTenantAssignmentIndex()
    {
        var connectionString = await AssignmentsDbFactory.CreateMigratedDatabaseAsync(Guid.NewGuid().ToString("N"));

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT indexdef FROM pg_indexes
            WHERE tablename = 'assignment_question_generations'
            """;
        var indexes = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                indexes.Add(reader.GetString(0));
            }
        }

        indexes.Should().Contain(
            i => i.Contains("ix_assignment_question_generations_tenant_assignment")
                && i.Contains("(tenant_id, assignment_id)"),
            "the module convention's (TenantId, AssignmentId) index must exist on the header table");
    }

    // ── Fakes ────────────────────────────────────────────────────────────

    private sealed class InMemoryFileStore(string path, string content) : IFileStore
    {
        private readonly byte[] _bytes = System.Text.Encoding.UTF8.GetBytes(content);

        public int OpenReadCalls { get; private set; }
        public bool ThrowOnOpen { get; init; }

        public Task<string> StoreAsync(Stream content, string fileName, string contentType,
            CancellationToken cancellationToken = default) => Task.FromResult(path);

        public Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default)
        {
            OpenReadCalls++;
            if (ThrowOnOpen) throw new FileNotFoundException("the staged blob was swept");
            return Task.FromResult<Stream>(new MemoryStream(_bytes));
        }

        public Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class ScriptedExtractor(AttachmentExtractionResult next) : IAttachmentTextExtractor
    {
        public AttachmentExtractionResult Next { get; set; } = next;

        public Task<AttachmentExtractionResult> ExtractAsync(
            Stream content, string fileName, long fileSize, CancellationToken cancellationToken = default) =>
            Task.FromResult(Next);
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
