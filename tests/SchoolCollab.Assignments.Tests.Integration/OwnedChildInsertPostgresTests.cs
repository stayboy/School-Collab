using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Core.Data;
using SchoolCollab.Assignments.Core.Data.Repositories;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Tests.Integration;

/// <summary>
/// Regression guard for the pre-existing P0 that R3's Postgres round-trip tests uncovered and this
/// round fixed (§17, <c>docs/specs/assignment-authoring-compartments.md</c>): on PostgreSQL, adding a
/// child to an ALREADY PERSISTED assignment never INSERTed it.
/// <para><b>Discrimination (verified at base <c>0c8912da</c>, diff absent).</b> Without
/// <c>ValueGeneratedNever()</c> on the owned keys, EF Core tracks the new child as <c>Modified</c> and
/// emits <c>UPDATE assignment_questions SET … WHERE id = &lt;the new id&gt;</c> with no INSERT, so every
/// test here threw <c>DbUpdateConcurrencyException</c>. Two of the three therefore assert the row EXISTS
/// in the database (read back from a FRESH context) as well as that no exception escaped — a repair that
/// silently wrote nothing would pass a "did not throw" assertion and fail these.</para>
/// <para><b>Why it cannot live in the unit suite:</b> the InMemory provider tracks the same wrong state
/// but performs no row-count check, so it silently "succeeds" and would have masked the defect.</para>
/// </summary>
[TestClass]
public sealed class OwnedChildInsertPostgresTests
{
    private static readonly Guid TenantId = Guid.Parse("cccccccc-dddd-eeee-ffff-000000000001");
    private static readonly Guid TopicId = Guid.Parse("cccccccc-dddd-eeee-ffff-000000000002");

    private static ITenantProvider NewTenantProvider()
    {
        var provider = new TenantProvider();
        ((TenantProvider)provider).SetTenant(new TenantContext(TenantId, "TestTenant", TenantType.School));
        return provider;
    }

    private static Assignment NewAssignment(ITenantProvider tenants, string title) =>
        Assignment.Create(
                title, null, AssignmentType.Digital, GradingFormat.AutoGraded,
                TargetAudienceType.AllStudents, TopicId, null, null, null,
                createdByTeacherId: Guid.Empty)
            .WithTenant(tenants);

    private static async Task<Guid> SeedAsync(string connectionString, ITenantProvider tenants, Assignment assignment)
    {
        await using var db = AssignmentsDbFactory.CreateContext(connectionString, TenantId);
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();
        return assignment.Id;
    }

    [TestMethod]
    public async Task AddingAQuestionToAPersistedAssignment_InsertsTheRow()
    {
        var connectionString = await AssignmentsDbFactory.CreateMigratedDatabaseAsync(Guid.NewGuid().ToString("N"));
        var tenants = NewTenantProvider();

        var seeded = NewAssignment(tenants, "Question insert guard");
        var existingQuestionId = seeded.AddQuestion("Seeded?", QuestionType.ShortAnswer, 0).Id;
        var assignmentId = await SeedAsync(connectionString, tenants, seeded);

        Guid addedId;
        await using (var db = AssignmentsDbFactory.CreateContext(connectionString, TenantId))
        {
            var loaded = (await new AssignmentRepository(db).GetAsync(assignmentId))!;
            addedId = loaded.AddQuestion("Added later?", QuestionType.ShortAnswer, 1).Id;
            await db.SaveChangesAsync();
        }

        await using var read = AssignmentsDbFactory.CreateContext(connectionString, TenantId);
        var questions = (await read.Assignments.AsNoTracking().SingleAsync(a => a.Id == assignmentId)).Questions;

        questions.Should().HaveCount(2, "the new question must be INSERTed, not dropped on the floor");
        questions.Select(q => q.Id).Should().Contain(addedId);
        questions.Select(q => q.Id).Should().Contain(existingQuestionId, "the pre-existing row is untouched");
    }

