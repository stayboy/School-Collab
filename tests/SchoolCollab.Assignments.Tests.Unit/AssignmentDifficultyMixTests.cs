using FluentAssertions;
using SchoolCollab.Assignments.Core.Domain;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// WS-B2 (spec §3.4 line 70) — the assignment difficulty mix: optional
/// per-difficulty counts (easy / medium / hard) that the AI question
/// generator reconciles. Null = let the model decide; a negative count is
/// rejected as <see cref="ArgumentException"/> (domain guard), and the counts
/// round-trip through <see cref="Assignment.Update"/>.
/// </summary>
[TestClass]
public class AssignmentDifficultyMixTests
{
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000010");

    private static Assignment NewAssignment() =>
        Assignment.Create("Title", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, null, null, null);

    [TestMethod]
    public void Create_Defaults_ToNullCounts_LetTheModelDecide()
    {
        var assignment = NewAssignment();

        assignment.DifficultyEasyCount.Should().BeNull();
        assignment.DifficultyMediumCount.Should().BeNull();
        assignment.DifficultyHardCount.Should().BeNull();
    }

    [TestMethod]
    public void Create_WithCounts_SetsDifficultyMix()
    {
        var assignment = Assignment.Create(
            "Title", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, null, null, null,
            difficultyEasy: 2, difficultyMedium: 3, difficultyHard: 1);

        assignment.DifficultyEasyCount.Should().Be(2);
        assignment.DifficultyMediumCount.Should().Be(3);
        assignment.DifficultyHardCount.Should().Be(1);
    }

    [TestMethod]
    public void Create_RejectsNegativeCount()
    {
        var act = () => Assignment.Create(
            "Title", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, null, null, null,
            difficultyHard: -1);

        act.Should().Throw<ArgumentException>();
    }

    [TestMethod]
    public void Update_RoundTrips_ChangedCounts()
    {
        var assignment = NewAssignment();

        assignment.Update("Title2", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, null, null, 50m, true,
            difficultyEasy: 1, difficultyMedium: 2, difficultyHard: 3);

        assignment.DifficultyEasyCount.Should().Be(1);
        assignment.DifficultyMediumCount.Should().Be(2);
        assignment.DifficultyHardCount.Should().Be(3);
    }

    [TestMethod]
    public void Update_RejectsNegativeCount()
    {
        var assignment = NewAssignment();

        var act = () => assignment.Update(
            "Title", null, AssignmentType.Digital, GradingFormat.TeacherGraded,
            TargetAudienceType.AllStudents, TopicId, null, null, 50m, true,
            difficultyMedium: -2);

        act.Should().Throw<ArgumentException>();
    }
}
