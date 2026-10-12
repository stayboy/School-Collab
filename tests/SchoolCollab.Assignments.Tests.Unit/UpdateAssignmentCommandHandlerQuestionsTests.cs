using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.UpdateAssignmentCommand;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.DTOs;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.AssignmentPolicies;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Tests.Unit.Handlers;

/// <summary>
/// Phase B1 round ar-1: <c>UpdateAssignmentCommandHandler</c> now supports
/// draft-replacement semantics for questions and attachments (decision b) plus
/// the optional <c>AiPromptOverride</c>. Non-draft updates still reject (FR-252).
/// Mirrors the setup pattern of <c>CreateAssignmentCommandHandlerEntityCodeTests</c>.
/// </summary>
[TestClass]
public class UpdateAssignmentCommandHandlerQuestionsTests
{
    private static readonly Guid TestTenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static (AssignmentsDbContext db, HybridCache cache, ITenantProvider tenants) BuildScope(string name)
    {
        var services = new ServiceCollection();
        services.AddTenancy();
        services.AddDbContext<AssignmentsDbContext>(opts => opts.UseInMemoryDatabase(name));
        services.AddDistributedMemoryCache();
        services.AddHybridCache();
        var sp = services.BuildServiceProvider();

        var db = sp.GetRequiredService<AssignmentsDbContext>();
        db.Database.EnsureCreated();
        var tenants = sp.GetRequiredService<ITenantProvider>();
        ((TenantProvider)tenants).SetTenant(new TenantContext(TestTenant, "TestSchool", TenantType.School));
        return (db, sp.GetRequiredService<HybridCache>(), tenants);
    }

    private static UpdateAssignmentCommandHandler NewHandler(
        IAssignmentRepository repo, HybridCache cache, FakeAssignmentPolicyResolver? policyResolver = null)
    {
        var publisher = new Mock<IIntegrationEventPublisher>();
        return new UpdateAssignmentCommandHandler(
            repo,
            publisher.Object,
            cache,
            Options.Create(new AttachmentUploadOptions()),
            new AcceptAllActivityGroupLookup(),
            policyResolver ?? new FakeAssignmentPolicyResolver(),
            NullLogger<UpdateAssignmentCommandHandler>.Instance);
    }

    private static Assignment SeedDraft(AssignmentsDbContext db, ITenantProvider tenants)
    {
        var assignment = Assignment.Create(
            "Original", null, AssignmentType.Digital,
            GradingFormat.AutoGraded, TargetAudienceType.AllStudents,
            Guid.NewGuid(), null, null,
            createdByTeacherId: Guid.Empty,
            mandatoryReview: true,
            assignmentNumber: "ASGA01",
            aiPromptOverride: "initial override")
            .WithTenant(tenants);
        var q1 = assignment.AddQuestion("Old MC", QuestionType.MultipleChoice, 0);
        q1.AddOption("A", true);
        q1.AddOption("B", false);
        var q2 = assignment.AddQuestion("Old TF", QuestionType.TrueFalse, 1);
        q2.AddOption("True", true);
        q2.AddOption("False", false);
        assignment.AddAttachment("old.pdf", "application/pdf", 1, "tenants/x/old.pdf");
        db.Assignments.Add(assignment);
        db.SaveChanges();
        return assignment;
    }

