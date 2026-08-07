using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Core.CQRS.TopicAssignments.Queries.ListGradeTopicCurriculumByGrade;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Tests.Unit;

[TestClass]
public class ListGradeTopicCurriculumByGradeHandlerTests
{
    private static ListGradeTopicCurriculumByGradeHandler NewHandler(StudentsTestScope s) =>
        new(s.Db, s.Cache);

    private static async Task<Guid> SeedGradeLevelAsync(StudentsTestScope s, string name)
    {
        var gl = GradeLevel.Create(Guid.NewGuid(), 1, name, 1);
        s.Db.GradeLevels.Add(gl);
        await s.Db.SaveChangesAsync();
        return gl.Id;
    }

    private static async Task<Guid> SeedTopicAsync(StudentsTestScope s, string code, string name, int order)
    {
        var topic = Topic.Create(Guid.NewGuid(), code, name, order);
        s.Db.Topics.Add(topic);
        await s.Db.SaveChangesAsync();
        return topic.Id;
    }

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    [TestMethod]
    public async Task ReturnsPerTopicStrandAndLessonCounts()
    {
        using var s = new StudentsTestScope("curriculum-counts");
        var glId = await SeedGradeLevelAsync(s, "Grade 4");
        var mathId = await SeedTopicAsync(s, "MATH", "Mathematics", 1);
        var engId = await SeedTopicAsync(s, "ENG", "English", 2);

        s.Db.GradeTopicAssignments.Add(GradeTopicAssignment.Create(glId, mathId, Today()));
        s.Db.GradeTopicAssignments.Add(GradeTopicAssignment.Create(glId, engId, Today()));
        await s.Db.SaveChangesAsync();

        // Mathematics: 2 strands + 3 lessons (lessons = parented strands).
        var numbers = TopicStrand.Create(mathId, "Numbers", null, 1);
        s.Db.TopicStrands.Add(numbers);
        s.Db.TopicStrands.Add(TopicStrand.Create(mathId, "Algebra", null, 2));
        s.Db.TopicStrands.Add(TopicStrand.Create(mathId, "Add", null, 1, numbers.Id));
        s.Db.TopicStrands.Add(TopicStrand.Create(mathId, "Subtract", null, 2, numbers.Id));
        s.Db.TopicStrands.Add(TopicStrand.Create(mathId, "Multiply", null, 3, numbers.Id));
        // English: 1 strand, 0 lessons.
        s.Db.TopicStrands.Add(TopicStrand.Create(engId, "Reading", null, 1));
        await s.Db.SaveChangesAsync();

        var result = await NewHandler(s).HandleAsync(new ListGradeTopicCurriculumByGrade(glId, Today()));

        result.Should().HaveCount(2);
        var math = result.Should().ContainSingle(x => x.Code == "MATH").Which;
        math.StrandCount.Should().Be(2);
        math.LessonCount.Should().Be(3);
        var eng = result.Should().ContainSingle(x => x.Code == "ENG").Which;
        eng.StrandCount.Should().Be(1);
        eng.LessonCount.Should().Be(0);
    }

    [TestMethod]
    public async Task EmptyGrade_ReturnsEmpty()
    {
        using var s = new StudentsTestScope("curriculum-empty");
        var glId = await SeedGradeLevelAsync(s, "Grade 7");

        var result = await NewHandler(s).HandleAsync(new ListGradeTopicCurriculumByGrade(glId, Today()));

        result.Should().BeEmpty();
    }
}
