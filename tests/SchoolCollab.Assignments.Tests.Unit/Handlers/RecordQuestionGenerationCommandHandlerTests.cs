using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.QuestionGeneration;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Tests.Unit.Handlers;

/// <summary>
/// R3 (acceptance criterion 4, handler half; plan-review P1-2) — the generation header is written by
/// the Assignments host at generate time, recording the requested shape and the provider/model the AI
/// host resolved. Nothing here exists against <c>0c8912da</c>.
/// <para>The persistence half (one row, the questions' <c>GenerationId</c> surviving a save, tenancy)
/// is asserted against real Postgres in <c>SchoolCollab.Assignments.Tests.Integration</c>.</para>
/// </summary>
[TestClass]
public class RecordQuestionGenerationCommandHandlerTests
{
    private static readonly Guid TestTenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static (AssignmentsDbContext db, ITenantProvider tenants) BuildScope(string name)
    {
        var services = new ServiceCollection();
        services.AddTenancy();
        services.AddDbContext<AssignmentsDbContext>(opts => opts.UseInMemoryDatabase(name));
        var sp = services.BuildServiceProvider();

        var db = sp.GetRequiredService<AssignmentsDbContext>();
        db.Database.EnsureCreated();
        var tenants = sp.GetRequiredService<ITenantProvider>();
        ((TenantProvider)tenants).SetTenant(new TenantContext(TestTenant, "TestSchool", TenantType.School));
        return (db, tenants);
    }

    private static RecordQuestionGenerationCommandHandler NewHandler(AssignmentsDbContext db) =>
        new(db, new AssignmentQuestionGenerationRepository(db),
            NullLogger<RecordQuestionGenerationCommandHandler>.Instance);

    private static Assignment NewAssignment(ITenantProvider tenants) =>
        Assignment.Create(
                "Title", null, AssignmentType.Digital, GradingFormat.AutoGraded,
                TargetAudienceType.AllStudents, Guid.NewGuid(), null, null, null)
            .WithTenant(tenants);

    private static RecordQuestionGenerationCommand Command(
        Guid assignmentId,
        string? model = "gemma4:31b-cloud",
        string? provider = "ollama",
        int questionCount = 5) =>
        new(assignmentId, questionCount, null, null, null, null, provider, model);

    [TestMethod]
    public async Task HandleAsync_RecordsExactlyOneHeaderPerCall_WithTheRequestedShapeAndTheResolvedModel()
    {
        var (db, tenants) = BuildScope("gen-header-happy");
        await using var _db = db;
        var assignment = NewAssignment(tenants);
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();

        var handler = NewHandler(db);
        var command = new RecordQuestionGenerationCommand(
            assignment.Id,
            QuestionCount: 12,
            Types: [QuestionTypeDto.MultipleChoice, QuestionTypeDto.ShortAnswer],
            DifficultyEasyCount: 4,
            DifficultyMediumCount: 5,
            DifficultyHardCount: 3,
            Provider: "openrouter",
            Model: "google/gemma-4-31b-it");

        var generationId = await handler.HandleAsync(command);

        generationId.Should().NotBeEmpty();
        var rows = await db.AssignmentQuestionGenerations.ToListAsync();
        rows.Should().HaveCount(1, "one generation writes exactly one header row");
        var header = rows.Single();
        header.Id.Should().Be(generationId);
        header.AssignmentId.Should().Be(assignment.Id);
        header.TenantId.Should().Be(TestTenant);
        header.QuestionCount.Should().Be(12);
        header.Types.Should().Be("MultipleChoice,ShortAnswer");
        header.DifficultyEasyCount.Should().Be(4);
        header.DifficultyMediumCount.Should().Be(5);
        header.DifficultyHardCount.Should().Be(3);
        // P1-1/D6: the values the AI host reported — never a client-chosen model.
        header.Provider.Should().Be("openrouter");
        header.Model.Should().Be("google/gemma-4-31b-it");
    }

    [TestMethod]
    public async Task HandleAsync_AllTypesSelected_RecordsANullTypeSet()
    {
        var (db, tenants) = BuildScope("gen-header-all-types");
        await using var _db = db;
        var assignment = NewAssignment(tenants);
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();

        var generationId = await NewHandler(db).HandleAsync(
            new RecordQuestionGenerationCommand(assignment.Id, 5, null, null, null, null, "ollama", "m"));

        var header = await db.AssignmentQuestionGenerations.SingleAsync(x => x.Id == generationId);
        header.Types.Should().BeNull("no selection means the AI host's balanced default, not an empty set");
    }

