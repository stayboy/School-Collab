using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Core.Domain;
using SchoolCollab.Assignments.Core.Services;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// Round notification-templates-b2c (spec §3.1.1 / §8, Q15/Q67) — the pure applicability matcher.
/// Every test here is discriminating against a plain "pick the first candidate" implementation: the
/// intersect matrix, the missing-dimension rule, specificity measured in <b>distinct dimensions</b>
/// (not row count), the tie-break order, and the built-in default when nothing applies.
/// </summary>
[TestClass]
public class NotificationTemplateEvaluatorTests
{
    private static readonly Guid GradeA = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid GradeB = Guid.Parse("00000000-0000-0000-0000-0000000000a2");
    private static readonly Guid StreamA = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
    private static readonly Guid GroupA = Guid.Parse("00000000-0000-0000-0000-0000000000c1");
    private static readonly Guid TopicA = Guid.Parse("00000000-0000-0000-0000-0000000000d1");

    private static readonly DateTimeOffset Oldest = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Newer = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The assignment under test: grade A, stream A, group A, topic A, type Digital.</summary>
    private static AssignmentTemplateContext Dimensions(
        bool withStream = true, bool withGroup = true, Guid? topic = null, AssignmentType type = AssignmentType.Digital) =>
        AssignmentTemplateContext.Create(
            [GradeA],
            withStream ? [StreamA] : [],
            withGroup ? [GroupA] : [],
            topicId: topic ?? TopicA,
            assignmentType: type);

    private static NotificationTemplateCandidate Candidate(
        string body = "body",
        Guid? id = null,
        DateTimeOffset? createdAt = null,
        params NotificationTemplateConstraint[] constraints) =>
        new(id ?? Guid.NewGuid(), $"subject {body}", body, constraints, createdAt ?? Oldest);

    private static NotificationTemplateConstraint Constraint(string dimension, Guid value) =>
        new(dimension, value.ToString("D"));

    private static NotificationTemplateConstraint Constraint(string dimension, string value) => new(dimension, value);

    // ── per-dimension intersect ────────────────────────────────────────────────────────────

    [TestMethod]
    public void EveryDimension_IntersectsOnTheAssignmentsOwnValues()
    {
        var grade = Constraint(TemplateConstraintDimensions.GradeLevel, GradeA);
        var stream = Constraint(TemplateConstraintDimensions.Stream, StreamA);
        var group = Constraint(TemplateConstraintDimensions.ActivityGroup, GroupA);
        var subject = Constraint(TemplateConstraintDimensions.Subject, TopicA);
        var type = Constraint(TemplateConstraintDimensions.AssignmentType, "Digital");

        foreach (var constraint in new[] { grade, stream, group, subject, type })
        {
            var candidate = Candidate(constraints: constraint);
            NotificationTemplateEvaluator.IsApplicable(candidate, Dimensions()).Should().BeTrue(
                $"a {constraint.Dimension} constraint on one of the assignment's own values applies");
            NotificationTemplateEvaluator.Select([candidate], Dimensions()).Should().NotBeNull();
        }
    }

    [TestMethod]
    public void EveryDimension_DoesNotIntersectOnAForeignValue()
    {
        var foreign = new[]
        {
            Constraint(TemplateConstraintDimensions.GradeLevel, GradeB),
            Constraint(TemplateConstraintDimensions.Stream, Guid.NewGuid()),
            Constraint(TemplateConstraintDimensions.ActivityGroup, Guid.NewGuid()),
            Constraint(TemplateConstraintDimensions.Subject, Guid.NewGuid()),
            Constraint(TemplateConstraintDimensions.AssignmentType, "Manual"),
        };

        foreach (var constraint in foreign)
        {
            var candidate = Candidate(constraints: constraint);
            NotificationTemplateEvaluator.IsApplicable(candidate, Dimensions()).Should().BeFalse(
                $"a {constraint.Dimension} constraint on a value the assignment does not carry does not apply");
        }
    }

    [TestMethod]
    public void OneOfSeveralValues_OnTheSameDimension_Intersects()
    {
        // "Grade A OR Grade B" on one dimension: the assignment is in grade A, so it applies.
        var candidate = Candidate(constraints:
        [
            Constraint(TemplateConstraintDimensions.GradeLevel, GradeB),
            Constraint(TemplateConstraintDimensions.GradeLevel, GradeA),
        ]);

        NotificationTemplateEvaluator.IsApplicable(candidate, Dimensions()).Should().BeTrue();
    }

