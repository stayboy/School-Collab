using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Events;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// AC1 — the grade↔stream bridge: EF shape (unique index, no FK on the stream
/// coded value, strict tenant filter) plus the repository's persistence contract.
/// Pre-fix the entity/table/index do not exist at all, so every test here is
/// red-by-non-compilation against the base commit.
/// </summary>
[TestClass]
public class GradeStreamAssignmentTests
{
    private static readonly Guid OtherTenantId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static GradeLevel NewGrade() =>
        GradeLevel.Create(Guid.NewGuid(), level: 5, name: "Grade 5", displayOrder: 5);

    // ── EF model shape ─────────────────────────────────────────────────────

    [TestMethod]
    public void Model_DeclaresUniqueIndexOnTenantGradeStream()
    {
        using var s = new StudentsTestScope("gsa-index");

        var entityType = s.Db.Model.FindEntityType(typeof(GradeStreamAssignment));
        entityType.Should().NotBeNull("the bridge must be mapped in the Students model");

        var unique = entityType!.GetIndexes().Where(i => i.IsUnique).ToList();
        unique.Should().ContainSingle(
            "the (tenant, grade, stream) triple is the bridge's identity — a stream is offered by a grade at most once");
        unique[0].GetDatabaseName().Should().Be("ix_grade_stream_assignments_tenant_grade_stream");
        unique[0].Properties.Select(p => p.Name).Should().Equal("TenantId", "GradeLevelId", "StreamCodedValueId");
    }

    [TestMethod]
    public void Model_StreamCodedValueIdIsNotAForeignKey()
    {
        using var s = new StudentsTestScope("gsa-no-fk");

        var entityType = s.Db.Model.FindEntityType(typeof(GradeStreamAssignment))!;

        // Coded values live in the Settings database — a cross-DB FK is impossible,
        // so the ONLY relationship is the grade level (matching
        // StudentEnrollment.StreamCodedValueId / GradeLevel.CodedValueId).
        var fkProperties = entityType.GetForeignKeys()
            .SelectMany(fk => fk.Properties.Select(p => p.Name))
            .ToList();

        fkProperties.Should().Equal("GradeLevelId");
    }

    [TestMethod]
    public void Model_GradeLevelForeignKeyCascades()
    {
        using var s = new StudentsTestScope("gsa-fk-cascade");

        var fk = s.Db.Model.FindEntityType(typeof(GradeStreamAssignment))!
            .GetForeignKeys()
            .Single();

        fk.DeleteBehavior.Should().Be(DeleteBehavior.Cascade,
            "deleting a grade level removes its stream offers");
        fk.PrincipalEntityType.ClrType.Should().Be(typeof(GradeLevel));
    }

    [TestMethod]
    public async Task QueryFilter_IsolatesTenants()
    {
        using var s = new StudentsTestScope("gsa-tenant-filter");

        // A row owned by another tenant, written through the sanctioned bypass.
        using (s.TenantAccessor.SuppressTenantGuard())
        {
            s.Db.GradeStreamAssignments.Add(
                GradeStreamAssignment.Create(Guid.NewGuid(), Guid.NewGuid()).WithTenant(OtherTenantId));
            await s.Db.SaveChangesAsync();
        }

        (await s.Db.GradeStreamAssignments.CountAsync()).Should().Be(0,
            "the strict 'Tenant' filter hides another tenant's bridge rows");

        // A row owned by the CURRENT tenant stays visible — proving the filter is a
        // predicate, not a blanket hide.
        var grade = NewGrade();
        s.Db.GradeLevels.Add(grade);
        await s.Db.SaveChangesAsync();
        await s.GradeStreamAssignments.AddOrReuseAsync(GradeStreamAssignment.Create(grade.Id, Guid.NewGuid()));

        (await s.Db.GradeStreamAssignments.CountAsync()).Should().Be(1);
        (await s.GradeStreamAssignments.ExistsAsync(grade.Id, Guid.NewGuid())).Should().BeFalse(
            "another tenant's row must not leak into a tenant-scoped ExistsAsync either");
    }

    // ── Repository contract ────────────────────────────────────────────────

