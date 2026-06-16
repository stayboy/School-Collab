using FluentAssertions;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Events;

namespace SchoolCollab.Assignments.Tests.Unit;

[TestClass]
public class AssignmentTests
{
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid SubjectId = Guid.Parse("00000000-0000-0000-0000-000000000010");

    [TestMethod]
    public void Create_SetsProperties()
    {
        var dueDate = DateTimeOffset.UtcNow.AddDays(7);

        var assignment = Assignment.Create(
            "Math Homework",
            "Complete exercises 1-10",
            AssignmentType.Online,
            GradingFormat.TeacherGraded,
            TargetAudience.AllStudents,
            SubjectId,
            null,
            dueDate,
            100m,
            TeacherId);

        Assert.AreEqual("Math Homework", assignment.Title);
        Assert.AreEqual("Complete exercises 1-10", assignment.Description);
        Assert.AreEqual(AssignmentType.Online, assignment.AssignmentType);
        Assert.AreEqual(SubjectId, assignment.SubjectCodedValueId);
        Assert.IsNull(assignment.GradeCodedValueId);
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
            AssignmentType.Offline,
            GradingFormat.TeacherGraded,
            TargetAudience.AllStudents,
            SubjectId,
            null, null, null,
            TeacherId);

        Assert.AreEqual("Test Title", assignment.Title);
        Assert.AreEqual("Test Description", assignment.Description);
    }

    [TestMethod]
    public void Create_SetsStatusToDraft()
    {
        var assignment = Assignment.Create("Title", null, AssignmentType.Online, GradingFormat.TeacherGraded, TargetAudience.AllStudents, SubjectId, null, null, null, TeacherId);
        Assert.AreEqual(AssignmentStatus.Draft, assignment.Status);
    }

    [TestMethod]
    public void Create_RaisesAssignmentCreatedEvent()
    {
        var assignment = Assignment.Create("Title", null, AssignmentType.Online, GradingFormat.TeacherGraded, TargetAudience.AllStudents, SubjectId, null, null, null, TeacherId);
        Assert.AreEqual(1, assignment.DomainEvents.Count);
        Assert.IsInstanceOfType(assignment.DomainEvents[0], typeof(AssignmentCreatedEvent));
    }

    [TestMethod]
    public void Update_WhenDraft_UpdatesProperties()
    {
        var assignment = Assignment.Create("Old Title", null, AssignmentType.Online, GradingFormat.TeacherGraded, TargetAudience.AllStudents, SubjectId, null, null, null, TeacherId);
        var newSubjectId = Guid.NewGuid();

        assignment.Update("New Title", "New Desc", AssignmentType.Hybrid, GradingFormat.AutoGraded, TargetAudience.SelectedStudents, newSubjectId, Guid.NewGuid(), null, 50m);

        Assert.AreEqual("New Title", assignment.Title);
        Assert.AreEqual("New Desc", assignment.Description);
        Assert.AreEqual(AssignmentType.Hybrid, assignment.AssignmentType);
        Assert.AreEqual(GradingFormat.AutoGraded, assignment.GradingFormat);
        Assert.AreEqual(TargetAudience.SelectedStudents, assignment.TargetAudience);
        Assert.AreEqual(newSubjectId, assignment.SubjectCodedValueId);
        Assert.AreEqual(50m, assignment.MaxScore);
    }

    [TestMethod]
    public void Update_WhenPublished_Throws()
    {
        var assignment = Assignment.Create("Title", null, AssignmentType.Online, GradingFormat.TeacherGraded, TargetAudience.AllStudents, SubjectId, null, null, null, TeacherId);
        assignment.Publish();
        var act = () => assignment.Update("New Title", null, AssignmentType.Online, GradingFormat.TeacherGraded, TargetAudience.AllStudents, SubjectId, null, null, null);
        act.Should().Throw<InvalidOperationException>();
    }

    [TestMethod]
    public void Update_WhenClosed_Throws()
    {
        var assignment = Assignment.Create("Title", null, AssignmentType.Online, GradingFormat.TeacherGraded, TargetAudience.AllStudents, SubjectId, null, null, null, TeacherId);
        assignment.Publish();
        assignment.Close();
        var act = () => assignment.Update("New Title", null, AssignmentType.Online, GradingFormat.TeacherGraded, TargetAudience.AllStudents, SubjectId, null, null, null);
        act.Should().Throw<InvalidOperationException>();
    }

    [TestMethod]
    public void Publish_ChangesStatusToPublished()
    {
        var assignment = Assignment.Create("Title", null, AssignmentType.Online, GradingFormat.TeacherGraded, TargetAudience.AllStudents, SubjectId, null, null, null, TeacherId);
        assignment.Publish();
        Assert.AreEqual(AssignmentStatus.Published, assignment.Status);
    }

    [TestMethod]
    public void Publish_WhenAlreadyPublished_IsNoOp()
    {
        var assignment = Assignment.Create("Title", null, AssignmentType.Online, GradingFormat.TeacherGraded, TargetAudience.AllStudents, SubjectId, null, null, null, TeacherId);
        assignment.Publish();
        assignment.Publish(); // Should not throw
        Assert.AreEqual(AssignmentStatus.Published, assignment.Status);
    }

    [TestMethod]
    public void Unpublish_ChangesStatusBackToDraft()
    {
        var assignment = Assignment.Create("Title", null, AssignmentType.Online, GradingFormat.TeacherGraded, TargetAudience.AllStudents, SubjectId, null, null, null, TeacherId);
        assignment.Publish();
        assignment.Unpublish();
        Assert.AreEqual(AssignmentStatus.Draft, assignment.Status);
    }

    [TestMethod]
    public void Unpublish_WhenDraft_Throws()
    {
        var assignment = Assignment.Create("Title", null, AssignmentType.Online, GradingFormat.TeacherGraded, TargetAudience.AllStudents, SubjectId, null, null, null, TeacherId);
        var act = () => assignment.Unpublish();
        act.Should().Throw<InvalidOperationException>();
    }

    [TestMethod]
    public void Close_ChangesStatusToClosed()
    {
        var assignment = Assignment.Create("Title", null, AssignmentType.Online, GradingFormat.TeacherGraded, TargetAudience.AllStudents, SubjectId, null, null, null, TeacherId);
        assignment.Publish();
        assignment.Close();
        Assert.AreEqual(AssignmentStatus.Closed, assignment.Status);
    }

    [TestMethod]
    public void Close_WhenDraft_ChangesToClosed()
    {
        var assignment = Assignment.Create("Title", null, AssignmentType.Online, GradingFormat.TeacherGraded, TargetAudience.AllStudents, SubjectId, null, null, null, TeacherId);
        assignment.Close();
        Assert.AreEqual(AssignmentStatus.Closed, assignment.Status);
    }

    [TestMethod]
    public void Close_WhenAlreadyClosed_IsNoOp()
    {
        var assignment = Assignment.Create("Title", null, AssignmentType.Online, GradingFormat.TeacherGraded, TargetAudience.AllStudents, SubjectId, null, null, null, TeacherId);
        assignment.Close();
        assignment.Close(); // Should not throw
        Assert.AreEqual(AssignmentStatus.Closed, assignment.Status);
    }

    [TestMethod]
    public void AddQuestion_AddsToQuestionsList()
    {
        var assignment = Assignment.Create("Title", null, AssignmentType.Online, GradingFormat.TeacherGraded, TargetAudience.AllStudents, SubjectId, null, null, null, TeacherId);
        var question = assignment.AddQuestion("What is 2+2?", QuestionType.MultipleChoice, 1);

        Assert.AreEqual(1, assignment.Questions.Count);
        Assert.AreEqual("What is 2+2?", question.QuestionText);
        Assert.AreEqual(QuestionType.MultipleChoice, question.QuestionType);
        Assert.AreEqual(1, question.DisplayOrder);
    }

    [TestMethod]
    public void RemoveQuestion_RemovesFromList()
    {
        var assignment = Assignment.Create("Title", null, AssignmentType.Online, GradingFormat.TeacherGraded, TargetAudience.AllStudents, SubjectId, null, null, null, TeacherId);
        var question = assignment.AddQuestion("Q1", QuestionType.ShortAnswer, 1);
        assignment.RemoveQuestion(question.Id);

        Assert.AreEqual(0, assignment.Questions.Count);
    }

    [TestMethod]
    public void RemoveQuestion_WithInvalidId_DoesNothing()
    {
        var assignment = Assignment.Create("Title", null, AssignmentType.Online, GradingFormat.TeacherGraded, TargetAudience.AllStudents, SubjectId, null, null, null, TeacherId);
        assignment.AddQuestion("Q1", QuestionType.ShortAnswer, 1);
        assignment.RemoveQuestion(Guid.NewGuid()); // Non-existent

        Assert.AreEqual(1, assignment.Questions.Count);
    }

    [TestMethod]
    public void AddReview_AddsToReviewsList()
    {
        var assignment = Assignment.Create("Title", null, AssignmentType.Online, GradingFormat.TeacherGraded, TargetAudience.AllStudents, SubjectId, null, null, null, TeacherId);
        var review = assignment.AddReview(TeacherId, 95m, "Good work");

        Assert.AreEqual(1, assignment.Reviews.Count);
        Assert.AreEqual(95m, review.Score);
        Assert.AreEqual("Good work", review.Comments);
    }

    [TestMethod]
    public void ClearDomainEvents_ClearsList()
    {
        var assignment = Assignment.Create("Title", null, AssignmentType.Online, GradingFormat.TeacherGraded, TargetAudience.AllStudents, SubjectId, null, null, null, TeacherId);
        Assert.AreEqual(1, assignment.DomainEvents.Count);

        assignment.ClearDomainEvents();

        Assert.AreEqual(0, assignment.DomainEvents.Count);
    }
}
