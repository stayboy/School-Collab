using FluentAssertions;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Services;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// Pure-logic tests for <see cref="ScoringEngine"/> (WS-A3 / spec §3.3).
/// The full scoring matrix lives here — no handlers, no DbContext, no
/// DI. Direct <c>new ScoringEngine()</c> instances (the
/// <c>NotificationRecipientFilterTests</c> pure-logic style).
/// </summary>
[TestClass]
public class ScoringEngineTests
{
    private static ScoringEngine NewEngine() => new();

    private static readonly Guid Q1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Q2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Q3 = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Q4 = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static readonly Guid OptA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OptB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid OptC = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private static ScoringInput McInput(
        IReadOnlyList<ScoringQuestion>? questions = null,
        IReadOnlyList<ScoringAnswer>? answers = null,
        decimal? maxScore = null,
        decimal? passScore = null) =>
        new(GradingFormat.AutoGraded, maxScore, passScore,
            questions ?? new List<ScoringQuestion>(),
            answers ?? new List<ScoringAnswer>());

    // ── MC / TF (option-match) ───────────────────────────────────────────

    [TestMethod]
    public void Mc_CorrectOptionSelected_IsCorrect()
    {
        var engine = NewEngine();
        var input = McInput(
            questions: [new(Q1, OptA, null)],
            answers: [new(Q1, OptA, null)]);

        var result = engine.Score(input);

        result.Questions.Should().HaveCount(1);
        result.Questions[0].IsCorrect.Should().BeTrue();
    }

    [TestMethod]
    public void Mc_WrongOptionSelected_IsCorrectFalse()
    {
        var engine = NewEngine();
        var input = McInput(
            questions: [new(Q1, OptA, null)],
            answers: [new(Q1, OptB, null)]);

        var result = engine.Score(input);

        result.Questions[0].IsCorrect.Should().BeFalse();
    }

    [TestMethod]
    public void Mc_UnansweredOptionMatch_IsCorrectFalse()
    {
        // Auto-scorable with no answer row → IsCorrect false (no answer
        // cannot match).
        var engine = NewEngine();
        var input = McInput(
            questions: [new(Q1, OptA, null)],
            answers: []);

        var result = engine.Score(input);

        result.Questions[0].IsCorrect.Should().BeFalse();
    }

    [TestMethod]
    public void Mc_NullSelectedOptionId_NeverCorrect()
    {
        // Both-null trap guard: a null SelectedOptionId on an
        // option-match question is never correct.
        var engine = NewEngine();
        var input = McInput(
            questions: [new(Q1, OptA, null)],
            answers: [new(Q1, null, null)]);

        var result = engine.Score(input);

        result.Questions[0].IsCorrect.Should().BeFalse();
    }

    [TestMethod]
    public void Tf_CorrectAndIncorrect_RulesMatchMc()
    {
        var engine = NewEngine();
        // Correct
        var correct = engine.Score(McInput(
            questions: [new(Q1, OptA, null)],
            answers: [new(Q1, OptA, null)]));
        correct.Questions[0].IsCorrect.Should().BeTrue();

        // Incorrect
        var wrong = engine.Score(McInput(
            questions: [new(Q1, OptA, null)],
            answers: [new(Q1, OptB, null)]));
        wrong.Questions[0].IsCorrect.Should().BeFalse();
    }

    [TestMethod]
    public void Mc_WithNullCorrectOptionId_IsCorrectNullAndExcluded()
    {
        // Recorded adjustment: a never-marked-correct option-match
        // question is IsCorrect null + excluded from autoScorableCount.
        var engine = NewEngine();
        var input = McInput(
            questions:
            [
                new(Q1, null, null), // never-marked-correct MC
                new(Q2, OptA, null), // auto-scorable
            ],
            answers:
            [
                new(Q1, OptA, null), // would otherwise look "correct"
                new(Q2, OptA, null),
            ]);

        var result = engine.Score(input);

        result.Questions.Should().HaveCount(2);
        result.Questions[0].IsCorrect.Should().BeNull("null CorrectOptionId excludes from scoring (recorded adjustment)");
        result.Questions[1].IsCorrect.Should().BeTrue();
        // Q1 excluded → autoScorableCount = 1 → Q2 correct → raw score = 1
        result.Score.Should().Be(1m);
    }

