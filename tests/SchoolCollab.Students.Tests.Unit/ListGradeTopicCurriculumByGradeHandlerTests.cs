using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Core.CQRS.TopicAssignments.Queries.ListActivityGroupTopicAssignments;
using SchoolCollab.Students.Core.CQRS.TopicAssignments.Queries.ListGradeTopicAssignments;
using SchoolCollab.Students.Core.CQRS.TopicAssignments.Queries.ListGradeTopicCurriculumByGrade;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// The grade-detail curriculum reader. It is a <b>management</b> read
/// (subject-period-exception-model.md v3 §5.2 rows 3-4 / AC-9): an excepted subject
/// stays listed so the Subjects card and the View-all dialog can render its
/// <c>n exceptions</c> badge and the navigating kebab. The availability filter lives
/// only on the two FR-58 feed readers, pinned both ways in
/// <c>ExceptedTopic_IsStillListed_WhileTheFeedReadersExcludeIt</c>.
/// </summary>
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

    [TestMethod]
    public async Task ExceptedTopic_IsStillListed_WhileTheFeedReadersExcludeIt()
    {
        // P1-1 (parent ruling, 2026-09-26): the curriculum read is the MANAGEMENT surface.
        // Spec §5.2 rows 3-4 and AC-9 require the grade-detail Subjects card and the View-all
        // dialog to show the `n exceptions` badge and the navigating kebab, which is only
        // possible while the excepted subject is listed. The exception filter stays on the two
        // FR-58 publish/feed readers, asserted here in the same test so the split is pinned.
        using var s = new StudentsTestScope("curriculum-excepted");
        var glId = await SeedGradeLevelAsync(s, "Grade 4");
        var mathId = await SeedTopicAsync(s, "MATH", "Mathematics", 1);

        s.Db.GradeTopicAssignments.Add(GradeTopicAssignment.Create(glId, mathId, Today()));
        s.Db.SubjectEnrollmentExceptions.Add(SubjectEnrollmentException.Create(
            s.Tenants.GetTenantContext().TenantId, glId, null, mathId,
            AcademicYearDivision.None, Today().AddDays(-10), Today().AddDays(10)));
        await s.Db.SaveChangesAsync();

        var curriculum = await NewHandler(s).HandleAsync(new ListGradeTopicCurriculumByGrade(glId, Today()));
        curriculum.Should().ContainSingle(x => x.TopicId == mathId,
            "the management read must keep listing an excepted subject so its badge and kebab can render");

        var gradeFeed = await new ListGradeTopicAssignmentsHandler(s.Db, s.Cache)
            .HandleAsync(new ListGradeTopicAssignments(glId, Today()));
        gradeFeed.Should().BeEmpty("the FR-58 feed reader still excludes the excepted subject");

        var group = ActivityGroup.Create("Term Club", span: EnrollmentSpan.Termly);
        s.Db.ActivityGroups.Add(group);
        s.Db.ActivityGroupTopicAssignments.Add(ActivityGroupTopicAssignment.Create(group.Id, mathId, Today()));
        s.Db.SubjectEnrollmentExceptions.Add(SubjectEnrollmentException.Create(
            s.Tenants.GetTenantContext().TenantId, null, group.Id, mathId,
            AcademicYearDivision.Terms, Today().AddDays(-10), Today().AddDays(10)));
        await s.Db.SaveChangesAsync();

        var groupFeed = await new ListActivityGroupTopicAssignmentsHandler(s.Db, s.Cache)
            .HandleAsync(new ListActivityGroupTopicAssignments(group.Id, Today()));
        groupFeed.Should().BeEmpty("the group-side FR-58 feed reader still excludes the excepted subject");
    }
}