    [TestMethod]
    public async Task AddingAnAttachmentToAPersistedAssignment_InsertsTheRow()
    {
        var connectionString = await AssignmentsDbFactory.CreateMigratedDatabaseAsync(Guid.NewGuid().ToString("N"));
        var tenants = NewTenantProvider();

        var seeded = NewAssignment(tenants, "Attachment insert guard");
        var existingAttachmentId = seeded.AddAttachment(
            "seeded.pdf", "application/pdf", 1024, "tenants/t/staging/seeded.pdf").Id;
        var assignmentId = await SeedAsync(connectionString, tenants, seeded);

        Guid addedId;
        await using (var db = AssignmentsDbFactory.CreateContext(connectionString, TenantId))
        {
            var loaded = (await new AssignmentRepository(db).GetAsync(assignmentId))!;
            addedId = loaded.AddAttachment(
                "added.pdf", "application/pdf", 2048, "tenants/t/staging/added.pdf").Id;
            await db.SaveChangesAsync();
        }

        await using var read = AssignmentsDbFactory.CreateContext(connectionString, TenantId);
        var attachments = (await read.Assignments.AsNoTracking().SingleAsync(a => a.Id == assignmentId)).Attachments;

        attachments.Should().HaveCount(2);
        attachments.Select(a => a.Id).Should().Contain(addedId);
        attachments.Select(a => a.Id).Should().Contain(existingAttachmentId);
    }

    [TestMethod]
    public async Task ReplacingEveryQuestionOnAPersistedAssignment_PersistsTheNewRowsAndDropsTheOld()
    {
        // The full-replacement sync the update handler performs: remove-each-then-add. Before the fix the
        // added rows were tracked as Modified, so this threw and — had it not — nothing would have been
        // written. Asserted on real Postgres because only that provider checks the row count.
        var connectionString = await AssignmentsDbFactory.CreateMigratedDatabaseAsync(Guid.NewGuid().ToString("N"));
        var tenants = NewTenantProvider();

        var seeded = NewAssignment(tenants, "Question replace guard");
        var oldId = seeded.AddQuestion("Old?", QuestionType.ShortAnswer, 0).Id;
        var assignmentId = await SeedAsync(connectionString, tenants, seeded);

        Guid newId;
        await using (var db = AssignmentsDbFactory.CreateContext(connectionString, TenantId))
        {
            var loaded = (await new AssignmentRepository(db).GetAsync(assignmentId))!;
            loaded.RemoveQuestion(oldId);
            newId = loaded.AddQuestion("New?", QuestionType.ShortAnswer, 0).Id;
            await db.SaveChangesAsync();
        }

        await using var read = AssignmentsDbFactory.CreateContext(connectionString, TenantId);
        var questions = (await read.Assignments.AsNoTracking().SingleAsync(a => a.Id == assignmentId)).Questions;

        questions.Should().HaveCount(1);
        questions.Single().Id.Should().Be(newId);
        questions.Single().QuestionText.Should().Be("New?");
        questions.Select(q => q.Id).Should().NotContain(oldId, "the replaced row really was deleted");
    }

    [TestMethod]
    public async Task AddingAnOptionToAnExistingQuestion_InsertsTheOptionRow()
    {
        // Same omission on the nested QuestionOption key: the option is an owned collection one level
        // deeper, so it needs its own guard.
        var connectionString = await AssignmentsDbFactory.CreateMigratedDatabaseAsync(Guid.NewGuid().ToString("N"));
        var tenants = NewTenantProvider();

        var seeded = NewAssignment(tenants, "Option insert guard");
        seeded.AddQuestion("Seeded?", QuestionType.MultipleChoice, 0).AddOption("A", isCorrect: true);
        var assignmentId = await SeedAsync(connectionString, tenants, seeded);

        await using (var db = AssignmentsDbFactory.CreateContext(connectionString, TenantId))
        {
            var loaded = (await new AssignmentRepository(db).GetAsync(assignmentId))!;
            loaded.Questions.Single().AddOption("B");
            await db.SaveChangesAsync();
        }

        await using var read = AssignmentsDbFactory.CreateContext(connectionString, TenantId);
        var options = (await read.Assignments.AsNoTracking().SingleAsync(a => a.Id == assignmentId))
            .Questions.Single().Options;

        options.Should().HaveCount(2, "the second option must be INSERTed");
        options.Select(o => o.OptionText).Should().BeEquivalentTo(["A", "B"]);
    }
}
