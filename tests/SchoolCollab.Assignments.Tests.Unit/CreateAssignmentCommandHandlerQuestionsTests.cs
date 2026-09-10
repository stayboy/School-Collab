using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.CreateAssignmentCommand;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
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

    private static CreateAssignmentCommandHandler NewHandler(AssignmentsDbContext db, HybridCache cache, ITenantProvider tenants)
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
            NullLogger<CreateAssignmentCommandHandler>.Instance);
    }

    private static CreateAssignmentCommand SampleCommand(
        string? aiPromptOverride = null,
        IReadOnlyList<NewQuestionDto>? questions = null,
        IReadOnlyList<NewAttachmentDto>? attachments = null) =>
        new(
            Title: "Algebra HW",
            Description: null,
            AssignmentType: AssignmentType.Digital,
            GradingFormat: GradingFormat.AutoGraded,
            TargetAudienceType: TargetAudienceType.AllStudents,
            TopicId: Guid.NewGuid(),
            GradeLevelId: null,
            DueDate: null,
            MaxScore: 100m,
            MandatoryReview: true,
            AiPromptOverride: aiPromptOverride,
            Questions: questions,
            Attachments: attachments);

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
    public async Task HandleAsync_TfCanonicalWithOneCorrect_Accepted()
    {
        var (db, cache, tenants) = BuildScope("create-tf-ok");
        using var _db = db;
        var handler = NewHandler(db, cache, tenants);

        var id = await handler.HandleAsync(SampleCommand(questions: [TrueFalseCorrect()]));

        var stored = db.Assignments.IgnoreQueryFilters().Single(a => a.Id == id);
        stored.Questions.Should().HaveCount(1);
        stored.Questions[0].Options.Should().HaveCount(2);
        stored.Questions[0].CorrectOptionId.Should().NotBeNull();
        stored.Questions[0].Options.Single(o => o.Id == stored.Questions[0].CorrectOptionId)
            .OptionText.Should().Be("True");
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
}