    private static UpdateAssignmentCommand SampleUpdate(
        Guid id,
        string? aiPromptOverride = null,
        IReadOnlyList<NewQuestionDto>? questions = null,
        IReadOnlyList<NewAttachmentDto>? attachments = null,
        IReadOnlyList<NewContentModuleDto>? contentModules = null,
        IReadOnlyList<NewResourceDto>? resources = null,
        // WS-A3 (spec §3.3 + §7 Q4): pass/fail threshold + attempt cap.
        decimal? passScore = null,
        int? maxAttempts = null,
        // QR-5/§5.6: the assignment's OWN instruction blocks — null preserves (the child-collection
        // contract), a non-null list replaces.
        IReadOnlyList<NewInstructionDto>? instructionItems = null) =>
        new(
            Id: id,
            Title: "Updated",
            Description: null,
            AssignmentType: AssignmentType.Digital,
            GradingFormat: GradingFormat.AutoGraded,
            TargetAudienceType: TargetAudienceType.AllStudents,
            TopicId: Guid.NewGuid(),
            DueDate: null,
            MaxScore: 100m,
            MandatoryReview: true,
            AiPromptOverride: aiPromptOverride,
            Questions: questions,
            Attachments: attachments,
            ContentModules: contentModules,
            Resources: resources,
            PassScore: passScore,
            MaxAttempts: maxAttempts,
            // QR-5/§5.6.
            InstructionItems: instructionItems);

    // ── QR-5/§5.6: the instruction blocks' update contract ────────────────────────────

    private static NewInstructionDto InstructionText(string text, string? title = "Read this") => new(
        Kind: InstructionKindDto.Text, Text: text, Url: null,
        FileName: null, ContentType: null, FileSize: 0, StoragePath: null, Title: title);

    private static void SeedInstructionRows(Assignment assignment, Guid firstQuestionId)
    {
        assignment.SetInstructionItems(
        [
            (null, InstructionKind.Text, "Read the question twice.", null, null, null, 0, null, "Read first"),
            (firstQuestionId, InstructionKind.Url, null, "https://example.com/help", null, null, 0, null, null),
        ]);
    }

    [TestMethod]
    public async Task Update_NullInstructionItems_PreservesBothOwnersBlocks()
    {
        var (db, cache, tenants) = BuildScope("update-instructions-preserve");
        using var _db = db;
        var assignment = SeedDraft(db, tenants);
        var firstQuestionId = assignment.Questions[0].Id;
        SeedInstructionRows(assignment, firstQuestionId);
        db.SaveChanges();

        var repo = new CapturingAssignmentRepository { Loaded = assignment };
        var handler = NewHandler(repo, cache);

        // Scalar-only save: neither the questions nor the instruction items were supplied.
        await handler.HandleAsync(SampleUpdate(assignment.Id));

        repo.Updated.Should().NotBeNull("the handler must save the mutated aggregate");
        var updated = repo.Updated!;
        updated.InstructionsFor(null).Should().ContainSingle()
            .Which.Text.Should().Be("Read the question twice.",
                "null InstructionItems preserves the assignment's own blocks — the child-collection contract");
        updated.InstructionsFor(firstQuestionId).Should().ContainSingle(
            "an untouched question keeps its block too");
        updated.InstructionsFor(null).Single().Title.Should().Be("Read first",
            "D7: null InstructionItems is the PRESERVE path — the name must survive it, not be nulled");
    }

    [TestMethod]
    public async Task Update_SuppliedInstructionItems_ReplaceTheAssignmentRows_LeaveTheQuestionsAlone()
    {
        var (db, cache, tenants) = BuildScope("update-instructions-replace");
        using var _db = db;
        var assignment = SeedDraft(db, tenants);
        var firstQuestionId = assignment.Questions[0].Id;
        SeedInstructionRows(assignment, firstQuestionId);
        db.SaveChanges();

        var repo = new CapturingAssignmentRepository { Loaded = assignment };
        var handler = NewHandler(repo, cache);

        await handler.HandleAsync(SampleUpdate(assignment.Id,
            instructionItems: [InstructionText("New guidance.")]));

        repo.Updated.Should().NotBeNull("the handler must save the mutated aggregate");
        var updated = repo.Updated!;
        updated.InstructionsFor(null).Should().ContainSingle()
            .Which.Text.Should().Be("New guidance.", "a non-null list is a full replacement");
        updated.InstructionsFor(firstQuestionId).Should().ContainSingle(
            "questions were not supplied, so their blocks ride the replacement untouched");
        updated.InstructionsFor(null).Single().Title.Should().Be("Read this",
            "D7: the SUPPLIED branch carries the replacement's own name (InstructionText's default)");
    }

