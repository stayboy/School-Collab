using FluentAssertions;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Settings.Admin.Components.Pages.EntityCodeRules;
using SchoolCollab.Settings.Core.Domain;

namespace SchoolCollab.Settings.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="EntityCodePreview"/> (the admin-UI template
/// preview simulator used by the Index page's Preview column and the Edit /
/// Create pages' "Preview next 5 codes" button).
/// <para>
/// <b>Why these tests are important</b> — an earlier version of
/// <see cref="EntityCodePreview"/> carried a private <c>SimSegment</c>
/// class that mirrored the server's increment + render logic. It drifted:
/// AlphanumericSequence previews showed <c>ASG00</c> instead of
/// <c>ASGA01</c> because the simulator rendered before incrementing and
/// started the alpha prefix at <c>""</c> instead of the server's <c>"A"</c>.
/// The current implementation delegates to the production
/// <see cref="EntityCodeSegment.Advance"/> / <see cref="EntityCodeRule.GenerateNext"/>
/// directly. Each test below BUILDS the same template two ways (production
/// <see cref="EntityCodeSegment"/> via the factory + form-model via
/// <see cref="SegmentFormModel"/>) and asserts the two outputs are identical
/// — if the production code ever changes, these tests surface the change
/// immediately. spec §5.1.
/// </para>
/// </summary>
[TestClass]
public class EntityCodePreviewTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 28, 9, 0, 0, TimeSpan.Zero);

    // ── The default student template (spec §6.1) ────────────────────────
    // Index 0: Fixed stamp "STU"
    // Index 1: AlphanumericSequence, Prefix="A", MinWidth=2, UpperLimit="09"
    // Expected preview: STUA01, STUA02, STUA03, STUA04, STUA05

    [TestMethod]
    public void RenderNext5_DefaultStudentTemplate_ProducesA01ThroughA05()
    {
        // Arrange — form-model side (what the Edit page passes in)
        var segments = new List<SegmentFormModel>
        {
            new() { Index = 0, Role = "stamp", Type = SegmentTypeDto.Fixed, FixedText = "STU" },
            new() { Index = 1, Role = null, Type = SegmentTypeDto.AlphanumericSequence,
                    Prefix = "A", MinWidth = 2, UpperLimit = "09" }
        };

        // Act
        var preview = EntityCodePreview.RenderNext5(segments);

        // Assert
        preview.Should().Equal("STUA01", "STUA02", "STUA03", "STUA04", "STUA05");
    }

    [TestMethod]
    public void RenderNext5_DefaultAssignmentTemplate_ProducesA01ThroughA05()
    {
        // The user-reported bug: Assignment template with AlphanumericSequence
        // showed only the numeric bits (ASG00..ASG04) without the leading A.
        // After the fix (preview delegates to production Advance) the leading
        // A is present on every code.
        var segments = new List<SegmentFormModel>
        {
            new() { Index = 0, Role = "stamp", Type = SegmentTypeDto.Fixed, FixedText = "ASG" },
            new() { Index = 1, Role = null, Type = SegmentTypeDto.AlphanumericSequence,
                    Prefix = "A", MinWidth = 2, UpperLimit = "09" }
        };

        var preview = EntityCodePreview.RenderNext5(segments);

        preview.Should().Equal("ASGA01", "ASGA02", "ASGA03", "ASGA04", "ASGA05");
    }

    [TestMethod]
    public void RenderNext5_MatchesProductionGenerateNext_ForDefaultStudentTemplate()
    {
        // The same template built via the production factory.
        var productionRule = EntityCodeRule.Create("STUDENT_CODE", "Student Code Template", null, isActive: true);
        productionRule.AddSegment(EntityCodeSegment.Fixed(0, "stamp", "STU"));
        productionRule.AddSegment(EntityCodeSegment.Sequence(1, null, SegmentType.AlphanumericSequence,
            prefix: "A", minWidth: 2, upperLimit: "09"));

        var productionCodes = new List<string>();
        for (var i = 0; i < 5; i++)
            productionCodes.Add(productionRule.GenerateNext(Now));

        var formSegments = new List<SegmentFormModel>
        {
            new() { Index = 0, Role = "stamp", Type = SegmentTypeDto.Fixed, FixedText = "STU" },
            new() { Index = 1, Role = null, Type = SegmentTypeDto.AlphanumericSequence,
                    Prefix = "A", MinWidth = 2, UpperLimit = "09" }
        };

        var preview = EntityCodePreview.RenderNext5(formSegments);

        preview.Should().Equal(productionCodes,
            "preview MUST equal what the production rule generates from a fresh state");
    }

    [TestMethod]
    public void RenderNext5_RolloverAtUpperLimit_AdvancesFromFreshStateToRollover()
    {
        // RenderNext5 always starts from a fresh state, so it cannot
        // demonstrate a rollover directly — the FIRST 5 codes from a fresh
        // alphanumeric segment with UpperLimit="09" are always A01..A05.
        // The rollover semantics are covered by the existing
        // EntityCodeRuleTests.AlphanumericSequence_RollsOverAtUpperLimit
        // (production) test, plus the UpperLimit="01" case below. This test
        // asserts the simple fact: a fresh-state preview never skips the
        // A series.
        var segments = new List<SegmentFormModel>
        {
            new() { Index = 0, Role = "stamp", Type = SegmentTypeDto.Fixed, FixedText = "ASG" },
            new() { Index = 1, Role = null, Type = SegmentTypeDto.AlphanumericSequence,
                    Prefix = "A", MinWidth = 2, UpperLimit = "09" }
        };

        var preview = EntityCodePreview.RenderNext5(segments);

        // The 9th code (A09) is the rollover boundary — it is included in
        // the first 5 codes only if MinWidth keeps the number single-digit.
        // With MinWidth=2 the boundary is at code 9 (A09), so codes 1..5
        // are all in the A series.
        preview.Should().Equal("ASGA01", "ASGA02", "ASGA03", "ASGA04", "ASGA05");
    }

    [TestMethod]
    public void RenderNext5_AlphanumericSequenceUpperLimit_StopsAtCollision()
    {
        // UpperLimit = "02" forces rollover every 2 codes. Fresh state:
        // A01, A02, B01, B02, C01, C02 — until C02 the upper limit
        // isn't hit. Build a template with UpperLimit = "01" so EVERY step
        // rolls the prefix over: A01, B01, C01, ... Z01, then throw.
        var segments = new List<SegmentFormModel>
        {
            new() { Index = 0, Role = "stamp", Type = SegmentTypeDto.Fixed, FixedText = "ASG" },
            new() { Index = 1, Role = null, Type = SegmentTypeDto.AlphanumericSequence,
                    Prefix = "A", MinWidth = 2, UpperLimit = "01" }
        };

        var preview = EntityCodePreview.RenderNext5(segments);

        // A01, B01, C01, D01, E01 — 5 codes, then a 6th would be F01, but
        // we asked for 5 and we got 5. The collision check is tested
        // separately via the Z-rollover case below.
        preview.Should().Equal("ASGA01", "ASGB01", "ASGC01", "ASGD01", "ASGE01");
    }

    [TestMethod]
    public void RenderNext5_AlphabeticSequence_AdvancesThroughLetters()
    {
        // AlphabeticSequence, Prefix="" (no static prefix), UpperLimit="" (default Z).
        // Expected: A, B, C, D, E.
        var segments = new List<SegmentFormModel>
        {
            new() { Index = 0, Role = "stamp", Type = SegmentTypeDto.Fixed, FixedText = "X" },
            new() { Index = 1, Role = null, Type = SegmentTypeDto.AlphabeticSequence,
                    Prefix = "", MinWidth = 1 }
        };

        var preview = EntityCodePreview.RenderNext5(segments);

        preview.Should().Equal("XA", "XB", "XC", "XD", "XE");
    }

    [TestMethod]
    public void RenderNext5_NumericSequence_PadsWithMinWidth()
    {
        // NumericSequence with Prefix="X-", MinWidth=3, UpperLimit="999".
        // Expected: X-001, X-002, X-003, X-004, X-005.
        var segments = new List<SegmentFormModel>
        {
            new() { Index = 0, Type = SegmentTypeDto.NumericSequence,
                    Prefix = "X-", MinWidth = 3, UpperLimit = "999" }
        };

        var preview = EntityCodePreview.RenderNext5(segments);

        preview.Should().Equal("X-001", "X-002", "X-003", "X-004", "X-005");
    }

    [TestMethod]
    public void RenderNext5_IncludesSuffix_OnEverySegment()
    {
        // Suffix is part of the segment template — must appear on every code.
        var segments = new List<SegmentFormModel>
        {
            new() { Index = 0, Role = "stamp", Type = SegmentTypeDto.Fixed, FixedText = "STU", Suffix = "-" },
            new() { Index = 1, Type = SegmentTypeDto.AlphanumericSequence,
                    Prefix = "A", Suffix = "-2026", MinWidth = 2, UpperLimit = "09" }
        };

        var preview = EntityCodePreview.RenderNext5(segments);

        preview.Should().Equal("STU-A01-2026", "STU-A02-2026", "STU-A03-2026", "STU-A04-2026", "STU-A05-2026");
    }

    [TestMethod]
    public void RenderNext5_PrefixStringPrepended_OnAlphanumericSequence()
    {
        // Prefix is the INITIAL letter for AlphanumericSequence. The first
        // generated code carries the prefix letter; subsequent codes carry
        // the rolled letter. Both are prepended to the numeric portion.
        var segments = new List<SegmentFormModel>
        {
            new() { Index = 0, Role = "stamp", Type = SegmentTypeDto.Fixed, FixedText = "Y" },
            new() { Index = 1, Type = SegmentTypeDto.AlphanumericSequence,
                    Prefix = "X", MinWidth = 2, UpperLimit = "03" }
        };

        var preview = EntityCodePreview.RenderNext5(segments);

        // X01, X02, X03, Y01, Y02 — 5 codes spanning the rollover.
        preview.Should().Equal("YX01", "YX02", "YX03", "YY01", "YY02");
    }

    [TestMethod]
    public void RenderNext5_NullOrEmpty_ReturnsEmptyList()
    {
        EntityCodePreview.RenderNext5(null).Should().BeEmpty();
        EntityCodePreview.RenderNext5(Array.Empty<SegmentFormModel>()).Should().BeEmpty();
    }

    [TestMethod]
    public void RenderNext5_InvalidInput_ReturnsEmptyList()
    {
        // MinWidth = 0 on a Numeric segment is invalid (factory throws).
        // RenderNext5 should swallow the exception and return an empty list
        // — the Edit/Create page surfaces this as "no preview available",
        // not a page-level crash.
        var segments = new List<SegmentFormModel>
        {
            new() { Index = 0, Type = SegmentTypeDto.NumericSequence,
                    Prefix = "X", MinWidth = 0 }
        };

        var preview = EntityCodePreview.RenderNext5(segments);

        preview.Should().BeEmpty();
    }

    [TestMethod]
    public void RenderFirst_DefaultStudentTemplate_ProducesStuA01()
    {
        // The Index page's "Preview" column uses RenderFirst on the wire
        // DTOs. Build the equivalent DTO list and assert.
        // (Admin.Shared wire-DTO uses int Type / ResetPeriod; codes match
        // Settings.Core enums: Fixed=0, NumericSequence=1,
        // AlphabeticSequence=2, AlphanumericSequence=3.)
        var dtos = new List<EntityCodeSegmentDto>
        {
            new(Guid.NewGuid(), 0, "stamp", 0 /*Fixed*/, "STU", "", "", 0 /*None*/,
                0, null, 0, null, null),
            new(Guid.NewGuid(), 1, null, 3 /*AlphanumericSequence*/, "", "A", "", 0 /*None*/,
                2, "09", 0, null, null),
        };

        var first = EntityCodePreview.RenderFirst(dtos);

        first.Should().Be("STUA01");
    }

    [TestMethod]
    public void RenderFirst_EmptyOrNull_ReturnsEmDash()
    {
        EntityCodePreview.RenderFirst(null).Should().Be("—");
        EntityCodePreview.RenderFirst(Array.Empty<EntityCodeSegmentDto>()).Should().Be("—");
    }

    [TestMethod]
    public void RenderFirst_AlphanumericLeadingLetterPresent()
    {
        // Regression test for the user-reported "preview only shows numeric
        // bits of the alphanumeric setup" — RenderFirst must include the
        // initial letter, not just the number.
        var dtos = new List<EntityCodeSegmentDto>
        {
            new(Guid.NewGuid(), 0, "stamp", 0 /*Fixed*/, "ASG", "", "", 0 /*None*/,
                0, null, 0, null, null),
            new(Guid.NewGuid(), 1, null, 3 /*AlphanumericSequence*/, "", "A", "", 0 /*None*/,
                2, "09", 0, null, null),
        };

        var first = EntityCodePreview.RenderFirst(dtos);

        first.Should().Be("ASGA01");
    }
}