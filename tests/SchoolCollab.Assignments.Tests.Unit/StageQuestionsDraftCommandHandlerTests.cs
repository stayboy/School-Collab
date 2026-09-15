using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.QuestionsDraft;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Tests.Unit.Handlers;

/// <summary>
/// WS-B2 (spec §3.4 line 73) — <see cref="StageQuestionsDraftCommandHandler"/>
/// validates the inbound questions FIRST (FR-252 — an invalid draft never enters
/// the blob), serializes with the Api's Web-default JSON options, and stages via
/// the Draft-only domain guard.
/// </summary>
[TestClass]
public class StageQuestionsDraftCommandHandlerTests
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

    private static StageQuestionsDraftCommandHandler NewHandler(AssignmentsDbContext db, HybridCache cache) =>
        new(new AssignmentRepository(db), cache, NullLogger<StageQuestionsDraftCommandHandler>.Instance);

    private static Assignment SeedDraft(AssignmentsDbContext db, ITenantProvider tenants)
    {
        var assignment = Assignment.Create(
                "Title", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
                TargetAudienceType.AllStudents, Guid.NewGuid(), null, null, null)
            .WithTenant(tenants);
        db.Assignments.Add(assignment);
        db.SaveChanges();
        return assignment;
    }

    private static NewQuestionDto Mc(int displayOrder) =>
        new(
            "Pick the capital of France.",
            QuestionTypeDto.MultipleChoice,
            displayOrder,
            [new NewQuestionOptionDto("Berlin", false), new NewQuestionOptionDto("Paris", true)]);

    [TestMethod]
    public async Task Stages_ValidatedQuestions_IntoBlob()
    {
        var (db, cache, tenants) = BuildScope("stage-ok");
        await using var _db = db;
        var assignment = SeedDraft(db, tenants);
        var handler = NewHandler(db, cache);

        await handler.HandleAsync(new StageQuestionsDraftCommand(assignment.Id, [Mc(0), Mc(1)]));

        db.Assignments.IgnoreQueryFilters().Single(a => a.Id == assignment.Id).QuestionsDraftJson.Should().NotBeNull();
    }

    [TestMethod]
    public async Task Throws_WhenAssignmentMissing()
    {
        var (db, cache, _) = BuildScope("stage-missing");
        await using var _db = db;
        var handler = NewHandler(db, cache);

        var act = async () => await handler.HandleAsync(new StageQuestionsDraftCommand(Guid.NewGuid(), [Mc(0)]));

        await act.Should().ThrowAsync<AssignmentNotFoundException>();
    }

    [TestMethod]
    public async Task Throws_WhenNotDraft()
    {
        var (db, cache, tenants) = BuildScope("stage-scheduled");
        await using var _db = db;
        var assignment = SeedDraft(db, tenants);
        assignment.Schedule(DateTimeOffset.UtcNow.AddDays(3));
        db.SaveChanges();
        var handler = NewHandler(db, cache);

        var act = async () => await handler.HandleAsync(new StageQuestionsDraftCommand(assignment.Id, [Mc(0)]));

        await act.Should().ThrowAsync<InvalidQuestionsDraftException>();
    }

    [TestMethod]
    public async Task Rejects_InvalidQuestionPayload_BeforeStaging()
    {
        var (db, cache, tenants) = BuildScope("stage-invalid");
        await using var _db = db;
        var assignment = SeedDraft(db, tenants);
        var handler = NewHandler(db, cache);

        var invalid = new NewQuestionDto(
            "Q?", QuestionTypeDto.MultipleChoice, 0,
            [new NewQuestionOptionDto("A", false), new NewQuestionOptionDto("B", false)]);

        var act = async () => await handler.HandleAsync(new StageQuestionsDraftCommand(assignment.Id, [invalid]));

        await act.Should().ThrowAsync<AssignmentQuestionValidationException>();
        db.Assignments.IgnoreQueryFilters().Single(a => a.Id == assignment.Id).QuestionsDraftJson.Should().BeNull(
            "an invalid draft must never enter the blob (FR-252)");
    }
}
