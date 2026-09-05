using FluentAssertions;
using SchoolCollab.Assignments.Core.Domain;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// Phase B1 round ar-1: covers the new domain surface from
/// <c>documents/specs/assignment-creation-with-ai.md</c> §3.1:
/// <list type="bullet">
///   <item><see cref="Assignment.AddAttachment"/> / <see cref="Assignment.RemoveAttachment"/></item>
///   <item><see cref="AssignmentQuestion.ModelAnswer"/> on the new optional
///         <c>modelAnswer</c> parameter to <see cref="Assignment.AddQuestion"/></item>
///   <item><see cref="AssignmentQuestion.AddOption"/> setting <c>CorrectOptionId</c></item>
/// </list>
/// </summary>
[TestClass]
public class AssignmentQuestionAttachmentTests
{
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000010");

    private static Assignment NewAssignment() =>
        Assignment.Create("Title", null, AssignmentType.Digital,
            GradingFormat.AutoGraded, TargetAudienceType.AllStudents,
            TopicId, null, null, null, TeacherId);

    [TestMethod]
    public void AddAttachment_AddsToAttachmentsList_AndReturnsRow()
    {
        var assignment = NewAssignment();

        var attachment = assignment.AddAttachment(
            fileName: "syllabus.pdf",
            contentType: "application/pdf",
            fileSize: 1024,
            storagePath: "tenants/abc/assignments/x/syllabus.pdf");

        assignment.Attachments.Should().HaveCount(1, "AddAttachment must append to the owned list");
        assignment.Attachments[0].Id.Should().Be(attachment.Id, "the returned row must be the persisted one");
        assignment.Attachments[0].FileName.Should().Be("syllabus.pdf");
        assignment.Attachments[0].ContentType.Should().Be("application/pdf");
        assignment.Attachments[0].FileSize.Should().Be(1024);
        assignment.Attachments[0].StoragePath.Should().Be("tenants/abc/assignments/x/syllabus.pdf");
        assignment.Attachments[0].AssignmentId.Should().Be(assignment.Id);
    }

    [TestMethod]
    public void RemoveAttachment_RemovesFromList()
    {
        var assignment = NewAssignment();
        var attachment = assignment.AddAttachment("a.txt", "text/plain", 1, "tenants/x/a.txt");

        assignment.RemoveAttachment(attachment.Id);

        assignment.Attachments.Should().BeEmpty();
    }

    [TestMethod]
    public void RemoveAttachment_WithUnknownId_IsNoOp()
    {
        var assignment = NewAssignment();
        assignment.AddAttachment("a.txt", "text/plain", 1, "tenants/x/a.txt");

        assignment.RemoveAttachment(Guid.NewGuid());

        assignment.Attachments.Should().HaveCount(1, "RemoveAttachment must silently ignore unknown ids");
    }

    [TestMethod]
    public void AddQuestion_AcceptsModelAnswer()
    {
        var assignment = NewAssignment();

        var q = assignment.AddQuestion("Name the main product of photosynthesis.",
            QuestionType.ShortAnswer, displayOrder: 0, modelAnswer: "Glucose");

        q.ModelAnswer.Should().Be("Glucose",
            "ModelAnswer must be settable via the new optional parameter");
    }

    [TestMethod]
    public void AddQuestion_WithoutModelAnswer_DefaultsToNull()
    {
        var assignment = NewAssignment();

        var q = assignment.AddQuestion("2 + 2 = ?", QuestionType.ShortAnswer, displayOrder: 0);

        q.ModelAnswer.Should().BeNull(
            "ModelAnswer must default to null when not supplied — preserves the prior contract");
    }

    [TestMethod]
    public void AddQuestion_AddOptionIsCorrectTrue_SetsCorrectOptionId()
    {
        var assignment = NewAssignment();
        var q = assignment.AddQuestion("Pick A.", QuestionType.MultipleChoice, 0);

        var correct = q.AddOption("A", isCorrect: true);
        q.AddOption("B", isCorrect: false);

        q.CorrectOptionId.Should().Be(correct.Id,
            "AddOption with isCorrect=true must set CorrectOptionId to the new option");
    }
}
