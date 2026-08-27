using FluentAssertions;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Events;

namespace SchoolCollab.Assignments.Tests.Unit;

[TestClass]
public class AssignmentTests
{
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000010");

    private static Assignment CreateTestAssignment(
        string title = "Title",
        string? description = null,
        AssignmentType type = AssignmentType.Digital,
        GradingFormat grading = GradingFormat.TeacherGraded,
        TargetAudienceType audience = TargetAudienceType.AllStudents) =>
        Assignment.Create(title, description, type, grading, audience, TopicId, null, null, null, TeacherId);

    [TestMethod]
    public void Create_WithEmptyTopicId_Throws()
    {
        var act = () => Assignment.Create(
            "Test",
            null,
            AssignmentType.Digital,
            GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents,
            Guid.Empty, // empty topic
            null, null, null,
            TeacherId);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("topicId")
            .WithMessage("Topic is required.*");
    }

    [TestMethod]
    public void Create_SelectedGrades_NullGrade_Throws()
    {
        var act = () => Assignment.Create(
            "Test",
            null,
            AssignmentType.Digital,
            GradingFormat.TeacherGraded,
            TargetAudienceType.SelectedGrades,
            TopicId, null, null, null,
            TeacherId);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("gradeLevelId")
            .WithMessage("SelectedGrades assignments require a grade level.*");
    }

    [TestMethod]
    public void Update_SelectedGrades_NullGrade_Throws()
    {
        var assignment = CreateTestAssignment();
        var act = () => assignment.Update(
            "New Title", null, AssignmentType.Digital,
            GradingFormat.TeacherGraded, TargetAudienceType.SelectedGrades,
            TopicId, null, null, null, true);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("gradeLevelId")
            .WithMessage("SelectedGrades assignments require a grade level.*");
    }

    [TestMethod]
    public void Update_WithEmptyTopicId_Throws()
    {
        var assignment = CreateTestAssignment();

        var act = () => assignment.Update(
            "New Title",
            null,
            AssignmentType.Digital,
            GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents,
            Guid.Empty, // empty topic
            null, null, null, true);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("topicId")
            .WithMessage("Topic is required.*");
    }

    [TestMethod]
    public void Create_SetsProperties()
    {
        var dueDate = DateTimeOffset.UtcNow.AddDays(7);

        var assignment = Assignment.Create(
            "Math Homework",
            "Complete exercises 1-10",
            AssignmentType.Digital,
            GradingFormat.AutoGraded,
            TargetAudienceType.AllStudents,
            TopicId,
            null,
            dueDate,
            100m,
            TeacherId);

        Assert.AreEqual("Math Homework", assignment.Title);
        Assert.AreEqual("Complete exercises 1-10", assignment.Description);
        Assert.AreEqual(AssignmentType.Digital, assignment.AssignmentType);
        Assert.AreEqual(GradingFormat.AutoGraded, assignment.GradingFormat);
        Assert.AreEqual(TargetAudienceType.AllStudents, assignment.TargetAudienceType);
        Assert.AreEqual(TopicId, assignment.TopicId);
        Assert.IsNull(assignment.GradeLevelId);
        Assert.AreEqual(dueDate, assignment.DueDate);
        Assert.AreEqual(100m, assignment.MaxScore);
        Assert.AreEqual(AssignmentStatus.Draft, assignment.Status);
        Assert.AreEqual(TeacherId, assignment.CreatedByTeacherId);
        Assert.AreNotEqual(Guid.Empty, assignment.Id);
    }

    [TestMethod]
    public void Create_TrimsTitleAndDescription()
    {
        var assignment = Assignment.Create(
            "  Test Title  ",
            "  Test Description  ",
            AssignmentType.Manual,
            GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents,
            TopicId,
            null, null, null,
            TeacherId);

        Assert.AreEqual("Test Title", assignment.Title);
        Assert.AreEqual("Test Description", assignment.Description);
    }

    [TestMethod]
    public void Create_SetsStatusToDraft()
    {
        var assignment = CreateTestAssignment();
        Assert.AreEqual(AssignmentStatus.Draft, assignment.Status);
    }

    [TestMethod]
    public void Create_RaisesAssignmentCreatedEvent()
    {
        var assignment = CreateTestAssignment();
        Assert.AreEqual(1, assignment.DomainEvents.Count);
        Assert.IsInstanceOfType(assignment.DomainEvents[0], typeof(AssignmentCreatedEvent));
    }

    [TestMethod]
    public void Create_DefaultGradingFormatIsTeacherGraded()
    {
        var assignment = CreateTestAssignment(grading: GradingFormat.TeacherGraded);
        Assert.AreEqual(GradingFormat.TeacherGraded, assignment.GradingFormat);
    }

    [TestMethod]
    public void Create_DefaultTargetAudienceIsAllStudents()
    {
        var assignment = CreateTestAssignment(audience: TargetAudienceType.AllStudents);
        Assert.AreEqual(TargetAudienceType.AllStudents, assignment.TargetAudienceType);
    }