    [TestMethod]
    public async Task Update_QuestionsReplacedWithNullItems_ReMintsQuestionRows_AndKeepsAssignmentRowIds()
    {
        var (db, cache, tenants) = BuildScope("update-instructions-re-mint");
        using var _db = db;
        var assignment = SeedDraft(db, tenants);
        var firstQuestionId = assignment.Questions[0].Id;
        SeedInstructionRows(assignment, firstQuestionId);
        var assignmentRowId = assignment.InstructionsFor(null).Single().Id;
        db.SaveChanges();

        var repo = new CapturingAssignmentRepository { Loaded = assignment };
        var handler = NewHandler(repo, cache);

        var replacement = new NewQuestionDto(
            QuestionText: "New question?", QuestionType: QuestionTypeDto.ShortAnswer, DisplayOrder: 0,
            Options: null, ModelAnswer: "42",
            Instructions: [InstructionText("Show your working.")]);

        await handler.HandleAsync(SampleUpdate(assignment.Id, questions: [replacement]));

        repo.Updated.Should().NotBeNull("the handler must save the mutated aggregate");
        var updated = repo.Updated!;
        updated.InstructionsFor(null).Should().ContainSingle()
            .Which.Id.Should().Be(assignmentRowId,
                "the append path never re-mints the assignment's own rows — a question-only edit cannot churn their ids");
        updated.InstructionsFor(firstQuestionId).Should().BeEmpty(
            "the removed question's rows were purged with it (RemoveQuestion)");
        var newQuestion = updated.Questions.Should().ContainSingle().Subject;
        updated.InstructionsFor(newQuestion.Id).Should().ContainSingle()
            .Which.Text.Should().Be("Show your working.",
                "the re-minted question's rows are stamped with its fresh id");
    }

    /// <summary>Capturing fake repository. The EF Core InMemory provider has a known
    /// quirk where a Same-Context Load → Replace-Owned-Children → SaveChanges
    /// sequence on <c>OwnsMany</c> collections raises a phantom
    /// "entity does not exist in the store" error. We assert the handler's
    /// aggregate mutations directly from the captured instance — the handler's
    /// job is to mutate the in-memory aggregate correctly; EF persistence is
    /// already covered by the in-memory provider's add-path tests.</summary>
    private sealed class CapturingAssignmentRepository : IAssignmentRepository
    {
        public Assignment? Loaded { get; set; }
        public Assignment? Updated { get; private set; }
        public Func<Assignment, Task>? OnUpdate { get; set; }

        public Task<Assignment?> GetAsync(Guid id, CancellationToken ct = default)
        {
            return Task.FromResult(Loaded);
        }

        public Task AddAsync(Assignment assignment, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Assignment assignment, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<AssignmentSummary>> ListAsync(AssignmentStatus? s, CancellationToken ct = default)
            => Task.FromResult(new List<AssignmentSummary>());
        public void DetectChanges() { }

        public Task<List<AssignmentSweepCandidate>> ListScheduledForAutoPublishAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
            => Task.FromResult(new List<AssignmentSweepCandidate>());
        public Task<List<AssignmentSweepCandidate>> ListDueForArchiveAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
            => Task.FromResult(new List<AssignmentSweepCandidate>());

        public async Task UpdateAsync(Assignment assignment, CancellationToken ct = default)
        {
            Updated = assignment;
            if (OnUpdate is not null) await OnUpdate(assignment);
        }
    }

