using FluentAssertions;
using SchoolCollab.Students.Core.CQRS.Topics.Commands.CreateTopicStrand;
using SchoolCollab.Students.Core.CQRS.Topics.Commands.UpdateTopicStrand;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// Verifies the strand-parenting invariants (strand-lesson-unification-plan.md):
/// a strand with a parent is a lesson; the parent must be a root strand in the same
/// topic and never itself.
/// </summary>
[TestClass]
public class TopicStrandHandlerTests
{
    [TestMethod]
    public async Task Create_WithValidRootParent_CreatesLesson()
    {
        using var s = new StudentsTestScope("strand-create-valid-parent");
        var topicId = Guid.NewGuid();
        var parent = TopicStrand.Create(topicId, "Numbers", null, 1);
        s.Db.TopicStrands.Add(parent);
        await s.Db.SaveChangesAsync();

        var result = await new CreateTopicStrandHandler(s.Db).HandleAsync(
            new CreateTopicStrand(topicId, "Add", null, 1, parent.Id), CancellationToken.None);

        result.ParentStrandId.Should().Be(parent.Id);
        result.IsLesson.Should().BeTrue("a strand with a parent is a lesson");
        result.TopicId.Should().Be(topicId);
    }

    [TestMethod]
    public async Task Create_WithoutParent_CreatesRootStrand()
    {
        using var s = new StudentsTestScope("strand-create-root");
        var topicId = Guid.NewGuid();

        var result = await new CreateTopicStrandHandler(s.Db).HandleAsync(
            new CreateTopicStrand(topicId, "Algebra", null, 1), CancellationToken.None);

        result.ParentStrandId.Should().BeNull();
        result.IsLesson.Should().BeFalse();
    }

    [TestMethod]
    public async Task Create_WithLessonAsParent_Throws()
    {
        using var s = new StudentsTestScope("strand-create-lesson-parent");
        var topicId = Guid.NewGuid();
        var root = TopicStrand.Create(topicId, "Numbers", null, 1);
        var lesson = TopicStrand.Create(topicId, "Add", null, 1, root.Id);
        s.Db.TopicStrands.Add(root);
        s.Db.TopicStrands.Add(lesson);
        await s.Db.SaveChangesAsync();

        var act = () => new CreateTopicStrandHandler(s.Db).HandleAsync(
            new CreateTopicStrand(topicId, "Sub-sub", null, 1, lesson.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*cannot be a parent*", "lessons (parented strands) cannot be parents");
    }

    [TestMethod]
    public async Task Create_WithParentFromOtherTopic_Throws()
    {
        using var s = new StudentsTestScope("strand-create-other-topic-parent");
        var topicA = Guid.NewGuid();
        var topicB = Guid.NewGuid();
        var parent = TopicStrand.Create(topicA, "Numbers", null, 1);
        s.Db.TopicStrands.Add(parent);
        await s.Db.SaveChangesAsync();

        var act = () => new CreateTopicStrandHandler(s.Db).HandleAsync(
            new CreateTopicStrand(topicB, "Add", null, 1, parent.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*same topic*");
    }

    [TestMethod]
    public async Task Create_WithMissingParent_Throws()
    {
        using var s = new StudentsTestScope("strand-create-missing-parent");
        var topicId = Guid.NewGuid();

        var act = () => new CreateTopicStrandHandler(s.Db).HandleAsync(
            new CreateTopicStrand(topicId, "Add", null, 1, Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*Parent strand*");
    }

    [TestMethod]
    public async Task Update_SettingSelfAsParent_Throws()
    {
        using var s = new StudentsTestScope("strand-update-self-parent");
        var topicId = Guid.NewGuid();
        var strand = TopicStrand.Create(topicId, "Algebra", null, 1);
        s.Db.TopicStrands.Add(strand);
        await s.Db.SaveChangesAsync();

        var act = () => new UpdateTopicStrandHandler(s.Db).HandleAsync(
            new UpdateTopicStrand(strand.Id, "Algebra", null, 1, strand.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*own parent*");
    }

    [TestMethod]
    public async Task Update_SettingValidParent_TurnsItIntoLesson()
    {
        using var s = new StudentsTestScope("strand-update-valid-parent");
        var topicId = Guid.NewGuid();
        var parent = TopicStrand.Create(topicId, "Numbers", null, 1);
        var strand = TopicStrand.Create(topicId, "Lesson placeholder", null, 1);
        s.Db.TopicStrands.Add(parent);
        s.Db.TopicStrands.Add(strand);
        await s.Db.SaveChangesAsync();

        var result = await new UpdateTopicStrandHandler(s.Db).HandleAsync(
            new UpdateTopicStrand(strand.Id, "Add", null, 1, parent.Id), CancellationToken.None);

        result.ParentStrandId.Should().Be(parent.Id);
        result.IsLesson.Should().BeTrue();
    }
}
