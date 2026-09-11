using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Core.CQRS.GradeAssignmentPolicies.Commands.UpsertGradeAssignmentPolicy;
using SchoolCollab.Students.Core.CQRS.GradeAssignmentPolicies.Queries.GetGradeAssignmentPolicy;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Core.Domain.Exceptions;

namespace SchoolCollab.Students.Tests.Unit;

[TestClass]
public class GradeAssignmentPolicyHandlerTests
{
    private static async Task<Guid> SeedGradeAsync(StudentsTestScope s, string name = "Grade 1")
    {
        var gl = GradeLevel.Create(Guid.NewGuid(), 1, name, 1);
        s.Db.GradeLevels.Add(gl);
        await s.Db.SaveChangesAsync();
        return gl.Id;
    }

    private static GetGradeAssignmentPolicyHandler NewGet(StudentsTestScope s) =>
        new(s.Db);

    private static UpsertGradeAssignmentPolicyHandler NewUpsert(StudentsTestScope s) =>
        new(s.Db, s.Tenants);

    [TestMethod]
    public async Task Get_WhenNoRow_ReturnsNull()
    {
        using var s = new StudentsTestScope("gap-get-null");
        var gradeId = await SeedGradeAsync(s);

        var result = await NewGet(s).HandleAsync(new GetGradeAssignmentPolicy(gradeId));
        result.Should().BeNull();
    }

    [TestMethod]
    public async Task Upsert_CreatesOverride_GetReturnsDto()
    {
        using var s = new StudentsTestScope("gap-upsert-create");
        var gradeId = await SeedGradeAsync(s);

        await NewUpsert(s).HandleAsync(new UpsertGradeAssignmentPolicy(gradeId, RequiresSignatureDefault: true));

        var result = await NewGet(s).HandleAsync(new GetGradeAssignmentPolicy(gradeId));
        result.Should().NotBeNull();
        result!.GradeLevelId.Should().Be(gradeId);
        result.RequiresSignatureDefault.Should().BeTrue();
    }

    [TestMethod]
    public async Task Upsert_ReplacesWithInherit()
    {
        using var s = new StudentsTestScope("gap-upsert-inherit");
        var gradeId = await SeedGradeAsync(s);
        var upsert = NewUpsert(s);

        await upsert.HandleAsync(new UpsertGradeAssignmentPolicy(gradeId, RequiresSignatureDefault: true));
        await upsert.HandleAsync(new UpsertGradeAssignmentPolicy(gradeId, RequiresSignatureDefault: null));

        var result = await NewGet(s).HandleAsync(new GetGradeAssignmentPolicy(gradeId));
        result!.RequiresSignatureDefault.Should().BeNull(); // inherit restored
        (await s.Db.GradeAssignmentPolicies.CountAsync()).Should().Be(1);
    }

    [TestMethod]
    public async Task Upsert_UnknownGrade_ThrowsGradeLevelNotFound()
    {
        using var s = new StudentsTestScope("gap-upsert-nograde");
        var act = () => NewUpsert(s).HandleAsync(
            new UpsertGradeAssignmentPolicy(Guid.NewGuid(), RequiresSignatureDefault: true));
        await act.Should().ThrowAsync<GradeLevelNotFoundException>();
    }
}