    [TestMethod]
    public async Task HandleAsync_Draft_ReplacesQuestionsAndAttachments()
    {
        // Build an in-memory store + tenant scope, seed the aggregate, then drive
        // the handler through a capturing fake so we can verify the in-memory
        // replacement without invoking the InMemory provider's broken owned-type
        // delete-on-save path. EF persistence of the new owned rows is exercised
        // by the Create-handler tests (the add path).
        var (db, _, tenants) = BuildScope("update-replace-seed");
        var seeded = SeedDraft(db, tenants);
        var seededId = seeded.Id;
        db.ChangeTracker.Clear();
        // Re-fetch the aggregate from the store so the capturing fake sees a
        // snapshot identical to what the real repository would return.
        var loaded = db.Assignments.Single(a => a.Id == seededId);
        db.ChangeTracker.Clear();

        var (_, cache, _) = BuildScope("update-replace");
        var repo = new CapturingAssignmentRepository { Loaded = loaded };
        var handler = NewHandler(repo, cache);

        var inboundQuestions = new[]
        {
            new NewQuestionDto(
                "Brand new short answer.",
                QuestionTypeDto.ShortAnswer, 0, null, ModelAnswer: "Glucose")
        };
        var inboundAttachments = new[]
        {
            new NewAttachmentDto("new.pdf", "application/pdf", 2048, "tenants/x/new.pdf")
        };

        await handler.HandleAsync(SampleUpdate(seededId,
            aiPromptOverride: "fresh override",
            questions: inboundQuestions,
            attachments: inboundAttachments));

        var mutated = repo.Updated;
        mutated.Should().NotBeNull("the handler must invoke UpdateAsync exactly once");
        mutated!.Title.Should().Be("Updated");
        mutated.AiPromptOverride.Should().Be("fresh override");

        mutated.Questions.Should().HaveCount(1, "the inbound question list fully replaces the prior two");
        mutated.Questions[0].QuestionText.Should().Be("Brand new short answer.");
        mutated.Questions[0].ModelAnswer.Should().Be("Glucose");
        mutated.Questions[0].DisplayOrder.Should().Be(0, "the handler re-indexes DisplayOrder 0..n by inbound list position");

        mutated.Attachments.Should().HaveCount(1, "the inbound attachment list fully replaces the prior one");
        mutated.Attachments[0].StoragePath.Should().Be("tenants/x/new.pdf");
    }

    [TestMethod]
    public async Task HandleAsync_NullQuestions_PreservesExistingChildren()
    {
        var (db, cache, tenants) = BuildScope("update-null-children");
        var original = SeedDraft(db, tenants);
        var handler = NewHandler(new AssignmentRepository(db), cache);

        await handler.HandleAsync(SampleUpdate(original.Id,
            aiPromptOverride: "still here",
            questions: null,
            attachments: null));

        db.ChangeTracker.Clear();
        var stored = db.Assignments.IgnoreQueryFilters().Single(a => a.Id == original.Id);
        stored.Questions.Should().HaveCount(2, "a null Questions collection leaves existing children untouched");
        stored.Attachments.Should().HaveCount(1);
        stored.AiPromptOverride.Should().Be("still here");
    }

    [TestMethod]
    public async Task HandleAsync_NonDraft_StillRejected()
    {
        var (db, cache, tenants) = BuildScope("update-published");
        var assignment = Assignment.Create(
            "Pub", null, AssignmentType.Digital,
            GradingFormat.AutoGraded, TargetAudienceType.AllStudents,
            Guid.NewGuid(), null, null,
            createdByTeacherId: Guid.Empty,
            mandatoryReview: true,
            assignmentNumber: "ASGA02")
            .WithTenant(tenants);
        assignment.Publish();
        db.Assignments.Add(assignment);
        db.SaveChanges();
        var handler = NewHandler(new AssignmentRepository(db), cache);

        var act = async () => await handler.HandleAsync(SampleUpdate(assignment.Id,
            aiPromptOverride: "x",
            questions: null,
            attachments: null));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Only draft or scheduled assignments can be updated*");
    }

    // ── D6 (round assignment-rules-policy-rework): the archive window is policy-resolved ──

