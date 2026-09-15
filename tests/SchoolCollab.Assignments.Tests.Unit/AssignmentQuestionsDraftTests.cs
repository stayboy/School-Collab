using FluentAssertions;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Domain.Exceptions;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// WS-B2 (spec §3.4 line 73) — the staged AI questions draft on the
/// assignment aggregate. Staging / confirming / discarding is Draft-only; the
/// state guard throws the typed <see cref="InvalidQuestionsDraftException"/>,
/// never the raw <see cref="InvalidOperationException"/>, per decision (d).
/// </summary>
[TestClass]
public class AssignmentQuestionsDraftTests
{
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000010");

    private static Assignment NewAssignment() =>
        Assignment.Create("Title", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, null, null, null);

    private static Assignment NewScheduled()
    {
        var a = NewAssignment();
        a.Schedule(DateTimeOffset.UtcNow.AddDays(3));
        return a;
    }

    [TestMethod]
    public void Stage_OnDraft_SetsBlobAndStampsUpdatedAt()
    {
        var assignment = NewAssignment();
        var before = assignment.UpdatedAt;

        assignment.StageQuestionsDraft("[]");

        assignment.QuestionsDraftJson.Should().Be("[]");
        assignment.UpdatedAt.Should().BeAfter(before);
    }

    [TestMethod]
    public void Stage_RejectsEmptyJson()
    {
        var assignment = NewAssignment();

        var act = () => assignment.StageQuestionsDraft("   ");

        act.Should().Throw<ArgumentException>();
    }

    [TestMethod]
    public void Stage_OnScheduled_ThrowsInvalidDraft()
    {
        var assignment = NewScheduled();

        var act = () => assignment.StageQuestionsDraft("[]");

        act.Should().Throw<InvalidQuestionsDraftException>();
    }

    [TestMethod]
    public void Discard_ClearsBlob()
    {
        var assignment = NewAssignment();
        assignment.StageQuestionsDraft("[]");

        assignment.DiscardQuestionsDraft();

        assignment.QuestionsDraftJson.Should().BeNull();
    }

    [TestMethod]
    public void Discard_NoBlob_IsIdempotent()
    {
        var assignment = NewAssignment();

        var act = () => assignment.DiscardQuestionsDraft();

        act.Should().NotThrow();
        assignment.QuestionsDraftJson.Should().BeNull();
    }

    [TestMethod]
    public void Discard_OnScheduled_ThrowsInvalidDraft()
    {
        var assignment = NewScheduled();

        var act = () => assignment.DiscardQuestionsDraft();

        act.Should().Throw<InvalidQuestionsDraftException>();
    }

    [TestMethod]
    public void Confirm_OnDraft_ClearsBlob()
    {
        var assignment = NewAssignment();
        assignment.StageQuestionsDraft("[]");

        assignment.ConfirmQuestionsDraft();

        assignment.QuestionsDraftJson.Should().BeNull();
    }

    [TestMethod]
    public void Confirm_NoBlob_ThrowsInvalidDraft()
    {
        var assignment = NewAssignment();

        var act = () => assignment.ConfirmQuestionsDraft();

        act.Should().Throw<InvalidQuestionsDraftException>();
    }

    [TestMethod]
    public void Confirm_OnScheduled_ThrowsInvalidDraft()
    {
        var assignment = NewScheduled();

        var act = () => assignment.ConfirmQuestionsDraft();

        act.Should().Throw<InvalidQuestionsDraftException>();
    }
}
