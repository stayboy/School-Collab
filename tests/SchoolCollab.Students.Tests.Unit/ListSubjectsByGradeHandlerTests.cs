using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Core.CQRS.Subjects.Queries.ListSubjectsByGrade;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Tests.Unit;

[TestClass]
public class ListSubjectsByGradeHandlerTests
{
    private static ListSubjectsByGradeHandler NewHandler(StudentsTestScope s) =>
        new(s.Db);

    private static async Task<Guid> SeedGradeLevelAsync(StudentsTestScope s, Guid codedValueId, int level, string name)
    {
        var gl = GradeLevel.Create(codedValueId, level, name, level);
        s.Db.GradeLevels.Add(gl);
        await s.Db.SaveChangesAsync();
        return gl.Id;
    }

    private static async Task<Guid> SeedSubjectAsync(StudentsTestScope s, Guid codedValueId, string code, string name, int order)
    {
        var subject = Subject.Create(codedValueId, code, name, order);
        s.Db.Subjects.Add(subject);
        await s.Db.SaveChangesAsync();
        return subject.Id;
    }

    private static async Task<Guid> SeedCurrentPeriodAsync(StudentsTestScope s, string name)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var period = Period.Create(name, today.AddDays(-1), today.AddDays(1));
        s.Db.Periods.Add(period);
        await s.Db.SaveChangesAsync();
        return period.Id;
    }

    private static async Task<Guid> SeedPastPeriodAsync(StudentsTestScope s, string name)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var period = Period.Create(name, today.AddDays(-30), today.AddDays(-15));
        s.Db.Periods.Add(period);
        await s.Db.SaveChangesAsync();
        return period.Id;
    }

    [TestMethod]
    public async Task NoCurrentPeriod_ReturnsEmpty()
    {
        using var s = new StudentsTestScope("subjects-noperiod");
        var glId = await SeedGradeLevelAsync(s, Guid.NewGuid(), 1, "Grade 1");
        await SeedSubjectAsync(s, Guid.NewGuid(), "MATH", "Mathematics", 1);

        var result = await NewHandler(s).HandleAsync(new ListSubjectsByGrade(glId));

        result.Should().BeEmpty("no current period exists");
    }

    [TestMethod]
    public async Task WithCurrentPeriod_ReturnsSubjectsAssignedToGrade()
    {
        using var s = new StudentsTestScope("subjects-current");
        var periodId = await SeedCurrentPeriodAsync(s, "Term 1");
        var glId = await SeedGradeLevelAsync(s, Guid.NewGuid(), 1, "Grade 1");
        var mathId = await SeedSubjectAsync(s, Guid.NewGuid(), "MATH", "Mathematics", 1);
        var engId = await SeedSubjectAsync(s, Guid.NewGuid(), "ENG", "English", 2);

        // Assign both subjects to Grade 1 for current period
        s.Db.GradeSubjectAssignments.Add(GradeSubjectAssignment.Create(glId, mathId, periodId));
        s.Db.GradeSubjectAssignments.Add(GradeSubjectAssignment.Create(glId, engId, periodId));
        await s.Db.SaveChangesAsync();

        var result = await NewHandler(s).HandleAsync(new ListSubjectsByGrade(glId));

        result.Should().HaveCount(2);
        result[0].Code.Should().Be("MATH");
        result[1].Code.Should().Be("ENG");
    }

    [TestMethod]
    public async Task WithExplicitPeriodId_UsesProvidedPeriod()
    {
        using var s = new StudentsTestScope("subjects-explicit-period");
        var pastPeriodId = await SeedPastPeriodAsync(s, "Term 0");
        var currentPeriodId = await SeedCurrentPeriodAsync(s, "Term 1");
        var glId = await SeedGradeLevelAsync(s, Guid.NewGuid(), 1, "Grade 1");
        var mathId = await SeedSubjectAsync(s, Guid.NewGuid(), "MATH", "Mathematics", 1);

        // Assign subject to past period only
        s.Db.GradeSubjectAssignments.Add(GradeSubjectAssignment.Create(glId, mathId, pastPeriodId));
        await s.Db.SaveChangesAsync();

        // Query with explicit past period → should find the subject
        var result = await NewHandler(s).HandleAsync(new ListSubjectsByGrade(glId, pastPeriodId));
        result.Should().ContainSingle(x => x.Code == "MATH");

        // Query with explicit current period → should be empty
        result = await NewHandler(s).HandleAsync(new ListSubjectsByGrade(glId, currentPeriodId));
        result.Should().BeEmpty();

        // Query with no period (derives current) → should be empty
        result = await NewHandler(s).HandleAsync(new ListSubjectsByGrade(glId));
        result.Should().BeEmpty();
    }

    [TestMethod]
    public async Task DifferentGradeLevel_ReturnsOnlySubjectsForThatGrade()
    {
        using var s = new StudentsTestScope("subjects-grade-filter");
        var periodId = await SeedCurrentPeriodAsync(s, "Term 1");
        var gl1Id = await SeedGradeLevelAsync(s, Guid.NewGuid(), 1, "Grade 1");
        var gl2Id = await SeedGradeLevelAsync(s, Guid.NewGuid(), 2, "Grade 2");
        var mathId = await SeedSubjectAsync(s, Guid.NewGuid(), "MATH", "Mathematics", 1);
        var engId = await SeedSubjectAsync(s, Guid.NewGuid(), "ENG", "English", 2);

        // Grade 1 has Math only
        s.Db.GradeSubjectAssignments.Add(GradeSubjectAssignment.Create(gl1Id, mathId, periodId));
        // Grade 2 has English only
        s.Db.GradeSubjectAssignments.Add(GradeSubjectAssignment.Create(gl2Id, engId, periodId));
        await s.Db.SaveChangesAsync();

        var result1 = await NewHandler(s).HandleAsync(new ListSubjectsByGrade(gl1Id));
        result1.Should().ContainSingle(x => x.Code == "MATH");

        var result2 = await NewHandler(s).HandleAsync(new ListSubjectsByGrade(gl2Id));
        result2.Should().ContainSingle(x => x.Code == "ENG");
    }

    [TestMethod]
    public async Task NoSubjectsAssigned_ReturnsEmpty()
    {
        using var s = new StudentsTestScope("subjects-empty");
        var periodId = await SeedCurrentPeriodAsync(s, "Term 1");
        var glId = await SeedGradeLevelAsync(s, Guid.NewGuid(), 1, "Grade 1");
        // No GradeSubjectAssignments seeded

        var result = await NewHandler(s).HandleAsync(new ListSubjectsByGrade(glId));

        result.Should().BeEmpty();
    }
}