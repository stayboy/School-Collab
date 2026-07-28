using FluentAssertions;
using SchoolCollab.Settings.Core.Domain;
using SchoolCollab.Settings.Core.Domain.Exceptions;

namespace SchoolCollab.Settings.Tests.Unit.Domain;

/// <summary>
/// Pure domain tests for <see cref="EntityCodeRule.GenerateNext"/> / <see cref="EntityCodeSegment"/>
/// — spec §5.1. No DbContext required; these exercise the increment, rollover,
/// period-reset, and upper-limit logic directly.
/// </summary>
[TestClass]
public class EntityCodeRuleTests
{
    private static DateTimeOffset Now { get; } = new DateTimeOffset(2026, 7, 28, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The default student template: Fixed stamp "STU" + AlphanumericSequence A01.</summary>
    private static EntityCodeRule DefaultStudentRule() =>
        BuildRule(
            EntityCodeSegment.Fixed(0, "stamp", "STU"),
            EntityCodeSegment.Sequence(1, null, SegmentType.AlphanumericSequence, prefix: "A", minWidth: 2, upperLimit: "09"));

    private static EntityCodeRule BuildRule(params EntityCodeSegment[] segments)
    {
        var rule = EntityCodeRule.Create("STUDENT_CODE", "Student Code Template", null, isActive: true);
        foreach (var s in segments)
            rule.AddSegment(s);
        return rule;
    }

    [TestMethod]
    public void NonResetRule_GeneratesSequentialAlphanumericCodes()
    {
        var rule = DefaultStudentRule();

        rule.GenerateNext(Now).Should().Be("STUA01");
        rule.GenerateNext(Now).Should().Be("STUA02");
        rule.GenerateNext(Now).Should().Be("STUA03");
    }

    [TestMethod]
    public void AlphanumericSequence_RollsOverAtUpperLimit()
    {
        var rule = DefaultStudentRule();

        // A01 .. A09
        for (var i = 1; i <= 9; i++)
            rule.GenerateNext(Now).Should().Be($"STUA0{i}", "sequence {0} is still in the A series", i);

        // Rollover: A09 -> B01 -> B02
        rule.GenerateNext(Now).Should().Be("STUB01");
        rule.GenerateNext(Now).Should().Be("STUB02");
    }

    [TestMethod]
    public void AlphanumericSequence_RollsOverThroughZThenThrows()
    {
        // UpperLimit 01 → every numeric step rolls the prefix: A01, B01, ... Z01, then throw.
        var rule = BuildRule(
            EntityCodeSegment.Fixed(0, "stamp", "STF"),
            EntityCodeSegment.Sequence(1, null, SegmentType.AlphanumericSequence, prefix: "A", minWidth: 2, upperLimit: "01"));

        for (var i = 0; i < 26; i++)
            rule.GenerateNext(Now).Should().Be($"STF{(char)('A' + i)}01");

        var act = () => rule.GenerateNext(Now);
        act.Should().Throw<EntityCodeGenerationCollisionException>();
    }

    [TestMethod]
    public void NumericSequence_HitsUpperLimit_ThrowsCollision()
    {
        var rule = BuildRule(
            EntityCodeSegment.Fixed(0, "stamp", "STU"),
            EntityCodeSegment.Sequence(1, null, SegmentType.NumericSequence, minWidth: 2, upperLimit: "03"));

        rule.GenerateNext(Now).Should().Be("STU01");
        rule.GenerateNext(Now).Should().Be("STU02");
        rule.GenerateNext(Now).Should().Be("STU03");

        var act = () => rule.GenerateNext(Now);
        act.Should().Throw<EntityCodeGenerationCollisionException>("pure numeric cannot roll over");
    }

    [TestMethod]
    public void AlphabeticSequence_IncrementsAToBThenThrowsAtLimit()
    {
        var rule = BuildRule(
            EntityCodeSegment.Fixed(0, "stamp", "STU"),
            EntityCodeSegment.Sequence(1, null, SegmentType.AlphabeticSequence, upperLimit: "C"));

        rule.GenerateNext(Now).Should().Be("STUA", "first value is A, not B");
        rule.GenerateNext(Now).Should().Be("STUB");
        rule.GenerateNext(Now).Should().Be("STUC");

        var act = () => rule.GenerateNext(Now);
        act.Should().Throw<EntityCodeGenerationCollisionException>();
    }

    [TestMethod]
    public void YearlyReset_SequenceResetsOnNewYear()
    {
        var rule = BuildRule(
            EntityCodeSegment.Fixed(0, "stamp", "STU"),
            EntityCodeSegment.Sequence(1, null, SegmentType.AlphanumericSequence, prefix: "A", minWidth: 2, upperLimit: "09", resetPeriod: ResetPeriod.Yearly));

        var endOfYear = new DateTimeOffset(2026, 12, 31, 23, 0, 0, TimeSpan.Zero);
        var nextYear = new DateTimeOffset(2027, 1, 2, 9, 0, 0, TimeSpan.Zero);

        rule.GenerateNext(endOfYear).Should().Be("STUA01");
        rule.GenerateNext(nextYear).Should().Be("STUA01", "the yearly bucket changed so the sequence resets");
    }

    [TestMethod]
    public void MonthlyReset_SequenceResetsOnNewMonth()
    {
        var rule = BuildRule(
            EntityCodeSegment.Fixed(0, "stamp", "STU"),
            EntityCodeSegment.Sequence(1, null, SegmentType.AlphanumericSequence, prefix: "A", minWidth: 2, upperLimit: "09", resetPeriod: ResetPeriod.Monthly));

        rule.GenerateNext(new DateTimeOffset(2026, 7, 15, 9, 0, 0, TimeSpan.Zero)).Should().Be("STUA01");
        rule.GenerateNext(new DateTimeOffset(2026, 7, 31, 9, 0, 0, TimeSpan.Zero)).Should().Be("STUA02");
        rule.GenerateNext(new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero)).Should().Be("STUA01", "August is a new monthly bucket");
    }

