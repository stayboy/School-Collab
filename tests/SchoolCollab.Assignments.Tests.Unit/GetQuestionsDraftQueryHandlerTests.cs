using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.QuestionsDraft;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Tests.Unit.Handlers;

/// <summary>
/// WS-B2 (spec §3.4 line 73) — <see cref="GetQuestionsDraftQueryHandler"/>
/// returns the staged draft as question DTOs, null when none is staged, and
/// throws the typed exception on a corrupt blob (defensive — a broken draft
/// never renders half-parseable).
/// </summary>
[TestClass]
public class GetQuestionsDraftQueryHandlerTests
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

    private static GetQuestionsDraftQueryHandler NewHandler(AssignmentsDbContext db) =>
        new(new AssignmentRepository(db), NullLogger<GetQuestionsDraftQueryHandler>.Instance);

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

    [TestMethod]
    public async Task Returns_StagedDraft()
    {
        var (db, cache, tenants) = BuildScope("query-staged");
        await using var _db = db;
        var assignment = SeedDraft(db, tenants);
        assignment.StageQuestionsDraft(
            JsonSerializer.Serialize(
                new List<NewQuestionDto>
                {
                    new("Q1", QuestionTypeDto.ShortAnswer, 0, null),
                    new("Q2", QuestionTypeDto.ShortAnswer, 0, null)
                },
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        db.SaveChanges();
        var handler = NewHandler(db);

        var result = await handler.HandleAsync(new GetQuestionsDraftQuery(assignment.Id));

        result.Should().NotBeNull();
        result!.Should().HaveCount(2);
        result.Select(q => q.QuestionText).Should().Contain(new[] { "Q1", "Q2" });
    }

    [TestMethod]
    public async Task Null_WhenNoBlob()
    {
        var (db, cache, tenants) = BuildScope("query-none");
        await using var _db = db;
        var assignment = SeedDraft(db, tenants);
        var handler = NewHandler(db);

        var result = await handler.HandleAsync(new GetQuestionsDraftQuery(assignment.Id));

        result.Should().BeNull();
    }

    [TestMethod]
    public async Task Throws_OnCorruptBlob()
    {
        var (db, cache, tenants) = BuildScope("query-corrupt");
        await using var _db = db;
        var assignment = SeedDraft(db, tenants);
        assignment.StageQuestionsDraft("not json");
        db.SaveChanges();
        var handler = NewHandler(db);

        var act = async () => await handler.HandleAsync(new GetQuestionsDraftQuery(assignment.Id));

        await act.Should().ThrowAsync<InvalidQuestionsDraftException>();
    }
}
