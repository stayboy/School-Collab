using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Core.CQRS.TopicAssignments.Commands.AssignGradeTopic;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// AC9 for the plain grade↔subject assign flow: the created bridge row appends at
/// the END of its grade's subject list (bridge <c>DisplayOrder == max + 1</c>), so
/// the curriculum read — and therefore the grid and its preview — puts a newly
/// assigned subject last. The deprecated <c>PeriodId</c> stays accepted-and-ignored.
/// </summary>
[TestClass]
public class AssignGradeTopicHandlerTests
{
    private static AssignGradeTopicHandler NewHandler(StudentsTestScope s) =>
        new(s.GradeTopicAssignments, s.Cache, NullLogger<AssignGradeTopicHandler>.Instance);

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    private static async Task<Guid> SeedGradeAsync(StudentsTestScope s, string name)
    {
        var grade = GradeLevel.Create(Guid.NewGuid(), 1, name, 1);
        s.Db.GradeLevels.Add(grade);
        await s.Db.SaveChangesAsync();
        return grade.Id;
    }

    private static async Task<Guid> SeedTopicAsync(StudentsTestScope s, string code, string name, int topicOrder)
    {
        var topic = Topic.Create(Guid.NewGuid(), code, name, topicOrder);
        s.Db.Topics.Add(topic);
        await s.Db.SaveChangesAsync();
        return topic.Id;
    }

    private static async Task SeedExistingAssignmentAsync(
        StudentsTestScope s, Guid gradeId, Guid topicId, int order)
    {
        s.Db.GradeTopicAssignments.Add(GradeTopicAssignment.Create(gradeId, topicId, Today(), displayOrder: order));
        await s.Db.SaveChangesAsync();
    }

    [TestMethod]
    public async Task Assign_AppendsTheBridgeRowAtTheEndOfTheGradesList()
    {
        using var s = new StudentsTestScope("agt-append");
        var gradeId = await SeedGradeAsync(s, "Grade 4");
        await SeedExistingAssignmentAsync(s, gradeId, await SeedTopicAsync(s, "MATH", "Mathematics", 1), 0);
        await SeedExistingAssignmentAsync(s, gradeId, await SeedTopicAsync(s, "ENG", "English", 2), 5);
        var scienceId = await SeedTopicAsync(s, "SCI", "Science", 3);

        var assignmentId = await NewHandler(s).HandleAsync(new AssignGradeTopic(gradeId, scienceId, Today()));

        var created = await s.Db.GradeTopicAssignments.SingleAsync(x => x.Id == assignmentId);
        created.DisplayOrder.Should().Be(6, "a new subject appends at the END (max + 1)");
        created.PeriodId.Should().BeNull("the deprecated PeriodId is accepted and ignored");
        created.GradeLevelId.Should().Be(gradeId);
        created.TopicId.Should().Be(scienceId);
    }

    [TestMethod]
    public async Task FirstSubjectOfAGrade_GetsOrderZero()
    {
        using var s = new StudentsTestScope("agt-first");
        var gradeId = await SeedGradeAsync(s, "Grade 1");
        var topicId = await SeedTopicAsync(s, "MATH", "Mathematics", 1);

        var assignmentId = await NewHandler(s).HandleAsync(new AssignGradeTopic(gradeId, topicId, Today()));

        var created = await s.Db.GradeTopicAssignments.SingleAsync(x => x.Id == assignmentId);
        created.DisplayOrder.Should().Be(0, "the first subject of a grade occupies position 0");
    }
}