    // ── ShortAnswer (text-match) ──────────────────────────────────────────

    [TestMethod]
    public void ShortAnswer_ExactMatch_IsCorrect()
    {
        var engine = NewEngine();
        var input = McInput(
            questions: [new(Q1, null, "Glucose")],
            answers: [new(Q1, null, "Glucose")]);

        var result = engine.Score(input);

        result.Questions[0].IsCorrect.Should().BeTrue();
    }

    [TestMethod]
    public void ShortAnswer_CaseInsensitiveMatch_IsCorrect()
    {
        var engine = NewEngine();
        var input = McInput(
            questions: [new(Q1, null, "Glucose")],
            answers: [new(Q1, null, "glucose")]);

        var result = engine.Score(input);

        result.Questions[0].IsCorrect.Should().BeTrue();
    }

    [TestMethod]
    public void ShortAnswer_WhitespaceCollapsed_IsCorrect()
    {
        var engine = NewEngine();
        var input = McInput(
            questions: [new(Q1, null, "a b")],
            answers: [new(Q1, null, "  a  b ")]);

        var result = engine.Score(input);

        result.Questions[0].IsCorrect.Should().BeTrue();
    }

    [TestMethod]
    public void ShortAnswer_Mismatch_IsCorrectFalse()
    {
        var engine = NewEngine();
        var input = McInput(
            questions: [new(Q1, null, "Glucose")],
            answers: [new(Q1, null, "Fructose")]);

        var result = engine.Score(input);

        result.Questions[0].IsCorrect.Should().BeFalse();
    }

    [TestMethod]
    public void ShortAnswer_NoModelAnswer_IsCorrectNullAndExcluded()
    {
        var engine = NewEngine();
        var input = McInput(
            questions: [new(Q1, null, null)],
            answers: [new(Q1, null, "anything")]);

        var result = engine.Score(input);

        result.Questions[0].IsCorrect.Should().BeNull();
    }

    [TestMethod]
    public void ShortAnswer_BlankModelAnswer_IsCorrectNullAndExcluded()
    {
        var engine = NewEngine();
        var input = McInput(
            questions: [new(Q1, null, "   ")],
            answers: [new(Q1, null, "anything")]);

        var result = engine.Score(input);

        result.Questions[0].IsCorrect.Should().BeNull();
    }

    [TestMethod]
    public void ShortAnswer_UnansweredModelAnswer_IsCorrectFalse()
    {
        // Auto-scorable with no answer row → IsCorrect false.
        var engine = NewEngine();
        var input = McInput(
            questions: [new(Q1, null, "Glucose")],
            answers: []);

        var result = engine.Score(input);

        result.Questions[0].IsCorrect.Should().BeFalse();
    }

    // ── TeacherGraded misuse guard ───────────────────────────────────────

    [TestMethod]
    public void TeacherGradedInput_Throws()
    {
        var engine = NewEngine();
        var act = () => engine.Score(new ScoringInput(
            GradingFormat.TeacherGraded, null, null,
            [], []));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("TeacherGraded never-scored*");
    }

    // ── Passed semantics (boundary >=) ───────────────────────────────────

    [TestMethod]
    public void Passed_NullPassScore_PassedIsNull()
    {
        var engine = NewEngine();
        var input = McInput(
            questions: [new(Q1, OptA, null)],
            answers: [new(Q1, OptA, null)],
            passScore: null);

        var result = engine.Score(input);

        result.Passed.Should().BeNull();
    }

    [TestMethod]
    public void Passed_ScoreEqualsPassScore_BoundaryIsPassed()
    {
        var engine = NewEngine();
        // 1 of 2 correct, passScore 50, maxScore 100 → 50 == 50 → Passed true
        var input = McInput(
            questions:
            [
                new(Q1, OptA, null),
                new(Q2, OptB, null),
            ],
            answers:
            [
                new(Q1, OptA, null), // correct
                new(Q2, OptA, null), // wrong
            ],
            maxScore: 100m,
            passScore: 50m);

        var result = engine.Score(input);

        result.Score.Should().Be(50m);
        result.Passed.Should().BeTrue("the >= boundary is binding — score == threshold passes");
    }