    [TestMethod]
    public async Task HandleAsync_ResolvedPolicyArchiveWindow_IsWhatTheUpdateSnapshots()
    {
        var (db, _, tenants) = BuildScope("update-gracedays-seed");
        var seeded = SeedDraft(db, tenants);
        var seededId = seeded.Id;
        db.ChangeTracker.Clear();
        var loaded = db.Assignments.Single(a => a.Id == seededId);
        db.ChangeTracker.Clear();

        var (_, cache, _) = BuildScope("update-gracedays-handler");
        var repo = new CapturingAssignmentRepository { Loaded = loaded };
        var resolver = new FakeAssignmentPolicyResolver
        {
            Policy = new EffectiveAssignmentPolicyResolver().Resolve(
                new AssignmentPolicyFields { ArchiveGraceDays = 7 }, gradeOverride: null),
        };
        var handler = NewHandler(repo, cache, resolver);

        await handler.HandleAsync(SampleUpdate(seededId));

        var mutated = repo.Updated;
        mutated.Should().NotBeNull();
        mutated!.ArchiveGraceDays.Should().Be(7,
            "an update RE-snapshots the archive window from the currently resolved policy (D6)");
    }

    [TestMethod]
    public async Task HandleAsync_FromScheduled_Succeeds_GuardWidened()
    {
        var (db, cache, tenants) = BuildScope("update-scheduled");
        var assignment = Assignment.Create(
            "Sch", null, AssignmentType.Digital,
            GradingFormat.AutoGraded, TargetAudienceType.AllStudents,
            Guid.NewGuid(), null, null,
            createdByTeacherId: Guid.Empty,
            mandatoryReview: true,
            assignmentNumber: "ASGA03")
            .WithTenant(tenants);
        var future = DateTimeOffset.UtcNow.AddDays(7);
        assignment.Schedule(future, approvalRequired: false);
        db.Assignments.Add(assignment);
        db.SaveChanges();

        var repo = new CapturingAssignmentRepository { Loaded = assignment };
        var handler = NewHandler(repo, cache);

        await handler.HandleAsync(SampleUpdate(assignment.Id,
            aiPromptOverride: "edit-on-scheduled",
            questions: null,
            attachments: null));

        var mutated = repo.Updated;
        mutated.Should().NotBeNull("the update must succeed on a Scheduled assignment (WS-A2 / decision (a))");
        mutated!.AiPromptOverride.Should().Be("edit-on-scheduled");
        mutated.Status.Should().Be(AssignmentStatus.Scheduled,
            "the status stays Scheduled — update does not transition lifecycle");
    }

    // ── WS-A3 / spec §3.3 + §7 Q4: PassScore / MaxAttempts threading ──

    [TestMethod]
    public async Task HandleAsync_PassScoreAndMaxAttempts_ThreadedToUpdate()
    {
        var (db, _, tenants) = BuildScope("update-scoring-threaded");
        var seeded = SeedDraft(db, tenants);
        var seededId = seeded.Id;
        db.ChangeTracker.Clear();
        var loaded = db.Assignments.Single(a => a.Id == seededId);
        var repo = new CapturingAssignmentRepository { Loaded = loaded };
        var (_, cache, _) = BuildScope("update-scoring-threaded-cache");
        var handler = NewHandler(repo, cache);

        await handler.HandleAsync(SampleUpdate(seededId,
            questions: null,
            attachments: null,
            passScore: 70m,
            maxAttempts: 5));

        var mutated = repo.Updated;
        mutated.Should().NotBeNull();
        mutated!.PassScore.Should().Be(70m,
            "the PassScore must thread through to the updated aggregate (WS-A3 / spec §3.3)");
        mutated.MaxAttempts.Should().Be(5,
            "the MaxAttempts must thread through to the updated aggregate (WS-A3 / spec §7 Q4)");
    }
}