    [TestMethod]
    public async Task HandleAsync_TwoGenerations_WriteTwoDistinctRows()
    {
        var (db, tenants) = BuildScope("gen-header-two");
        await using var _db = db;
        var assignment = NewAssignment(tenants);
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();

        var handler = NewHandler(db);
        var first = await handler.HandleAsync(Command(assignment.Id));
        var second = await handler.HandleAsync(Command(assignment.Id));

        first.Should().NotBe(second, "a regeneration is a new generation, not an update of the last one");
        (await db.AssignmentQuestionGenerations.CountAsync()).Should().Be(2);
    }

    [TestMethod]
    public async Task HandleAsync_MissingModel_IsRejectedInsteadOfRecordingANamelessHeader()
    {
        var (db, tenants) = BuildScope("gen-header-no-model");
        await using var _db = db;
        var assignment = NewAssignment(tenants);
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();

        var act = async () => await NewHandler(db).HandleAsync(Command(assignment.Id, model: "  "));

        await act.Should().ThrowAsync<AssignmentContentValidationException>()
            .WithMessage("*resolved model is required*");
        (await db.AssignmentQuestionGenerations.CountAsync()).Should().Be(0);
    }

    [TestMethod]
    public async Task HandleAsync_QuestionCountOutOfRange_IsRejected()
    {
        var (db, tenants) = BuildScope("gen-header-bad-count");
        await using var _db = db;
        var assignment = NewAssignment(tenants);
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();

        var handler = NewHandler(db);
        var tooMany = async () => await handler.HandleAsync(Command(assignment.Id, questionCount: 31));
        var none = async () => await handler.HandleAsync(Command(assignment.Id, questionCount: 0));

        await tooMany.Should().ThrowAsync<AssignmentContentValidationException>();
        await none.Should().ThrowAsync<AssignmentContentValidationException>();
        (await db.AssignmentQuestionGenerations.CountAsync()).Should().Be(0);
    }

    [TestMethod]
    public async Task HandleAsync_UnknownAssignment_ThrowsNotFoundAndWritesNothing()
    {
        var (db, _) = BuildScope("gen-header-missing");
        await using var __db = db;

        var act = async () => await NewHandler(db).HandleAsync(Command(Guid.NewGuid()));

        await act.Should().ThrowAsync<AssignmentNotFoundException>();
        (await db.AssignmentQuestionGenerations.CountAsync()).Should().Be(0);
    }

    [TestMethod]
    public async Task HandleAsync_AssignmentBelongingToAnotherTenant_ReadsAsAbsent()
    {
        // The header must inherit the assignment's tenant, and another tenant's assignment id must not
        // be reachable at all — so its generation history can never be extended from outside.
        var (db, tenants) = BuildScope("gen-header-cross-tenant");
        await using var __db = db;
        var provider = (TenantProvider)tenants;
        // Seed the foreign assignment UNDER ITS OWN TENANT (the save guard rejects any other route),
        // then come back to the test tenant and try to reach it.
        provider.SetTenant(new TenantContext(
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), "Other", TenantType.School));
        var foreign = NewAssignment(provider);
        db.Assignments.Add(foreign);
        await db.SaveChangesAsync();
        provider.SetTenant(new TenantContext(TestTenant, "TestSchool", TenantType.School));

        var act = async () => await NewHandler(db).HandleAsync(Command(foreign.Id));

        await act.Should().ThrowAsync<AssignmentNotFoundException>();
        (await db.AssignmentQuestionGenerations.IgnoreQueryFilters(["Tenant"]).CountAsync()).Should().Be(0);
    }

    [TestMethod]
    public void FormatTypes_JoinsNamesInAuthorOrder_AndReturnsNullForAnEmptySelection()
    {
        RecordQuestionGenerationCommandHandler
            .FormatTypes([QuestionTypeDto.ShortAnswer, QuestionTypeDto.TrueFalse])
            .Should().Be("ShortAnswer,TrueFalse");
        RecordQuestionGenerationCommandHandler.FormatTypes(null).Should().BeNull();
        RecordQuestionGenerationCommandHandler.FormatTypes([]).Should().BeNull();
    }

    [TestMethod]
    public void Create_RejectsAnEmptyTenantOrAssignmentOrModel()
    {
        var act = () => AssignmentQuestionGeneration.Create(
            Guid.Empty, Guid.NewGuid(), 5, null, null, null, null, "ollama", "m");

        act.Should().Throw<ArgumentException>();
    }
}
