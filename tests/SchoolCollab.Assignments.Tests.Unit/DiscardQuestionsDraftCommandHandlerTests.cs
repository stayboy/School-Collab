using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Commands.QuestionsDraft;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Tests.Unit.Handlers;

/// <summary>
/// WS-B2 (spec §3.4 line 73) — <see cref="DiscardQuestionsDraftCommandHandler"/>
/// clears a staged questions draft. Idempotent when none is staged.
/// </summary>
[TestClass]
public class DiscardQuestionsDraftCommandHandlerTests
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

    private static DiscardQuestionsDraftCommandHandler NewHandler(AssignmentsDbContext db, HybridCache cache) =>
        new(new AssignmentRepository(db), cache, NullLogger<DiscardQuestionsDraftCommandHandler>.Instance);

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
    public async Task Discards_StagedBlob()
    {
        var (db, cache, tenants) = BuildScope("discard-staged");
        await using var _db = db;
        var assignment = SeedDraft(db, tenants);
        assignment.StageQuestionsDraft("[]");
        db.SaveChanges();
        var handler = NewHandler(db, cache);

        await handler.HandleAsync(new DiscardQuestionsDraftCommand(assignment.Id));

        db.Assignments.IgnoreQueryFilters().Single(a => a.Id == assignment.Id).QuestionsDraftJson.Should().BeNull();
    }

    [TestMethod]
    public async Task Succeeds_WhenNoBlob()
    {
        var (db, cache, tenants) = BuildScope("discard-none");
        await using var _db = db;
        var assignment = SeedDraft(db, tenants);
        var handler = NewHandler(db, cache);

        var act = async () => await handler.HandleAsync(new DiscardQuestionsDraftCommand(assignment.Id));

        await act.Should().NotThrowAsync("discard is idempotent when no draft is staged");
    }
}