    [TestMethod]
    public void Passed_ScoreBelowPassScore_False()
    {
        var engine = NewEngine();
        // 1 of 2 correct, maxScore 100, passScore 51 → 50 < 51 → Passed false
        var input = McInput(
            questions:
            [
                new(Q1, OptA, null),
                new(Q2, OptB, null),
            ],
            answers:
            [
                new(Q1, OptA, null),
                new(Q2, OptA, null),
            ],
            maxScore: 100m,
            passScore: 51m);

        var result = engine.Score(input);

        result.Score.Should().Be(50m);
        result.Passed.Should().BeFalse();
    }

    // ── MaxScore scaling + rounding ─────────────────────────────────────

    [TestMethod]
    public void MaxScore_Null_ScoreIsRawCorrectCount()
    {
        var engine = NewEngine();
        var input = McInput(
            questions:
            [
                new(Q1, OptA, null),
                new(Q2, OptB, null),
                new(Q3, OptC, null),
            ],
            answers:
            [
                new(Q1, OptA, null),
                new(Q2, OptB, null),
                new(Q3, OptA, null), // wrong
            ]);

        var result = engine.Score(input);

        result.Score.Should().Be(2m, "MaxScore null → raw correct count");
    }

    [TestMethod]
    public void MaxScore_Set_ScalesAndRounds_AwayFromZero()
    {
        // 2 of 3 correct, MaxScore 100 → 66.666… → 66.67 (MidpointRounding.AwayFromZero)
        var engine = NewEngine();
        var input = McInput(
            questions:
            [
                new(Q1, OptA, null),
                new(Q2, OptB, null),
                new(Q3, OptC, null),
            ],
            answers:
            [
                new(Q1, OptA, null),
                new(Q2, OptB, null),
                new(Q3, OptA, null),
            ],
            maxScore: 100m);

        var result = engine.Score(input);

        result.Score.Should().Be(66.67m);
    }

    [TestMethod]
    public void MaxScore_Set_ZeroAutoScorable_ScoreIsZero()
    {
        // Recorded adjustment: division-by-zero guard → Score = 0.
        var engine = NewEngine();
        var input = McInput(
            questions: [new(Q1, null, null)], // not auto-scorable
            answers: [],
            maxScore: 100m);

        var result = engine.Score(input);

        result.Score.Should().Be(0m);
    }

    // ── Empty answers ────────────────────────────────────────────────────

    [TestMethod]
    public void EmptyAnswers_AllAutoScorableFalse()
    {
        var engine = NewEngine();
        var input = McInput(
            questions:
            [
                new(Q1, OptA, null),
                new(Q2, null, "Glucose"),
            ],
            answers: []);

        var result = engine.Score(input);

        result.Questions.Should().AllSatisfy(q => q.IsCorrect.Should().BeFalse());
        result.Score.Should().Be(0m);
    }

    [TestMethod]
    public void EmptyAnswers_PassScoreZero_PassedTrue()
    {
        var engine = NewEngine();
        var input = McInput(
            questions: [new(Q1, OptA, null)],
            answers: [],
            passScore: 0m);

        var result = engine.Score(input);

        result.Score.Should().Be(0m);
        result.Passed.Should().BeTrue("0 >= 0 → pass");
    }

    [TestMethod]
    public void EmptyAnswers_PassScoreAboveZero_PassedFalse()
    {
        var engine = NewEngine();
        var input = McInput(
            questions: [new(Q1, OptA, null)],
            answers: [],
            passScore: 1m);

        var result = engine.Score(input);

        result.Passed.Should().BeFalse();
    }

    // ── Duplicate answer question ids → first wins ──────────────────────

    [TestMethod]
    public void DuplicateAnswerQuestionId_FirstWins()
    {
        var engine = NewEngine();
        var input = McInput(
            questions: [new(Q1, OptA, null)],
            answers:
            [
                new(Q1, OptA, null), // correct
                new(Q1, OptB, null), // would otherwise overwrite
            ]);

        var result = engine.Score(input);

        result.Questions[0].IsCorrect.Should().BeTrue("first answer wins on duplicate QuestionId");
    }
}