    [TestMethod]
    public void EveryConstraintMustIntersect_NotJustOne()
    {
        // Grade matches, stream does not — the "every constrained dimension" rule.
        var candidate = Candidate(constraints:
        [
            Constraint(TemplateConstraintDimensions.GradeLevel, GradeA),
            Constraint(TemplateConstraintDimensions.Stream, Guid.NewGuid()),
        ]);

        NotificationTemplateEvaluator.IsApplicable(candidate, Dimensions()).Should().BeFalse();
    }

    [TestMethod]
    public void AConstrainedDimensionTheAssignmentLacks_IsNotApplicable()
    {
        // The assignment has no stream target at all — a stream-constrained template cannot apply.
        var candidate = Candidate(constraints: [Constraint(TemplateConstraintDimensions.Stream, StreamA)]);

        NotificationTemplateEvaluator.IsApplicable(candidate, Dimensions(withStream: false))
            .Should().BeFalse("a constrained dimension the assignment lacks ⇒ not applicable");
    }

    [TestMethod]
    public void AnUnknownDimension_IsNotApplicable_AndNeverThrows()
    {
        // Risk recorded in the round doc: an unrecognised dimension must degrade, not blow up.
        var candidate = Candidate(constraints: [Constraint("SomethingNew", "x")]);

        NotificationTemplateEvaluator.IsApplicable(candidate, Dimensions()).Should().BeFalse();
        NotificationTemplateEvaluator.Select([candidate], Dimensions()).Should().BeNull();
    }

    [TestMethod]
    public void AnUnconstrainedCandidate_IsAlwaysApplicable()
    {
        NotificationTemplateEvaluator.IsApplicable(Candidate(), Dimensions()).Should().BeTrue(
            "the empty constraint set IS the tenant default");
        NotificationTemplateEvaluator.IsApplicable(Candidate(), AssignmentTemplateContext.Empty).Should().BeTrue();
    }

    // ── specificity = distinct constrained dimensions ─────────────────────────────────────

    [TestMethod]
    public void Specificity_CountsDistinctDimensions_NotRows()
    {
        var oneDimensionThreeValues = Candidate(constraints:
        [
            Constraint(TemplateConstraintDimensions.GradeLevel, GradeA),
            Constraint(TemplateConstraintDimensions.GradeLevel, GradeB),
            Constraint(TemplateConstraintDimensions.GradeLevel, Guid.NewGuid()),
        ]);

        NotificationTemplateEvaluator.Specificity(oneDimensionThreeValues).Should().Be(1);
    }

    [TestMethod]
    public void OneDimensionWithThreeValues_LosesToTwoDimensions()
    {
        // The discriminating case the round exists for: three GradeLevel rows are ONE dimension, so
        // the two-dimension candidate outranks them — a row-count implementation picks the wrong one.
        var wideSingleDimension = Candidate("single", constraints:
        [
            Constraint(TemplateConstraintDimensions.GradeLevel, GradeA),
            Constraint(TemplateConstraintDimensions.GradeLevel, GradeB),
            Constraint(TemplateConstraintDimensions.GradeLevel, Guid.NewGuid()),
        ]);
        var twoDimensions = Candidate("two", constraints:
        [
            Constraint(TemplateConstraintDimensions.GradeLevel, GradeA),
            Constraint(TemplateConstraintDimensions.AssignmentType, "Digital"),
        ]);

        var winner = NotificationTemplateEvaluator.Select([wideSingleDimension, twoDimensions], Dimensions());

        winner.Should().Be(twoDimensions);
    }

    [TestMethod]
    public void Rank_OrdersMostSpecificFirst_AndIncludesOnlyApplicableCandidates()
    {
        var unconstrained = Candidate("default");
        var oneDimension = Candidate("one", constraints: [Constraint(TemplateConstraintDimensions.GradeLevel, GradeA)]);
        var twoDimensions = Candidate("two", constraints:
        [
            Constraint(TemplateConstraintDimensions.GradeLevel, GradeA),
            Constraint(TemplateConstraintDimensions.Subject, TopicA),
        ]);
        var foreign = Candidate("foreign", constraints: [Constraint(TemplateConstraintDimensions.GradeLevel, GradeB)]);

        var ranked = NotificationTemplateEvaluator.Rank(
            [unconstrained, oneDimension, twoDimensions, foreign], Dimensions());

        ranked.Should().ContainInOrder(twoDimensions, oneDimension, unconstrained);
        ranked.Should().NotContain(foreign);
    }

    // ── ties (Q67) ────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Tie_OnSpecificity_PicksTheOldestCreatedAt()
    {
        var newer = Candidate("newer", createdAt: Newer,
            constraints: [Constraint(TemplateConstraintDimensions.GradeLevel, GradeA)]);
        var oldest = Candidate("oldest", createdAt: Oldest,
            constraints: [Constraint(TemplateConstraintDimensions.GradeLevel, GradeA)]);

        var winner = NotificationTemplateEvaluator.Select([newer, oldest], Dimensions());

        winner.Should().Be(oldest, "Q67: UpdatedAt moves on edit, so the tie-break is CreatedAt");
    }