    [TestMethod]
    public void QuarterlyReset_SequenceResetsOnNewQuarter()
    {
        var rule = BuildRule(
            EntityCodeSegment.Fixed(0, "stamp", "STU"),
            EntityCodeSegment.Sequence(1, null, SegmentType.AlphanumericSequence, prefix: "A", minWidth: 2, upperLimit: "09", resetPeriod: ResetPeriod.Quarterly));

        // Q3 = Jul-Sep. Oct 1 starts Q4.
        rule.GenerateNext(new DateTimeOffset(2026, 9, 30, 9, 0, 0, TimeSpan.Zero)).Should().Be("STUA01");
        rule.GenerateNext(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero)).Should().Be("STUA01", "October starts a new quarter (Q4)");
    }

    [TestMethod]
    public void PeriodBucketBoundary_DoesNotResetWithinSamePeriod()
    {
        var rule = BuildRule(
            EntityCodeSegment.Fixed(0, "stamp", "STU"),
            EntityCodeSegment.Sequence(1, null, SegmentType.AlphanumericSequence, prefix: "A", minWidth: 2, upperLimit: "09", resetPeriod: ResetPeriod.Yearly));

        rule.GenerateNext(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)).Should().Be("STUA01");
        rule.GenerateNext(new DateTimeOffset(2026, 12, 31, 23, 59, 59, TimeSpan.Zero)).Should().Be("STUA02", "still 2026 — same yearly bucket, no reset");
    }

    [TestMethod]
    public void FixedOnlySegment_ReturnsFixedText()
    {
        var rule = BuildRule(EntityCodeSegment.Fixed(0, "stamp", "STU"));

        rule.GenerateNext(Now).Should().Be("STU");
        rule.GenerateNext(Now).Should().Be("STU", "a fixed segment never changes");
    }

    [TestMethod]
    public void MixedFixedAndSequence_ConcatenatesInIndexOrder()
    {
        // Prefix "STU-" (index 0) + AlphanumericSequence (index 1) + fixed "-2026" (index 2)
        var rule = BuildRule(
            EntityCodeSegment.Fixed(0, "prefix", "STU-"),
            EntityCodeSegment.Sequence(1, "stamp", SegmentType.AlphanumericSequence, prefix: "A", minWidth: 2, upperLimit: "09"),
            EntityCodeSegment.Fixed(2, null, "-2026"));

        rule.GenerateNext(Now).Should().Be("STU-A01-2026");
        rule.GenerateNext(Now).Should().Be("STU-A02-2026");
    }

    [TestMethod]
    public void RuleWithNoSegments_Throws()
    {
        var rule = EntityCodeRule.Create("EMPTY_CODE", "Empty", null, isActive: true);

        var act = () => rule.GenerateNext(Now);
        act.Should().Throw<EntityCodeGenerationException>();
    }

    // ───────────────────────────────────────────────────────────────────
    // Tenant-override resolution tests (spec §4.12). These exercise
    // EntityCodeRule.GenerateNextWithOverrides against a freshly-built rule
    // — no DbContext, no ITenantProvider, no real tenant id required.
    // ───────────────────────────────────────────────────────────────────

    [TestMethod]
    public void GenerateNextWithOverrides_OverridesFixedText_AppliesAtRenderTime()
    {
        var rule = BuildRule(
            EntityCodeSegment.Fixed(0, "stamp", "STU"),
            EntityCodeSegment.Sequence(1, null, SegmentType.NumericSequence, prefix: "", minWidth: 2, upperLimit: "99"));

        var stamp = rule.Segments.First(s => s.Index == 0);
        var overrides = new Dictionary<Guid, IReadOnlyDictionary<OverrideField, string>>
        {
            [stamp.Id] = new Dictionary<OverrideField, string> { [OverrideField.FixedText] = "ABC" }
        };

        rule.GenerateNextWithOverrides(Now, overrides).Should().Be("ABC01");

        // The persisted segment is NOT mutated by the override path — a second
        // call without the override map should still render the original "STU".
        rule.GenerateNext(Now).Should().Be("STU02");
    }

    [TestMethod]
    public void GenerateNextWithOverrides_OverrideUnknownSegment_IsSilentlyIgnored()
    {
        var rule = BuildRule(
            EntityCodeSegment.Fixed(0, "stamp", "STU"),
            EntityCodeSegment.Sequence(1, null, SegmentType.NumericSequence, prefix: "", minWidth: 2, upperLimit: "99"));

        // A stale override pointing at a non-existent segment id (e.g. one
        // that was removed between when the override was saved and when the
        // generator ran) should NOT throw — the rule generates with the
        // remaining valid segments and the bad override is dropped.
        var staleOverrides = new Dictionary<Guid, IReadOnlyDictionary<OverrideField, string>>
        {
            [Guid.NewGuid()] = new Dictionary<OverrideField, string> { [OverrideField.FixedText] = "NOPE" }
        };

        rule.GenerateNextWithOverrides(Now, staleOverrides).Should().Be("STU01");
    }

    [TestMethod]
    public void GenerateNextWithOverrides_OverrideMinWidthForNumericSegment_Applies()
    {
        var rule = BuildRule(
            EntityCodeSegment.Sequence(0, null, SegmentType.NumericSequence, prefix: "", minWidth: 2, upperLimit: "99"));
        var numeric = rule.Segments.First();

        var overrides = new Dictionary<Guid, IReadOnlyDictionary<OverrideField, string>>
        {
            [numeric.Id] = new Dictionary<OverrideField, string> { [OverrideField.MinWidth] = "4" }
        };

        rule.GenerateNextWithOverrides(Now, overrides).Should().Be("0001");
    }

    [TestMethod]
    public void GenerateNextWithOverrides_OverrideMinWidthBelow1_IsIgnored()
    {
        // MinWidth must be >= 1; an override that fails the guard is dropped,
        // so the persisted MinWidth (2) stays in effect.
        var rule = BuildRule(
            EntityCodeSegment.Sequence(0, null, SegmentType.NumericSequence, prefix: "", minWidth: 2, upperLimit: "99"));
        var numeric = rule.Segments.First();

        var overrides = new Dictionary<Guid, IReadOnlyDictionary<OverrideField, string>>
        {
            [numeric.Id] = new Dictionary<OverrideField, string> { [OverrideField.MinWidth] = "0" }
        };

        rule.GenerateNextWithOverrides(Now, overrides).Should().Be("01");
    }

    [TestMethod]
    public void GenerateNextWithOverrides_OverrideResetPeriodToYearly_Applies()
    {
        var rule = BuildRule(
            EntityCodeSegment.Sequence(0, null, SegmentType.NumericSequence, prefix: "", minWidth: 2, upperLimit: "99"));
        var numeric = rule.Segments.First();

        // First call: 2026-07-28, sequence=01
        rule.GenerateNext(Now).Should().Be("01");

        // Override the reset period to Yearly. Then advance to 2027 — should reset to 01.
        var overrides = new Dictionary<Guid, IReadOnlyDictionary<OverrideField, string>>
        {
            [numeric.Id] = new Dictionary<OverrideField, string> { [OverrideField.ResetPeriod] = ((int)ResetPeriod.Yearly).ToString() }
        };
        var nextYear = new DateTimeOffset(2027, 1, 5, 9, 0, 0, TimeSpan.Zero);
        rule.GenerateNextWithOverrides(nextYear, overrides).Should().Be("01");
    }
}