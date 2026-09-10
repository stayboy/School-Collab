using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SchoolCollab.Assignments.Contracts.Events;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.DuplicateAssignmentCommand;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Core.EntityCodes;
using SchoolCollab.Core.Messaging;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Tests.Unit.Handlers;

/// <summary>
/// Round ar-7-template (WS-A4 / spec §3.1): <c>DuplicateAssignmentCommand</c>
/// clones any existing assignment — Draft, Published, Scheduled, Closed or
/// Archived — as a fresh Draft template copy. The copy carries the source's
/// scalar fields, questions/options (with re-pointed <c>CorrectOptionId</c>),
/// attachments, modules and resources; it never carries reviews, recipients,
/// gates, submissions, activity-group links, approval stamps or publish stamps.
/// </summary>
[TestClass]
public class DuplicateAssignmentCommandHandlerTests
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

    private static DuplicateAssignmentCommandHandler NewHandler(
        AssignmentsDbContext db,
        HybridCache cache,
        ITenantProvider tenants,
        Mock<IEntityCodeGenerator>? generator = null,
        Mock<IIntegrationEventPublisher>? publisher = null)
    {
        if (generator is null)
        {
            generator = new Mock<IEntityCodeGenerator>();
            generator.Setup(g => g.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync("DUP999");
        }

        publisher ??= new Mock<IIntegrationEventPublisher>();
        return new DuplicateAssignmentCommandHandler(
            new AssignmentRepository(db),
            generator.Object,
            publisher.Object,
            cache,
            tenants,
            NullLogger<DuplicateAssignmentCommandHandler>.Instance);
    }

    private static Assignment SeedPublishedSource(
        AssignmentsDbContext db,
        ITenantProvider tenants,
        string assignmentNumber = "SRC001",
        Guid? createdByTeacherId = null)
    {
        createdByTeacherId ??= Guid.NewGuid();

        var source = Assignment.Create(
                title: "Original Assignment",
                description: "Original description",
                assignmentType: AssignmentType.SemiManual,
                gradingFormat: GradingFormat.TeacherGraded,
                targetAudienceType: TargetAudienceType.SelectedGrades,
                topicId: Guid.NewGuid(),
                gradeLevelId: Guid.NewGuid(),
                dueDate: new DateTimeOffset(2026, 12, 31, 23, 59, 0, TimeSpan.Zero),
                maxScore: 100m,
                createdByTeacherId: createdByTeacherId.Value,
                mandatoryReview: false,
                assignmentNumber: assignmentNumber,
                aiPromptOverride: "Override prompt",
                archiveGraceDays: 14,
                passScore: 70m,
                maxAttempts: 2)
            .WithTenant(tenants);

        // Question 1: MultipleChoice with one correct option.
        var q1 = source.AddQuestion("What is 2+2?", QuestionType.MultipleChoice, 5);
        q1.AddOption("1", false);
        q1.AddOption("2", false);
        q1.AddOption("4", true);
        q1.AddOption("5", false);

        // Question 2: ShortAnswer with model answer.
        source.AddQuestion("Name the largest planet.", QuestionType.ShortAnswer, 1, "Jupiter");

        source.AddAttachment("syllabus.pdf", "application/pdf", 2048, "tenants/x/syllabus.pdf");

        // Two modules: add Video first (DisplayOrder 0), then Guide (DisplayOrder 1)
        // so the clone reproduces the sorted source order.
        source.AddModule(ModuleType.Video, "https://example.com/video", "Lecture video", "/videos/1", 50, false);
        source.AddModule(ModuleType.Guide, "https://example.com/guide", "Study guide", "/guides/1", 75, true);

        source.AddResource(ResourceKind.Url, "https://example.com/ref", null, "Reference page", true);
        source.AddResource(ResourceKind.File, null, "tenants/x/ref.pdf", "Reference PDF", false);

        // A review row must NOT be copied.
        source.AddReview(Guid.NewGuid(), 88m, "Looks good");

        source.Publish();

        db.Assignments.Add(source);
        db.SaveChanges();

        return source;
    }

    [TestMethod]
    public async Task HandleAsync_PublishedSource_ClonesAsFreshDraftWithCopySuffix()
    {
        // Arrange
        var (db, cache, tenants) = BuildScope("dup-published-full-matrix");
        await using var _db = db;
        var publisher = new Mock<IIntegrationEventPublisher>();
        var handler = NewHandler(db, cache, tenants, publisher: publisher);

        var source = SeedPublishedSource(db, tenants);

        // Act
        var newId = await handler.HandleAsync(new DuplicateAssignmentCommand(source.Id));

        // Assert
        newId.Should().NotBe(source.Id);

        var clone = db.Assignments.IgnoreQueryFilters().Single(a => a.Id == newId);

        clone.Title.Should().Be("Original Assignment (copy)");
        clone.Description.Should().Be(source.Description);
        clone.AssignmentType.Should().Be(source.AssignmentType);
        clone.GradingFormat.Should().Be(source.GradingFormat);
        clone.TargetAudienceType.Should().Be(source.TargetAudienceType);
        clone.TopicId.Should().Be(source.TopicId);
        clone.GradeLevelId.Should().Be(source.GradeLevelId);
        clone.DueDate.Should().Be(source.DueDate);
        clone.MaxScore.Should().Be(source.MaxScore);
        clone.MandatoryReview.Should().Be(source.MandatoryReview);
        clone.AiPromptOverride.Should().Be(source.AiPromptOverride);
        clone.ArchiveGraceDays.Should().Be(source.ArchiveGraceDays);
        clone.PassScore.Should().Be(source.PassScore);
        clone.MaxAttempts.Should().Be(source.MaxAttempts);
        clone.CreatedByTeacherId.Should().Be(source.CreatedByTeacherId);
        clone.AssignmentNumber.Should().Be("DUP999").And.NotBe(source.AssignmentNumber);
        clone.Status.Should().Be(AssignmentStatus.Draft);
        clone.PublishedAt.Should().BeNull();
        clone.AvailableFromUtc.Should().BeNull();
        clone.ApprovalStatus.Should().BeNull();
        clone.ApprovedBy.Should().BeNull();
        clone.ApprovedAt.Should().BeNull();
        clone.TenantId.Should().Be(TestTenant);

        // Questions copied in order (sorted by source DisplayOrder, re-indexed 0..n).
        clone.Questions.Should().HaveCount(2);
        clone.Questions.Select(q => q.DisplayOrder)
            .Should().BeEquivalentTo(new[] { 0, 1 }, opts => opts.WithStrictOrdering());

        var clonedMc = clone.Questions.Single(q => q.QuestionType == QuestionType.MultipleChoice);
        clonedMc.QuestionText.Should().Be("What is 2+2?");
        clonedMc.Options.Should().HaveCount(4);
        var sourceCorrect = source.Questions.Single(q => q.QuestionType == QuestionType.MultipleChoice).CorrectOptionId;
        sourceCorrect.Should().NotBeNull();
        clonedMc.CorrectOptionId.Should().NotBeNull();
        clonedMc.CorrectOptionId!.Value.Should().NotBe(sourceCorrect!.Value,
            "CorrectOptionId must be re-pointed to the new option, not copied verbatim");
        clonedMc.Options.Single(o => o.Id == clonedMc.CorrectOptionId).OptionText.Should().Be("4");

        var clonedSa = clone.Questions.Single(q => q.QuestionType == QuestionType.ShortAnswer);
        clonedSa.ModelAnswer.Should().Be("Jupiter");
        clonedSa.Options.Should().BeEmpty();

        // Attachments
        clone.Attachments.Should().HaveCount(1);
        clone.Attachments[0].FileName.Should().Be("syllabus.pdf");
        clone.Attachments[0].ContentType.Should().Be("application/pdf");
        clone.Attachments[0].FileSize.Should().Be(2048);
        clone.Attachments[0].StoragePath.Should().Be("tenants/x/syllabus.pdf");

        // Modules copied in order (sorted by DisplayOrder, re-indexed by AddModule running count).
        clone.Modules.Should().HaveCount(2);
        clone.Modules.Select(m => m.DisplayOrder)
            .Should().BeEquivalentTo(new[] { 0, 1 }, opts => opts.WithStrictOrdering());
        clone.Modules[0].ModuleType.Should().Be(ModuleType.Video);
        clone.Modules[0].Url.Should().Be("https://example.com/video");
        clone.Modules[0].Title.Should().Be("Lecture video");
        clone.Modules[0].StoragePath.Should().Be("/videos/1");
        clone.Modules[0].MinCompletionThresholdPercent.Should().Be(50);
        clone.Modules[0].IsRequired.Should().BeFalse();
        clone.Modules[1].ModuleType.Should().Be(ModuleType.Guide);
        clone.Modules[1].MinCompletionThresholdPercent.Should().Be(75);
        clone.Modules[1].IsRequired.Should().BeTrue();

        // Resources
        clone.Resources.Should().HaveCount(2);
        clone.Resources[0].ResourceKind.Should().Be(ResourceKind.Url);
        clone.Resources[0].Url.Should().Be("https://example.com/ref");
        clone.Resources[0].DisplayName.Should().Be("Reference page");
        clone.Resources[0].IncludedInGeneration.Should().BeTrue();
        clone.Resources[1].ResourceKind.Should().Be(ResourceKind.File);
        clone.Resources[1].StoragePath.Should().Be("tenants/x/ref.pdf");
        clone.Resources[1].IncludedInGeneration.Should().BeFalse();

        // Reviews must NOT carry over.
        clone.Reviews.Should().BeEmpty();

        // Integration event enqueued for the new assignment.
        publisher.Verify(
            p => p.EnqueueAsync(
                It.Is<AssignmentCreatedIntegrationEvent>(e =>
                    e.AssignmentId == newId &&
                    e.Title == clone.Title &&
                    e.AssignmentNumber == "DUP999" &&
                    e.CreatedAt == clone.CreatedAt),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task HandleAsync_UnknownSource_ThrowsAssignmentNotFound_NothingPersisted()
    {
        // Arrange
        var (db, cache, tenants) = BuildScope("dup-unknown-source");
        await using var _db = db;
        var handler = NewHandler(db, cache, tenants);

        var unknownId = Guid.NewGuid();

        // Act
        var act = async () => await handler.HandleAsync(new DuplicateAssignmentCommand(unknownId));

        // Assert
        await act.Should().ThrowAsync<AssignmentNotFoundException>();
        db.Assignments.IgnoreQueryFilters().ToList().Should().BeEmpty();
    }

    [TestMethod]
    public async Task HandleAsync_CallsEntityCodeGenerator_WithAssignmentCode()
    {
        // Arrange
        var (db, cache, tenants) = BuildScope("dup-generator-rule");
        await using var _db = db;
        var generator = new Mock<IEntityCodeGenerator>();
        generator.Setup(g => g.GenerateAsync("ASSIGNMENT_CODE", It.IsAny<CancellationToken>()))
                 .ReturnsAsync("GEN777");
        var handler = NewHandler(db, cache, tenants, generator: generator);

        var source = SeedPublishedSource(db, tenants);

        // Act
        var newId = await handler.HandleAsync(new DuplicateAssignmentCommand(source.Id));

        // Assert
        generator.Verify(g => g.GenerateAsync("ASSIGNMENT_CODE", It.IsAny<CancellationToken>()), Times.Once);
        var clone = db.Assignments.IgnoreQueryFilters().Single(a => a.Id == newId);
        clone.AssignmentNumber.Should().Be("GEN777");
    }

    [TestMethod]
    public async Task HandleAsync_DraftSource_StillDuplicates()
    {
        // Arrange
        var (db, cache, tenants) = BuildScope("dup-draft-source");
        await using var _db = db;
        var handler = NewHandler(db, cache, tenants);

        var source = Assignment.Create(
                title: "Draft Original",
                description: null,
                assignmentType: AssignmentType.Digital,
                gradingFormat: GradingFormat.AutoGraded,
                targetAudienceType: TargetAudienceType.AllStudents,
                topicId: Guid.NewGuid(),
                gradeLevelId: null,
                dueDate: null,
                maxScore: null,
                createdByTeacherId: Guid.NewGuid(),
                assignmentNumber: "DRAFT01")
            .WithTenant(tenants);
        source.AddQuestion("Q1", QuestionType.TrueFalse, 0, modelAnswer: null);
        db.Assignments.Add(source);
        await db.SaveChangesAsync();

        // Act
        var newId = await handler.HandleAsync(new DuplicateAssignmentCommand(source.Id));

        // Assert
        var clone = db.Assignments.IgnoreQueryFilters().Single(a => a.Id == newId);
        clone.Status.Should().Be(AssignmentStatus.Draft);
        clone.Title.Should().Be("Draft Original (copy)");
        clone.Questions.Should().HaveCount(1);
    }
}
