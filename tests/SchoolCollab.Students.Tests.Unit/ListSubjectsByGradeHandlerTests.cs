using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Core.CQRS.Topics.Queries.ListTopicsByGrade;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Tests.Unit;

[TestClass]
public class ListTopicsByGradeHandlerTests
{
    private static ListTopicsByGradeHandler NewHandler(StudentsTestScope s) =>
        new(s.Db);

    private static async Task<Guid> SeedGradeLevelAsync(StudentsTestScope s, Guid codedValueId, int level, string name)
    {
        var gl = GradeLevel.Create(codedValueId, level, name, level);
        s.Db.GradeLevels.Add(gl);
        await s.Db.SaveChangesAsync();
        return gl.Id;
    }

    private static async Task<Guid> SeedTopicAsync(StudentsTestScope s, Guid codedValueId, string code, string name, int order)
    {
        var topic = Topic.Create(codedValueId, code, name, order);
        s.Db.Topics.Add(topic);
        await s.Db.SaveChangesAsync();
        return topic.Id;
    }

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    [TestMethod]
    public async Task NoEffectiveDate_ReturnsTopicsEffectiveToday()
    {
        // Assignments are date-based and open-ended: a topic assigned with a
        // StartDate in the past and no EndDate is still effective today.
        using var s = new StudentsTestScope("subjects-noperiod");
        var glId = await SeedGradeLevelAsync(s, Guid.NewGuid(), 1, "Grade 1");
        var mathId = await SeedTopicAsync(s, Guid.NewGuid(), "MATH", "Mathematics", 1);

        s.Db.GradeSubjectAssignments.Add(
            GradeSubjectAssignment.Create(glId, activityGroupId: null, mathId, Today().AddDays(-30)));
        await s.Db.SaveChangesAsync();

        var result = await NewHandler(s).HandleAsync(new ListTopicsByGrade(glId));

        result.Should().ContainSingle(x => x.Code == "MATH");
    }

    [TestMethod]
    public async Task WithEffectiveDate_ReturnsTopicsAssignedToGrade()
    {
        using var s = new StudentsTestScope("subjects-current");
        var glId = await SeedGradeLevelAsync(s, Guid.NewGuid(), 1, "Grade 1");
        var mathId = await SeedTopicAsync(s, Guid.NewGuid(), "MATH", "Mathematics", 1);
        var engId = await SeedTopicAsync(s, Guid.NewGuid(), "ENG", "English", 2);

        // Assign both subjects to Grade 1, effective from today (open-ended).
        s.Db.GradeSubjectAssignments.Add(GradeSubjectAssignment.Create(glId, activityGroupId: null, mathId, Today()));
        s.Db.GradeSubjectAssignments.Add(GradeSubjectAssignment.Create(glId, activityGroupId: null, engId, Today()));
        await s.Db.SaveChangesAsync();

        var result = await NewHandler(s).HandleAsync(new ListTopicsByGrade(glId));

        result.Should().HaveCount(2);
        result[0].Code.Should().Be("MATH");
        result[1].Code.Should().Be("ENG");
    }

    [TestMethod]
    public async Task BlockedAssignment_NotReturnedAsEffective()
    {
        // A blocked/archived assignment has an EndDate; it must be excluded from
        // the effective set on any date after it ended.
        using var s = new StudentsTestScope("subjects-blocked");
        var glId = await SeedGradeLevelAsync(s, Guid.NewGuid(), 1, "Grade 1");
        var mathId = await SeedTopicAsync(s, Guid.NewGuid(), "MATH", "Mathematics", 1);

        // Assigned effective [-30, -10] — ended in the past.
        s.Db.GradeSubjectAssignments.Add(
            GradeSubjectAssignment.Create(glId, activityGroupId: null, mathId, Today().AddDays(-30), Today().AddDays(-10)));
        await s.Db.SaveChangesAsync();

        var result = await NewHandler(s).HandleAsync(new ListTopicsByGrade(glId));
        result.Should().BeEmpty("a blocked/archived assignment is not effective today");

        // An explicit effectiveDate inside the window still sees it (historical view).
        var historical = await NewHandler(s).HandleAsync(
            new ListTopicsByGrade(glId, Today().AddDays(-20)));
        historical.Should().ContainSingle(x => x.Code == "MATH");
    }

    [TestMethod]
    public async Task WithExplicitEffectiveDate_FiltersByDate()
    {
        using var s = new StudentsTestScope("subjects-explicit-date");
        var glId = await SeedGradeLevelAsync(s, Guid.NewGuid(), 1, "Grade 1");
        var mathId = await SeedTopicAsync(s, Guid.NewGuid(), "MATH", "Mathematics", 1);

        // Math effective only from +10 (future) — not effective today.
        s.Db.GradeSubjectAssignments.Add(
            GradeSubjectAssignment.Create(glId, activityGroupId: null, mathId, Today().AddDays(10)));
        await s.Db.SaveChangesAsync();

        var today = await NewHandler(s).HandleAsync(new ListTopicsByGrade(glId));
        today.Should().BeEmpty("a topic starting in the future is not effective today");

        var future = await NewHandler(s).HandleAsync(new ListTopicsByGrade(glId, Today().AddDays(10)));
        future.Should().ContainSingle(x => x.Code == "MATH");
    }

    [TestMethod]
    public async Task DifferentGradeLevel_ReturnsOnlyTopicsForThatGrade()
    {
        using var s = new StudentsTestScope("subjects-grade-filter");
        var gl1Id = await SeedGradeLevelAsync(s, Guid.NewGuid(), 1, "Grade 1");
        var gl2Id = await SeedGradeLevelAsync(s, Guid.NewGuid(), 2, "Grade 2");
        var mathId = await SeedTopicAsync(s, Guid.NewGuid(), "MATH", "Mathematics", 1);
        var engId = await SeedTopicAsync(s, Guid.NewGuid(), "ENG", "English", 2);

        // Grade 1 has Math only
        s.Db.GradeSubjectAssignments.Add(GradeSubjectAssignment.Create(gl1Id, activityGroupId: null, mathId, Today()));
        // Grade 2 has English only
        s.Db.GradeSubjectAssignments.Add(GradeSubjectAssignment.Create(gl2Id, activityGroupId: null, engId, Today()));
        await s.Db.SaveChangesAsync();

        var result1 = await NewHandler(s).HandleAsync(new ListTopicsByGrade(gl1Id));
        result1.Should().ContainSingle(x => x.Code == "MATH");

        var result2 = await NewHandler(s).HandleAsync(new ListTopicsByGrade(gl2Id));
        result2.Should().ContainSingle(x => x.Code == "ENG");
    }

    [TestMethod]
    public async Task NoTopicsAssigned_ReturnsEmpty()
    {
        using var s = new StudentsTestScope("subjects-empty");
        var glId = await SeedGradeLevelAsync(s, Guid.NewGuid(), 1, "Grade 1");
        // No GradeSubjectAssignments seeded

        var result = await NewHandler(s).HandleAsync(new ListTopicsByGrade(glId));

        result.Should().BeEmpty();
    }
}
