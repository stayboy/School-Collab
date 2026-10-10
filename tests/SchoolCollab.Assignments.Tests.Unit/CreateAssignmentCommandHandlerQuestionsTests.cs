using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.CreateAssignmentCommand;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.AssignmentPolicies;
using SchoolCollab.Core.EntityCodes;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Tests.Unit.Handlers;

/// <summary>
/// Phase B1 round ar-1: <c>CreateAssignmentCommandHandler</c> now persists
/// questions, options, attachments and <c>AiPromptOverride</c> on the draft
/// (AI spec §3.3 / §2.6 FR-250/251/230/210). Validation (FR-252) must reject
/// malformed payloads BEFORE any child is added (EC-7), so a partial
/// aggregate can never be persisted. Mirrors the setup pattern of
/// <c>CreateAssignmentCommandHandlerEntityCodeTests</c>.
/// </summary>
[TestClass]
public class CreateAssignmentCommandHandlerQuestionsTests
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

    private static CreateAssignmentCommandHandler NewHandler(
        AssignmentsDbContext db, HybridCache cache, ITenantProvider tenants,
        FakeAssignmentPolicyResolver? policyResolver = null)
    {
        var generator = new Mock<IEntityCodeGenerator>();
        generator.Setup(g => g.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync("ASGA01");
        var publisher = new Mock<IIntegrationEventPublisher>();
        return new CreateAssignmentCommandHandler(
            new AssignmentRepository(db),
            generator.Object,
            publisher.Object,
            cache,
            tenants,
            Options.Create(new AttachmentUploadOptions()),
            new FakeCurrentUser(),
            new FakeTeacherDirectory(),
            new FakeFeatureFlagService { IsEnabledValue = true },
            new AcceptAllActivityGroupLookup(),
            policyResolver ?? new FakeAssignmentPolicyResolver(),
            NullLogger<CreateAssignmentCommandHandler>.Instance);
    }

    private static CreateAssignmentCommand SampleCommand(
        string? aiPromptOverride = null,
        IReadOnlyList<NewQuestionDto>? questions = null,
        IReadOnlyList<NewAttachmentDto>? attachments = null,
        IReadOnlyList<NewContentModuleDto>? contentModules = null,
        IReadOnlyList<NewResourceDto>? resources = null,
        // WS-A3 (spec §3.3 + §7 Q4): pass/fail threshold + attempt cap.
        decimal? passScore = null,
        int? maxAttempts = null,
        // WS-B2 (spec §3.4 line 70): optional per-difficulty counts.
        int? difficultyEasyCount = null,
        int? difficultyMediumCount = null,
        int? difficultyHardCount = null,
        // Q1(ii) (owner, 2026-10-09): the grading format decides whether the questions must define
        // response kinds — mandatory only where the format can carry them (Teacher Marked).
        GradingFormat? gradingFormat = null,
        // QR-5/§5.6: the assignment's OWN instruction blocks (the questions' ride their DTOs).
        IReadOnlyList<NewInstructionDto>? instructionItems = null) =>
        new(
            Title: "Algebra HW",
            Description: null,
            AssignmentType: AssignmentType.Digital,
            GradingFormat: gradingFormat ?? GradingFormat.AutoGraded,
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
            // WS-B2 (spec §3.4 line 70): threaded to Assignment.Create.
            DifficultyEasyCount: difficultyEasyCount,
            DifficultyMediumCount: difficultyMediumCount,
            DifficultyHardCount: difficultyHardCount,
            // QR-5/§5.6: the assignment's own instruction blocks.
            InstructionItems: instructionItems);

    private static NewQuestionDto McQuestion(int displayOrder) =>
        new(
            QuestionText: $"Pick the capital of France. [{displayOrder}]",
            QuestionType: QuestionTypeDto.MultipleChoice,
            DisplayOrder: displayOrder,
            Options:
            [
                new NewQuestionOptionDto("Berlin", false),
                new NewQuestionOptionDto("Paris", true),
                new NewQuestionOptionDto("Madrid", false),
                new NewQuestionOptionDto("Rome", false)
            ]);

    private static NewQuestionDto TrueFalseCorrect() =>
        new(
            QuestionText: "Photosynthesis requires sunlight.",
            QuestionType: QuestionTypeDto.TrueFalse,
            DisplayOrder: 0,
            Options:
            [
                new NewQuestionOptionDto("True", true),
                new NewQuestionOptionDto("False", false)
            ]);

    private static NewQuestionDto ShortAnswer() =>
        new(
            QuestionText: "Name the main product of photosynthesis.",
            QuestionType: QuestionTypeDto.ShortAnswer,
            DisplayOrder: 0,
            Options: null,
            ModelAnswer: "Glucose");

    [TestMethod]
    public async Task HandleAsync_PersistsQuestionsAndOptions_ReindexedByListPosition()
    {
        var (db, cache, tenants) = BuildScope("create-questions-reindex");
        using var _db = db;
        var handler = NewHandler(db, cache, tenants);

        // Inbound carries DisplayOrder 5, 99 — handler must re-index 0..n.
        var questions = new[]
        {
            McQuestion(5),
            McQuestion(99)
        };

        var id = await handler.HandleAsync(SampleCommand(questions: questions));

        var stored = db.Assignments.IgnoreQueryFilters().Single(a => a.Id == id);
        stored.Questions.Should().HaveCount(2);
        stored.Questions.Select(q => q.DisplayOrder)
            .Should().BeEquivalentTo(new[] { 0, 1 }, opts => opts.WithStrictOrdering(),
                "the handler re-indexes DisplayOrder 0..n by inbound list position (EC-7)");
        stored.Questions.SelectMany(q => q.Options)
            .Select(o => o.IsCorrect)
            .Where(b => b)
            .Should().HaveCount(2, "both questions must persist exactly one correct option");
    }

    [TestMethod]
    public async Task HandleAsync_McWithZeroCorrect_RejectedBeforeAnyChildAdded()
    {
        var (db, cache, tenants) = BuildScope("create-mc-no-correct");
        using var _db = db;
        var handler = NewHandler(db, cache, tenants);

        var questions = new[]
        {
            new NewQuestionDto(
                "Q?", QuestionTypeDto.MultipleChoice, 0,
                Options: [new NewQuestionOptionDto("A", false), new NewQuestionOptionDto("B", false)])
        };

        var act = async () => await handler.HandleAsync(SampleCommand(questions: questions));

        await act.Should().ThrowAsync<AssignmentQuestionValidationException>()
            .WithMessage("*exactly 1 correct option*");
        db.Assignments.IgnoreQueryFilters().Should().BeEmpty(
            "FR-252 must reject before any child is added — no partial aggregate");
    }

    [TestMethod]
    public async Task HandleAsync_McWithTwoCorrect_Rejected()
    {
        var (db, cache, tenants) = BuildScope("create-mc-two-correct");
        using var _db = db;
        var handler = NewHandler(db, cache, tenants);

        var questions = new[]
        {
            new NewQuestionDto(
                "Q?", QuestionTypeDto.MultipleChoice, 0,
                Options:
                [
                    new NewQuestionOptionDto("A", true),
                    new NewQuestionOptionDto("B", true),
                    new NewQuestionOptionDto("C", false)
                ])
        };

        var act = async () => await handler.HandleAsync(SampleCommand(questions: questions));

        await act.Should().ThrowAsync<AssignmentQuestionValidationException>()
            .WithMessage("*exactly 1 correct option*");
    }

    [TestMethod]
    public async Task HandleAsync_TfNonCanonical_Rejected()
    {
        var (db, cache, tenants) = BuildScope("create-tf-noncanonical");
        using var _db = db;
        var handler = NewHandler(db, cache, tenants);

        var questions = new[]
        {
            new NewQuestionDto(
                "Q?", QuestionTypeDto.TrueFalse, 0,
                Options:
                [
                    new NewQuestionOptionDto("Yes", true),
                    new NewQuestionOptionDto("No", false)
                ])
        };

        var act = async () => await handler.HandleAsync(SampleCommand(questions: questions));

        await act.Should().ThrowAsync<AssignmentQuestionValidationException>()
            .WithMessage("*'True' and 'False'*");
    }

    [TestMethod]
    public async Task HandleAsync_TeacherMarked_RequiresAResponseKind()
    {
        var (db, cache, tenants) = BuildScope("create-kinds-required");
        using var _db = db;
        var handler = NewHandler(db, cache, tenants);

        // McQuestion carries no kinds — legal on Auto Scored, rejected on Teacher Marked.
        var questions = new[] { McQuestion(0) };

        var act = async () => await handler.HandleAsync(SampleCommand(
            questions: questions,
            gradingFormat: GradingFormat.TeacherGraded));

        await act.Should().ThrowAsync<AssignmentQuestionValidationException>()
            .WithMessage("*at least one response kind*");
        db.Assignments.IgnoreQueryFilters().Should().BeEmpty(
            "the definition is mandatory where the format can carry kinds — nothing is persisted");
    }

    [TestMethod]
    public async Task HandleAsync_AutoScored_DoesNotRequireAResponseKind()
    {
        var (db, cache, tenants) = BuildScope("create-kinds-optional");
        using var _db = db;
        var handler = NewHandler(db, cache, tenants);

        // The normal auto-scored shape: the expected answer form is fixed by QuestionType, so no
        // response kind is required (and the media rule would reject every one of them anyway).
        var questions = new[] { McQuestion(0) };

        var id = await handler.HandleAsync(SampleCommand(
            questions: questions,
            gradingFormat: GradingFormat.AutoGraded));

        var stored = db.Assignments.IgnoreQueryFilters().Single(a => a.Id == id);
        stored.Questions.Should().ContainSingle()
            .Which.ResponseKinds.Should().BeEmpty(
                "requiring a kind here would be unsatisfiable — the media rule permits none on Auto Scored");
    }

    [TestMethod]
    public async Task HandleAsync_PersistsInstructionItems_ForBothOwners_AndReindexesPerOwner()
    {
        var (db, cache, tenants) = BuildScope("create-instructions-both-owners");
        using var _db = db;
        var handler = NewHandler(db, cache, tenants);

        // AutoGraded: questions carry no kinds (Q1(ii)) but may carry instruction blocks.
        var questions = new[]
        {
            new NewQuestionDto(
                QuestionText: "Explain photosynthesis.",
                QuestionType: QuestionTypeDto.ShortAnswer,
                DisplayOrder: 0,
                Options: null,
                ModelAnswer: "Glucose",
                Instructions:
                [
                    new NewInstructionDto(Kind: InstructionKindDto.Text, Text: "Answer in full sentences.", Url: null, FileName: null, ContentType: null, FileSize: 0, StoragePath: null, Title: "Answer in full sentences."),
                    new NewInstructionDto(Kind: InstructionKindDto.Url, Text: null, Url: "https://example.com/how", FileName: null, ContentType: null, FileSize: 0, StoragePath: null),
                ]),
        };

        var id = await handler.HandleAsync(SampleCommand(
            questions: questions,
            instructionItems:
            [
                new NewInstructionDto(Kind: InstructionKindDto.Text, Text: "Photograph your written work.", Url: null, FileName: null, ContentType: null, FileSize: 0, StoragePath: null, Title: "Photograph your written work."),
                new NewInstructionDto(Kind: InstructionKindDto.Audio, Text: null, Url: null, FileName: "how-to.mp3", ContentType: "audio/mpeg", FileSize: 2048, StoragePath: "tenants/t/staging/g/how-to.mp3"),
            ]));

        var stored = db.Assignments.IgnoreQueryFilters().Single(a => a.Id == id);

        var assignmentRows = stored.InstructionsFor(null);
        assignmentRows.Select(r => (r.Kind, r.DisplayOrder)).Should()
            .Equal([(InstructionKind.Text, 0), (InstructionKind.Audio, 1)],
                "the assignment's own rows are re-indexed 0..n by payload position (EC-7)");
        assignmentRows[1].StoragePath.Should().Be("tenants/t/staging/g/how-to.mp3");
        assignmentRows[0].Title.Should().Be("Photograph your written work.",
            "D7: the create path carries the material's NAME — a site that dropped it would persist null silently");
        assignmentRows[1].Title.Should().BeNull("an Audio material carries no name of its own");

        var question = stored.Questions.Should().ContainSingle().Subject;
        var questionRows = stored.InstructionsFor(question.Id);
        questionRows.Select(r => (r.Kind, r.DisplayOrder)).Should().Equal(
            (InstructionKind.Text, 0), (InstructionKind.Url, 1));
        questionRows.Should().OnlyContain(r => r.QuestionId == question.Id,
            "the question's rows are stamped with the id the aggregate minted");
        questionRows[0].Title.Should().Be("Answer in full sentences.",
            "D7: a question's Text row is titled by the same rule as the assignment's own");
        questionRows[1].Title.Should().BeNull("the Link supplied no label");
    }

    [TestMethod]
    public async Task HandleAsync_InvalidInstruction_RejectedBeforeAnyChildAdded()
    {
        var (db, cache, tenants) = BuildScope("create-instruction-invalid");
        using var _db = db;
        var handler = NewHandler(db, cache, tenants);

        var act = async () => await handler.HandleAsync(SampleCommand(
            instructionItems:
            [
                new NewInstructionDto(Kind: InstructionKindDto.Text, Text: "   ", Url: null, FileName: null, ContentType: null, FileSize: 0, StoragePath: null, Title: null),
            ]));

        await act.Should().ThrowAsync<AssignmentContentValidationException>()
            .WithMessage("*a text instruction needs its title*");
        db.Assignments.IgnoreQueryFilters().Should().BeEmpty(
            "an invalid block never leaves a partial aggregate behind (EC-7)");
    }

    [TestMethod]
    public async Task HandleAsync_WithDifficultyCounts_PersistsThem()
    {
        var (db, cache, tenants) = BuildScope("create-difficulty-counts");
        using var _db = db;
        var handler = NewHandler(db, cache, tenants);

        var id = await handler.HandleAsync(SampleCommand(
            difficultyEasyCount: 2, difficultyMediumCount: 3, difficultyHardCount: 1));

        var stored = db.Assignments.IgnoreQueryFilters().Single(a => a.Id == id);
        stored.DifficultyEasyCount.Should().Be(2);
        stored.DifficultyMediumCount.Should().Be(3);
        stored.DifficultyHardCount.Should().Be(1);
    }

    [TestMethod]
    public async Task HandleAsync_WithNegativeDifficultyCount_RejectedBeforePersist()
    {
        var (db, cache, tenants) = BuildScope("create-negative-difficulty");
        using var _db = db;
        var handler = NewHandler(db, cache, tenants);

        var act = async () => await handler.HandleAsync(SampleCommand(difficultyHardCount: -1));

        await act.Should().ThrowAsync<ArgumentException>(
            "the domain Create guard rejects negative difficulty counts");
        db.Assignments.IgnoreQueryFilters().Should().BeEmpty(
            "no partial aggregate may be persisted");
    }

    [TestMethod]
    public async Task HandleAsync_EmptyQuestionText_Rejected()
    {
        var (db, cache, tenants) = BuildScope("create-empty-text");
        using var _db = db;
        var handler = NewHandler(db, cache, tenants);

        var questions = new[]
        {
            new NewQuestionDto(
                "   ", QuestionTypeDto.MultipleChoice, 0,
                Options: [new NewQuestionOptionDto("A", true), new NewQuestionOptionDto("B", false)])
        };

        var act = async () => await handler.HandleAsync(SampleCommand(questions: questions));

        await act.Should().ThrowAsync<AssignmentQuestionValidationException>()
            .WithMessage("*QuestionText is required*");
    }

    [TestMethod]
    public async Task HandleAsync_ShortAnswer_PersistsModelAnswerWithoutOptions()
    {
        var (db, cache, tenants) = BuildScope("create-shortanswer");
        using var _db = db;
        var handler = NewHandler(db, cache, tenants);

        var id = await handler.HandleAsync(SampleCommand(questions: [ShortAnswer()]));

        var stored = db.Assignments.IgnoreQueryFilters().Single(a => a.Id == id);
        stored.Questions.Should().HaveCount(1);
        stored.Questions[0].QuestionType.Should().Be(QuestionType.ShortAnswer);
        stored.Questions[0].ModelAnswer.Should().Be("Glucose",
            "ShortAnswer must persist the inbound ModelAnswer for teacher reference");
        stored.Questions[0].Options.Should().BeEmpty();
    }

    [TestMethod]
    public async Task HandleAsync_Attachments_PersistedWithOpaqueStoragePath()
    {
        var (db, cache, tenants) = BuildScope("create-attachments");
        using var _db = db;
        var handler = NewHandler(db, cache, tenants);

        var attachments = new[]
        {
            new NewAttachmentDto("syllabus.pdf", "application/pdf", 2048, "tenants/abc/x/syllabus.pdf"),
            new NewAttachmentDto("intro.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 512, "tenants/abc/x/intro.docx")
        };

        var id = await handler.HandleAsync(SampleCommand(attachments: attachments));

        var stored = db.Assignments.IgnoreQueryFilters().Single(a => a.Id == id);
        stored.Attachments.Should().HaveCount(2);
        stored.Attachments.Select(a => a.StoragePath)
            .Should().BeEquivalentTo(attachments.Select(a => a.StoragePath),
                "StoragePath is opaque — must be stored as given");
        stored.Attachments.Should().AllSatisfy(a => a.AssignmentId.Should().Be(id));
    }

    [TestMethod]
    public async Task HandleAsync_AiPromptOverride_Persisted()
    {
        var (db, cache, tenants) = BuildScope("create-aiprompt");
        using var _db = db;
        var handler = NewHandler(db, cache, tenants);

        var id = await handler.HandleAsync(SampleCommand(aiPromptOverride: "Make questions for grade 5 only."));

        var stored = db.Assignments.IgnoreQueryFilters().Single(a => a.Id == id);
        stored.AiPromptOverride.Should().Be("Make questions for grade 5 only.",
            "the per-assignment AI prompt override must round-trip through the create command (FR-230)");
    }

    [TestMethod]
    public async Task HandleAsync_NullQuestions_BehavesAsToday()
    {
        var (db, cache, tenants) = BuildScope("create-null-questions");
        using var _db = db;
        var handler = NewHandler(db, cache, tenants);

        var id = await handler.HandleAsync(SampleCommand(questions: null, attachments: null));

        var stored = db.Assignments.IgnoreQueryFilters().Single(a => a.Id == id);
        stored.Questions.Should().BeEmpty(
            "a null Questions collection is the pre-feature contract — must not error or synthesise rows");
        stored.Attachments.Should().BeEmpty();
    }

    // ── D6/D10 (round assignment-rules-policy-rework): the archive window is policy-resolved ──

    [TestMethod]
    public async Task HandleAsync_UnsetPolicyArchiveWindow_KeepsTheBuiltInRetentionFloor()
    {
        var (db, cache, tenants) = BuildScope("create-gracedays-default");
        using var _db = db;
        var handler = NewHandler(db, cache, tenants);

        var id = await handler.HandleAsync(SampleCommand());

        var stored = db.Assignments.IgnoreQueryFilters().Single(a => a.Id == id);
        stored.ArchiveGraceDays.Should().Be(30,
            "an unset policy leaves the built-in 30-day retention floor (D6/OD2) — the request has no say");
    }

    [TestMethod]
    public async Task HandleAsync_ResolvedPolicyArchiveWindow_IsWhatGetsPersisted()
    {
        var (db, cache, tenants) = BuildScope("create-gracedays-policy");
        using var _db = db;
        var resolver = new FakeAssignmentPolicyResolver
        {
            Policy = new EffectiveAssignmentPolicyResolver().Resolve(
                new AssignmentPolicyFields { ArchiveGraceDays = 7 }, gradeOverride: null),
        };
        var handler = NewHandler(db, cache, tenants, resolver);

        var id = await handler.HandleAsync(SampleCommand());

        var stored = db.Assignments.IgnoreQueryFilters().Single(a => a.Id == id);
        stored.ArchiveGraceDays.Should().Be(7,
            "the resolved policy — not the author — decides the archive window (D6)");
        resolver.RequestedGradeLevelIds.Should().ContainSingle(
            "the create path resolves the policy for its derived policy-scope grade (no grade target ⇒ null)");
    }

    // ── WS-A3 / spec §3.3 + §7 Q4: PassScore / MaxAttempts threading ──

    [TestMethod]
    public async Task HandleAsync_PassScoreAndMaxAttempts_Persisted()
    {
        var (db, cache, tenants) = BuildScope("create-scoring-persisted");
        using var _db = db;
        var handler = NewHandler(db, cache, tenants);

        var id = await handler.HandleAsync(SampleCommand(passScore: 80m, maxAttempts: 3));

        var stored = db.Assignments.IgnoreQueryFilters().Single(a => a.Id == id);
        stored.PassScore.Should().Be(80m,
            "the PassScore must thread through to the created aggregate (WS-A3 / spec §3.3)");
        stored.MaxAttempts.Should().Be(3,
            "the MaxAttempts must thread through to the created aggregate (WS-A3 / spec §7 Q4)");
    }

    [TestMethod]
    public async Task HandleAsync_PassScoreAndMaxAttempts_DefaultsToNull()
    {
        var (db, cache, tenants) = BuildScope("create-scoring-default");
        using var _db = db;
        var handler = NewHandler(db, cache, tenants);

        var id = await handler.HandleAsync(SampleCommand());

        var stored = db.Assignments.IgnoreQueryFilters().Single(a => a.Id == id);
        stored.PassScore.Should().BeNull();
        stored.MaxAttempts.Should().BeNull();
    }
}