    [TestMethod]
    public void Update_WhenDraft_UpdatesProperties()
    {
        var assignment = CreateTestAssignment("Old Title");
        var newTopicId = Guid.NewGuid();

        assignment.Update("New Title", "New Desc", AssignmentType.SemiManual,
            GradingFormat.InstantGraded, TargetAudienceType.SelectedGrades,
            newTopicId, Guid.NewGuid(), null, 50m, true);

        Assert.AreEqual("New Title", assignment.Title);
        Assert.AreEqual("New Desc", assignment.Description);
        Assert.AreEqual(AssignmentType.SemiManual, assignment.AssignmentType);
        Assert.AreEqual(GradingFormat.InstantGraded, assignment.GradingFormat);
        Assert.AreEqual(TargetAudienceType.SelectedGrades, assignment.TargetAudienceType);
        Assert.AreEqual(newTopicId, assignment.TopicId);
        Assert.AreEqual(50m, assignment.MaxScore);
    }

    [TestMethod]
    public void Update_WhenPublished_Throws()
    {
        var assignment = CreateTestAssignment();
        assignment.Publish();
        var act = () => assignment.Update("New Title", null, AssignmentType.Digital,
            GradingFormat.TeacherGraded, TargetAudienceType.AllStudents,
            TopicId, null, null, null, true);
        act.Should().Throw<InvalidOperationException>();
    }

    [TestMethod]
    public void Update_WhenClosed_Throws()
    {
        var assignment = CreateTestAssignment();
        assignment.Publish();
        assignment.Close();
        var act = () => assignment.Update("New Title", null, AssignmentType.Digital,
            GradingFormat.TeacherGraded, TargetAudienceType.AllStudents,
            TopicId, null, null, null, true);
        act.Should().Throw<InvalidOperationException>();
    }

    [TestMethod]
    public void Publish_ChangesStatusToPublished()
    {
        var assignment = CreateTestAssignment();
        assignment.Publish();
        Assert.AreEqual(AssignmentStatus.Published, assignment.Status);
    }

    [TestMethod]
    public void Publish_WhenAlreadyPublished_IsNoOp()
    {
        var assignment = CreateTestAssignment();
        assignment.Publish();
        assignment.Publish(); // Should not throw
        Assert.AreEqual(AssignmentStatus.Published, assignment.Status);
    }

    [TestMethod]
    public void Unpublish_ChangesStatusBackToDraft()
    {
        var assignment = CreateTestAssignment();
        assignment.Publish();
        assignment.Unpublish();
        Assert.AreEqual(AssignmentStatus.Draft, assignment.Status);
    }

    [TestMethod]
    public void Unpublish_WhenDraft_Throws()
    {
        var assignment = CreateTestAssignment();
        var act = () => assignment.Unpublish();
        act.Should().Throw<InvalidOperationException>();
    }

    [TestMethod]
    public void Close_ChangesStatusToClosed()
    {
        var assignment = CreateTestAssignment();
        assignment.Publish();
        assignment.Close();
        Assert.AreEqual(AssignmentStatus.Closed, assignment.Status);
    }

    [TestMethod]
    public void Close_WhenDraft_ChangesToClosed()
    {
        var assignment = CreateTestAssignment();
        assignment.Close();
        Assert.AreEqual(AssignmentStatus.Closed, assignment.Status);
    }

    [TestMethod]
    public void Close_WhenAlreadyClosed_IsNoOp()
    {
        var assignment = CreateTestAssignment();
        assignment.Close();
        assignment.Close(); // Should not throw
        Assert.AreEqual(AssignmentStatus.Closed, assignment.Status);
    }

    [TestMethod]
    public void AddQuestion_AddsToQuestionsList()
    {
        var assignment = CreateTestAssignment();
        var question = assignment.AddQuestion("What is 2+2?", QuestionType.MultipleChoice, 1);

        Assert.AreEqual(1, assignment.Questions.Count);
        Assert.AreEqual("What is 2+2?", question.QuestionText);
        Assert.AreEqual(QuestionType.MultipleChoice, question.QuestionType);
        Assert.AreEqual(1, question.DisplayOrder);
    }

    [TestMethod]
    public void RemoveQuestion_RemovesFromList()
    {
        var assignment = CreateTestAssignment();
        var question = assignment.AddQuestion("Q1", QuestionType.ShortAnswer, 1);
        assignment.RemoveQuestion(question.Id);

        Assert.AreEqual(0, assignment.Questions.Count);
    }

    [TestMethod]
    public void RemoveQuestion_WithInvalidId_DoesNothing()
    {
        var assignment = CreateTestAssignment();
        assignment.AddQuestion("Q1", QuestionType.ShortAnswer, 1);
        assignment.RemoveQuestion(Guid.NewGuid()); // Non-existent

        Assert.AreEqual(1, assignment.Questions.Count);
    }

    [TestMethod]
    public void AddReview_AddsToReviewsList()
    {
        var assignment = CreateTestAssignment();
        var review = assignment.AddReview(TeacherId, 95m, "Good work");

        Assert.AreEqual(1, assignment.Reviews.Count);
        Assert.AreEqual(95m, review.Score);
        Assert.AreEqual("Good work", review.Comments);
    }

    [TestMethod]
    public void ClearDomainEvents_ClearsList()
    {
        var assignment = CreateTestAssignment();
        Assert.AreEqual(1, assignment.DomainEvents.Count);

        assignment.ClearDomainEvents();

        Assert.AreEqual(0, assignment.DomainEvents.Count);
    }
}
