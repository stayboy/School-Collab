using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Assignments.Core.CQRS.Assignments.Queries.GetAssignmentAuthoringChildren;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Tests.Unit.Handlers;

/// <summary>
/// Assignment-authoring P1 rework — <see cref="GetAssignmentAuthoringChildrenQueryHandler"/>
/// returns one assignment's persisted questions (with options and ids), attachments and
/// resources, and null for an absent id. This is the read the Edit surface loads its
/// editors from, so a wrong shape here is exactly the destructive-replacement bug it
/// exists to close.
/// </summary>
[TestClass]
public class GetAssignmentAuthoringChildrenQueryHandlerTests
{
    private static readonly Guid TestTenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static (AssignmentsDbContext db, ITenantProvider tenants) BuildScope(string name)
    {
        var services = new ServiceCollection();
        services.AddTenancy();
        services.AddDbContext<AssignmentsDbContext>(opts => opts.UseInMemoryDatabase(name));
        services.AddDistributedMemoryCache();
        var sp = services.BuildServiceProvider();

        var db = sp.GetRequiredService<AssignmentsDbContext>();
        db.Database.EnsureCreated();
        var tenants = sp.GetRequiredService<ITenantProvider>();
        ((TenantProvider)tenants).SetTenant(new TenantContext(TestTenant, "TestSchool", TenantType.School));
        return (db, tenants);
    }

    private static GetAssignmentAuthoringChildrenQueryHandler NewHandler(AssignmentsDbContext db) =>
        new(db, NullLogger<GetAssignmentAuthoringChildrenQueryHandler>.Instance);

    private static Assignment NewAssignment(ITenantProvider tenants) =>
        Assignment.Create(
                "Title", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
                TargetAudienceType.AllStudents, Guid.NewGuid(), null, null, null)
            .WithTenant(tenants);

    [TestMethod]
    public async Task Returns_QuestionsWithOptionsIds_AttachmentsAndResources()
    {
        var (db, tenants) = BuildScope("children-full");
        await using var _db = db;
        var assignment = NewAssignment(tenants);

        // Two questions in a deliberately out-of-order DisplayOrder: the read must return
        // them in persisted order, not insertion order. Children are attached BEFORE the
        // first SaveChanges (the owned-collection seeding pattern the duplicate-handler
        // suite uses).
        var second = assignment.AddQuestion("Second?", QuestionType.MultipleChoice, 1);
        second.AddOption("B1", isCorrect: false);
        second.AddOption("B2", isCorrect: true);
        var first = assignment.AddQuestion("First?", QuestionType.ShortAnswer, 0, modelAnswer: "42");

        var attachment = assignment.AddAttachment("syllabus.pdf", "application/pdf", 2048, "tenants/t/staging/syllabus.pdf");
        var urlResource = assignment.AddResource(ResourceKind.Url, url: "https://example.com/article", displayName: "Article");
        var fileResource = assignment.AddResource(ResourceKind.File, storagePath: "tenants/t/staging/notes.pdf", displayName: "Notes");

        db.Assignments.Add(assignment);
        db.SaveChanges();

        var result = await NewHandler(db).HandleAsync(new GetAssignmentAuthoringChildrenQuery(assignment.Id));

        result.Should().NotBeNull();
        result!.AssignmentId.Should().Be(assignment.Id);

        // Questions come back in persisted DisplayOrder, not insertion order.
        result.Questions.Select(q => q.QuestionText).Should().Equal("First?", "Second?");
        result.Questions.Select(q => q.DisplayOrder).Should().Equal(0, 1);

        var shortAnswer = result.Questions[0];
        shortAnswer.Id.Should().Be(first.Id, "the question id round-trips so the editor can render the persisted row");
        shortAnswer.QuestionType.Should().Be(QuestionTypeDto.ShortAnswer);
        shortAnswer.ModelAnswer.Should().Be("42");
        shortAnswer.Options.Should().BeEmpty("a ShortAnswer question has no options");

        var multipleChoice = result.Questions[1];
        multipleChoice.Id.Should().Be(second.Id);
        multipleChoice.QuestionType.Should().Be(QuestionTypeDto.MultipleChoice);
        multipleChoice.Options.Should().HaveCount(2);
        multipleChoice.Options!.Select(o => o.OptionText).Should().Equal("B1", "B2");
        // Option correctness is carried per option — the editor derives its single
        // correct-option index from it.
        multipleChoice.Options!.Select(o => o.IsCorrect).Should().Equal(new[] { false, true });
        multipleChoice.Options.Should().OnlyContain(o => o.Id != Guid.Empty);

        result.Attachments.Should().ContainSingle();
        result.Attachments[0].Id.Should().Be(attachment.Id);
        result.Attachments[0].FileName.Should().Be("syllabus.pdf");
        result.Attachments[0].ContentType.Should().Be("application/pdf");
        result.Attachments[0].FileSize.Should().Be(2048);
        result.Attachments[0].StoragePath.Should().Be("tenants/t/staging/syllabus.pdf");

        result.Resources.Should().HaveCount(2);
        result.Resources.Select(r => r.ResourceKind).Should().BeEquivalentTo(new[] { ResourceKindDto.Url, ResourceKindDto.File });
        var file = result.Resources.Single(r => r.ResourceKind == ResourceKindDto.File);
        file.Id.Should().Be(fileResource.Id);
        file.StoragePath.Should().Be("tenants/t/staging/notes.pdf");
        file.DisplayName.Should().Be("Notes");
        result.Resources.Single(r => r.ResourceKind == ResourceKindDto.Url).Url.Should().Be("https://example.com/article");
        urlResource.Id.Should().NotBe(Guid.Empty);
    }

    [TestMethod]
    public async Task Returns_EmptyCollections_WhenTheAssignmentHasNoChildren()
    {
        var (db, tenants) = BuildScope("children-empty");
        await using var _db = db;
        var assignment = NewAssignment(tenants);
        db.Assignments.Add(assignment);
        db.SaveChanges();

        var result = await NewHandler(db).HandleAsync(new GetAssignmentAuthoringChildrenQuery(assignment.Id));

        result.Should().NotBeNull();
        result!.Questions.Should().BeEmpty();
        result.Attachments.Should().BeEmpty();
        result.Resources.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Returns_Null_ForAnAbsentAssignment()
    {
        var (db, _) = BuildScope("children-absent");
        await using var __db = db;

        var result = await NewHandler(db).HandleAsync(new GetAssignmentAuthoringChildrenQuery(Guid.NewGuid()));

        result.Should().BeNull("the route maps this to 404");
    }
}