    [TestMethod]
    public void TieAtTheTopSpecificity_FallsBackToTheTenantDefault()
    {
        // spec §3.7 / Q67: "most specific match, tie → tenant default". Two equally specific
        // constrained candidates are ambiguous, so the tenant-wide default wins outright — even
        // though it is neither the oldest row nor the most specific one. Without the fallback the
        // oldest constrained row wins silently, which the spec forbids.
        var tenantDefault = Candidate("default", createdAt: Newer);
        var olderConstrained = Candidate("older constrained", createdAt: Oldest,
            constraints: [Constraint(TemplateConstraintDimensions.GradeLevel, GradeA)]);
        var newerConstrained = Candidate("newer constrained", createdAt: Newer,
            constraints: [Constraint(TemplateConstraintDimensions.GradeLevel, GradeA)]);

        var winner = NotificationTemplateEvaluator.Select(
            [olderConstrained, newerConstrained, tenantDefault], Dimensions());

        winner.Should().Be(tenantDefault,
            "an equal-specificity tie resolves to the tenant default (spec §3.7), not an arbitrary constrained row");

        NotificationTemplateEvaluator.Rank([olderConstrained, newerConstrained, tenantDefault], Dimensions())
            .Should().StartWith(tenantDefault, "the publish dialog pre-selects the first ranked candidate");
    }

    [TestMethod]
    public void TieAtTheTopSpecificity_WithNoTenantDefault_PicksTheOldest()
    {
        // the no-default case keeps the CreatedAt tie-break (§3.7 falls back to the oldest row).
        var older = Candidate("older", createdAt: Oldest,
            constraints: [Constraint(TemplateConstraintDimensions.GradeLevel, GradeA)]);
        var newer = Candidate("newer", createdAt: Newer,
            constraints: [Constraint(TemplateConstraintDimensions.GradeLevel, GradeA)]);

        NotificationTemplateEvaluator.Select([newer, older], Dimensions()).Should().Be(older);
    }

    [TestMethod]
    public void TieBetweenTwoDefaults_PutsTheDefaultFirst_ThenTheOldest()
    {
        // Two unconstrained rows coexist only under the handler's accepted concurrent-create race;
        // among them the stated order is default-first then oldest CreatedAt, and both resolve to the
        // older row because a default is specificity 0.
        var newerDefault = Candidate("newer default", createdAt: Newer);
        var oldestDefault = Candidate("oldest default", createdAt: Oldest);

        NotificationTemplateEvaluator.Specificity(newerDefault).Should().Be(0);
        NotificationTemplateEvaluator.Select([newerDefault, oldestDefault], Dimensions())
            .Should().Be(oldestDefault);

        var ranked = NotificationTemplateEvaluator.Rank([newerDefault, oldestDefault], Dimensions());
        ranked.Should().ContainInOrder(oldestDefault, newerDefault);
    }

    [TestMethod]
    public void Tie_OnSpecificityAndCreatedAt_IsBrokenDeterministicallyByTemplateId()
    {
        var low = Guid.Parse("00000000-0000-0000-0000-00000000000a");
        var high = Guid.Parse("00000000-0000-0000-0000-00000000000b");

        var ranked = NotificationTemplateEvaluator.Rank(
            [Candidate("high", id: high), Candidate("low", id: low)], Dimensions());

        ranked.Should().ContainInOrder(ranked.OrderBy(c => c.TemplateId));
    }

    // ── the built-in default ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void NoCandidates_SelectsNothing_SoTheBuiltInDefaultRenders()
    {
        NotificationTemplateEvaluator.Select([], Dimensions()).Should().BeNull();
        NotificationTemplateEvaluator.Rank([], Dimensions()).Should().BeEmpty();
    }

    [TestMethod]
    public void NoApplicableCandidate_SelectsNothing_EvenWhenOthersExist()
    {
        var foreign = Candidate(constraints: [Constraint(TemplateConstraintDimensions.GradeLevel, GradeB)]);

        NotificationTemplateEvaluator.Select([foreign], Dimensions()).Should().BeNull();
    }

    [TestMethod]
    public void MatchingIsCaseInsensitiveAndTrimmed_ForValueStrings()
    {
        var candidate = Candidate(constraints: [Constraint(TemplateConstraintDimensions.AssignmentType, " digital ")]);

        NotificationTemplateEvaluator.IsApplicable(candidate, Dimensions()).Should().BeTrue();
    }
}
