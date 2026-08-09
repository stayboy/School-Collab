using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Students.Core.CQRS.TopicAssignments.Commands.RemoveTopicAssignment;
using SchoolCollab.Students.Core.CQRS.TopicAssignments.Queries.ListGradeTopicCurriculumByGrade;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// Behavioural tests for <see cref="RemoveTopicAssignmentHandler"/> — i.e. that
/// removing a topic (subject) from a grade actually ends the assignment and the
/// topic is no longer returned for the grade. The assignment is blocked/archived
/// (EndDate set), not hard-deleted, so the audit trail is retained.
/// </summary>
[TestClass]
public class RemoveTopicAssignmentHandlerTests
{
    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    private static RemoveTopicAssignmentHandler NewHandler(StudentsTestScope s) =>
        new(s.GradeTopicAssignments, s.Cache, NullLogger<RemoveTopicAssignmentHandler>.Instance);

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

    private static async Task<GradeTopicAssignment> SeedAssignmentAsync(StudentsTestScope s, Guid glId, Guid topicId)
    {
        var assignment = GradeTopicAssignment.Create(glId, topicId, Today());
        s.Db.GradeTopicAssignments.Add(assignment);
        await s.Db.SaveChangesAsync();
        return assignment;
    }

    private static async Task<Guid[]> CurriculumTopicIdsAsync(StudentsTestScope s, Guid glId) =>
        (await new ListGradeTopicCurriculumByGradeHandler(s.Db, s.Cache)
            .HandleAsync(new ListGradeTopicCurriculumByGrade(glId, Today())))
        .Select(x => x.TopicId)
        .ToArray();

    [TestMethod]
    public async Task EndsAssignment_SoTopicIsNoLongerInGrade()
    {
        using var s = new StudentsTestScope("remove-topic-effective");
        var glId = await SeedGradeLevelAsync(s, "Grade 4");
        var mathId = await SeedTopicAsync(s, "MATH", "Mathematics", 1);
        var assignment = await SeedAssignmentAsync(s, glId, mathId);

        // Sanity: the topic is in the grade's curriculum before removal.
        (await CurriculumTopicIdsAsync(s, glId)).Should().Contain(mathId);

        await NewHandler(s).HandleAsync(new RemoveTopicAssignment(assignment.Id));

        // The assignment row is retained (audit trail) but ended effective as of
        // yesterday, so it is no longer effective today (EndDate is inclusive).
        var reloaded = await s.Db.GradeTopicAssignments.SingleAsync(a => a.Id == assignment.Id);
        reloaded.EndDate.Should().Be(Today().AddDays(-1));
        reloaded.IsEffectiveOn(Today()).Should().BeFalse("the ended assignment is not effective today");

        // The topic is no longer returned for the grade.
        (await CurriculumTopicIdsAsync(s, glId)).Should().NotContain(mathId);
    }

    [TestMethod]
    public async Task OtherTopicAssignmentsForGrade_AreUnaffected()
    {
        using var s = new StudentsTestScope("remove-topic-others");
        var glId = await SeedGradeLevelAsync(s, "Grade 5");
        var mathId = await SeedTopicAsync(s, "MATH", "Mathematics", 1);
        var engId = await SeedTopicAsync(s, "ENG", "English", 2);
        var mathAssignment = await SeedAssignmentAsync(s, glId, mathId);
        var engAssignment = await SeedAssignmentAsync(s, glId, engId);

        await NewHandler(s).HandleAsync(new RemoveTopicAssignment(mathAssignment.Id));

        // English stays assigned; only Mathematics is removed.
        var engReloaded = await s.Db.GradeTopicAssignments.SingleAsync(a => a.Id == engAssignment.Id);
        engReloaded.EndDate.Should().BeNull("unrelated assignments must remain open-ended");
        (await CurriculumTopicIdsAsync(s, glId)).Should().BeEquivalentTo(new[] { engId });
    }

    [TestMethod]
    public async Task MissingAssignment_ThrowsInvalidOperationException()
    {
        using var s = new StudentsTestScope("remove-topic-missing");
        var handler = NewHandler(s);

        var act = () => handler.HandleAsync(new RemoveTopicAssignment(Guid.NewGuid()));
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }
}
