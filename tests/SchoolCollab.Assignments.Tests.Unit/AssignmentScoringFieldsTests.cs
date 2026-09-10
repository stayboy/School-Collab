using FluentAssertions;
using SchoolCollab.Assignments.Core.Domain;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// WS-A3 / spec §3.3 + §7 Q4 — <see cref="Assignment"/> threads
/// <c>PassScore</c> + <c>MaxAttempts</c> through <c>Create</c> /
/// <c>Update</c>. Domain matrix in the <see cref="AssignmentTests"/>
/// convention: pure, no DbContext.
/// </summary>
[TestClass]
public class AssignmentScoringFieldsTests
{
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000010");

    private static Assignment CreateAssignment(
        decimal? passScore = null,
        int? maxAttempts = null,
        decimal? maxScore = null,
        GradingFormat grading = GradingFormat.AutoGraded) =>
        Assignment.Create("Title", null, AssignmentType.Digital, grading,
            TargetAudienceType.AllStudents, TopicId, null, null, maxScore, TeacherId,
            passScore: passScore, maxAttempts: maxAttempts);

    // ── PassScore / MaxAttempts round-trip on Create ────────────────────

    [TestMethod]
    public void Create_WithPassScoreAndMaxAttempts_StampsBoth()
    {
        var a = CreateAssignment(passScore: 80m, maxAttempts: 3, maxScore: 100m);

        a.PassScore.Should().Be(80m);
        a.MaxAttempts.Should().Be(3);
    }

    [TestMethod]
    public void Create_OmitsBoth_DefaultsToNull()
    {
        var a = CreateAssignment();

        a.PassScore.Should().BeNull();
        a.MaxAttempts.Should().BeNull();
    }

    [TestMethod]
    public void Create_PassScoreNullAndMaxScoreNull_NullBothFine()
    {
        var act = () => CreateAssignment(passScore: null, maxAttempts: null);

        act.Should().NotThrow();
    }

    // ── Create validation ───────────────────────────────────────────────

    [TestMethod]
    public void Create_PassScoreGreaterThanMaxScore_Throws()
    {
        var act = () => CreateAssignment(passScore: 101m, maxScore: 100m);

        act.Should().Throw<ArgumentException>()
            .WithMessage("Pass score must not exceed the max score.*");
    }

    [TestMethod]
    public void Create_MaxAttemptsZero_Throws()
    {
        var act = () => CreateAssignment(maxAttempts: 0);

        act.Should().Throw<ArgumentException>()
            .WithMessage("Max attempts must be at least 1.*");
    }

    [TestMethod]
    public void Create_MaxAttemptsNegative_Throws()
    {
        var act = () => CreateAssignment(maxAttempts: -1);

        act.Should().Throw<ArgumentException>()
            .WithMessage("Max attempts must be at least 1.*");
    }

    [TestMethod]
    public void Create_MaxAttemptsOne_Allowed()
    {
        var a = CreateAssignment(maxAttempts: 1);

        a.MaxAttempts.Should().Be(1);
    }

    // ── Update validation ───────────────────────────────────────────────

    [TestMethod]
    public void Update_PassScoreAndMaxAttempts_RoundTripsOnDraft()
    {
        var a = CreateAssignment();
        a.Update("T", null, AssignmentType.Digital, GradingFormat.AutoGraded,
            TargetAudienceType.AllStudents, TopicId, null, null, 100m, true,
            passScore: 75m, maxAttempts: 4);

        a.PassScore.Should().Be(75m);
        a.MaxAttempts.Should().Be(4);
    }

    [TestMethod]
    public void Update_PassScoreGreaterThanMaxScore_Throws()
    {
        var a = CreateAssignment(maxScore: 100m);
        var act = () => a.Update("T", null, AssignmentType.Digital, GradingFormat.AutoGraded,
            TargetAudienceType.AllStudents, TopicId, null, null, 100m, true,
            passScore: 101m, maxAttempts: 3);

        act.Should().Throw<ArgumentException>()
            .WithMessage("Pass score must not exceed the max score.*");
    }

    [TestMethod]
    public void Update_MaxAttemptsZero_Throws()
    {
        var a = CreateAssignment();
        var act = () => a.Update("T", null, AssignmentType.Digital, GradingFormat.AutoGraded,
            TargetAudienceType.AllStudents, TopicId, null, null, null, true,
            passScore: null, maxAttempts: 0);

        act.Should().Throw<ArgumentException>()
            .WithMessage("Max attempts must be at least 1.*");
    }

    [TestMethod]
    public void Update_PassScoreAndMaxAttempts_NullsAreFine()
    {
        var a = CreateAssignment(passScore: 80m, maxAttempts: 3);
        a.Update("T", null, AssignmentType.Digital, GradingFormat.AutoGraded,
            TargetAudienceType.AllStudents, TopicId, null, null, null, true,
            passScore: null, maxAttempts: null);

        a.PassScore.Should().BeNull();
        a.MaxAttempts.Should().BeNull();
    }
}