    [TestMethod]
    public async Task AddOrReuseAsync_PersistsOnce_AndExistsAsyncFindsIt()
    {
        using var s = new StudentsTestScope("gsa-add");
        var grade = GradeLevel.Create(Guid.NewGuid(), level: 5, name: "Grade 5", displayOrder: 5);
        s.Db.GradeLevels.Add(grade);
        await s.Db.SaveChangesAsync();
        var streamCodedValueId = Guid.NewGuid();

        var created = await s.GradeStreamAssignments.AddOrReuseAsync(
            GradeStreamAssignment.Create(grade.Id, streamCodedValueId));

        created.Id.Should().NotBeEmpty();
        created.GradeLevelId.Should().Be(grade.Id);
        created.StreamCodedValueId.Should().Be(streamCodedValueId);
        created.TenantId.Should().Be(s.Tenants.GetTenantContext().TenantId,
            "the save-guard stamps the current tenant on a strict entity");
        (await s.GradeStreamAssignments.ExistsAsync(grade.Id, streamCodedValueId)).Should().BeTrue();
    }

    [TestMethod]
    public async Task AddOrReuseAsync_ReturnsTheWinnersRow_ForADuplicatePair()
    {
        using var s = new StudentsTestScope("gsa-reuse");
        var grade = GradeLevel.Create(Guid.NewGuid(), level: 5, name: "Grade 5", displayOrder: 5);
        s.Db.GradeLevels.Add(grade);
        await s.Db.SaveChangesAsync();
        var streamCodedValueId = Guid.NewGuid();

        var first = await s.GradeStreamAssignments.AddOrReuseAsync(
            GradeStreamAssignment.Create(grade.Id, streamCodedValueId));
        var second = await s.GradeStreamAssignments.AddOrReuseAsync(
            GradeStreamAssignment.Create(grade.Id, streamCodedValueId));

        second.Id.Should().Be(first.Id, "the unique index makes a re-assign idempotent, not a second row");
        (await s.Db.GradeStreamAssignments.CountAsync()).Should().Be(1);
    }

    [TestMethod]
    public async Task ListByGradeLevelAsync_ReturnsOnlyThatGradesRows()
    {
        using var s = new StudentsTestScope("gsa-list");
        var gradeA = GradeLevel.Create(Guid.NewGuid(), level: 5, name: "Grade 5", displayOrder: 5);
        var gradeB = GradeLevel.Create(Guid.NewGuid(), level: 6, name: "Grade 6", displayOrder: 6);
        s.Db.GradeLevels.AddRange(gradeA, gradeB);
        await s.Db.SaveChangesAsync();

        await s.GradeStreamAssignments.AddOrReuseAsync(GradeStreamAssignment.Create(gradeA.Id, Guid.NewGuid()));
        await s.GradeStreamAssignments.AddOrReuseAsync(GradeStreamAssignment.Create(gradeB.Id, Guid.NewGuid()));

        var rows = await s.GradeStreamAssignments.ListByGradeLevelAsync(gradeA.Id);

        rows.Should().ContainSingle();
        rows[0].GradeLevelId.Should().Be(gradeA.Id);
    }

    [TestMethod]
    public async Task DeleteAsync_RemovesTheBridgeRow()
    {
        using var s = new StudentsTestScope("gsa-delete");
        var grade = GradeLevel.Create(Guid.NewGuid(), level: 5, name: "Grade 5", displayOrder: 5);
        s.Db.GradeLevels.Add(grade);
        await s.Db.SaveChangesAsync();
        var streamCodedValueId = Guid.NewGuid();
        var row = await s.GradeStreamAssignments.AddOrReuseAsync(
            GradeStreamAssignment.Create(grade.Id, streamCodedValueId));

        await s.GradeStreamAssignments.DeleteAsync(row);

        (await s.GradeStreamAssignments.ExistsAsync(grade.Id, streamCodedValueId)).Should().BeFalse();
        (await s.Db.GradeStreamAssignments.CountAsync()).Should().Be(0);
    }

    // ── Domain factory ─────────────────────────────────────────────────────

    [TestMethod]
    public void Create_RaisesGradeStreamAssignedEvent()
    {
        var gradeLevelId = Guid.NewGuid();
        var streamCodedValueId = Guid.NewGuid();

        var assignment = GradeStreamAssignment.Create(gradeLevelId, streamCodedValueId);

        assignment.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<GradeStreamAssignedEvent>()
            .Which.Should().BeEquivalentTo(new { GradeLevelId = gradeLevelId, StreamCodedValueId = streamCodedValueId });

        assignment.CreatedAt.Should().Be(assignment.UpdatedAt);
        assignment.ClearDomainEvents();
        assignment.DomainEvents.Should().BeEmpty();
    }
}
