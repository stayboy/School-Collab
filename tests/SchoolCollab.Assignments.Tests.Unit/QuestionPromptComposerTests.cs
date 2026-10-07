using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Application.Helpers;
using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="QuestionPromptComposer"/> — the R3 deterministic prompt composition
/// (spec §6.1a PB-4). The narrative skeleton is the single predicate source for PB-5 (replace),
/// PB-6 (re-hydration) and QA-23 (refresh), so these lock the exact line set, the conditional
/// renderings, and the narrow parse's strictness (hand-written prose must never match).
/// </summary>
[TestClass]
public class QuestionPromptComposerTests
{
    private static QuestionPromptNarrativeInputs Inputs(
        IReadOnlyList<QuestionTypeDto>? types = null,
        IReadOnlyList<string>? resources = null,
        IReadOnlyList<int>? grades = null,
        int count = 10,
        int? easy = 4,
        int? medium = 4,
        int? hard = 2,
        IReadOnlyList<string>? strands = null,
        IReadOnlyList<string>? lessons = null) =>
        new(
            TopicName: "Photosynthesis",
            GradeLevels: grades ?? [3],
            QuestionCount: count,
            DifficultyEasy: easy,
            DifficultyMedium: medium,
            DifficultyHard: hard,
            Types: types ?? [QuestionTypeDto.MultipleChoice, QuestionTypeDto.ShortAnswer],
            ResourceNames: resources,
            StrandNames: strands,
            LessonNames: lessons);

    [TestMethod]
    public void Compose_EmittedLines_FollowTheNormativeSkeleton()
    {
        var narrative = QuestionPromptComposer.Compose(
            Inputs(resources: ["syllabus.pdf", "example.com/article"]));

        narrative.Should().Be(
            "Generate 10 questions for the subject \"Photosynthesis\".\n" +
            "Grade level: Grade 3.\n" +
            "Difficulty mix: 4 easy / 4 medium / 2 hard.\n" +
            "Include: Multiple choice, Short answer.\n" +
            "Grounded on: syllabus.pdf, example.com/article.");
    }

    [TestMethod]
    public void Compose_NoGradeTargets_RendersDashNeverOmitsTheLine()
    {
        var narrative = QuestionPromptComposer.Compose(Inputs(grades: []));

        narrative.Should().Contain("Grade level: —.");
    }

    [TestMethod]
    public void Compose_NoResources_OmitsTheGroundedLine()
    {
        var narrative = QuestionPromptComposer.Compose(Inputs(resources: null));

        narrative.Should().NotContain("Grounded on:");
        narrative.Should().EndWith("Include: Multiple choice, Short answer.");
    }

    [TestMethod]
    public void Compose_AllTypesOff_RendersBalancedMix()
    {
        var narrative = QuestionPromptComposer.Compose(Inputs(types: []));

        narrative.Should().Contain("Include: balanced mix.");
    }

    [TestMethod]
    public void Compose_AllDifficultyUnset_RendersZerosNotOmitted()
    {
        var narrative = QuestionPromptComposer.Compose(Inputs(easy: null, medium: null, hard: null));

        narrative.Should().Contain("Difficulty mix: 0 easy / 0 medium / 0 hard.");
    }

    [TestMethod]
    public void Compose_R4PickLines_AppendAfterTheResourceLine()
    {
        var narrative = QuestionPromptComposer.Compose(
            Inputs(resources: ["syllabus.pdf"], strands: ["Fractions"], lessons: ["Equivalent fractions"]));

        narrative.Should().EndWith(
            "Grounded on: syllabus.pdf.\n" +
            "Strands: Fractions.\n" +
            "Lessons: Equivalent fractions.");
    }

    [TestMethod]
    public void FormatGrades_ContiguousMoreThanTwo_CollapsesToARange()
    {
        QuestionPromptComposer.FormatGrades([4, 1, 3, 2]).Should().Be("Grades 1–4");
    }

    [TestMethod]
    public void FormatGrades_TwoGrades_AreCommaSeparated()
    {
        QuestionPromptComposer.FormatGrades([2, 1]).Should().Be("Grade 1, Grade 2");
    }

    [TestMethod]
    public void FormatGrades_NonContiguousThree_AreCommaSeparated()
    {
        QuestionPromptComposer.FormatGrades([1, 2, 5]).Should().Be("Grade 1, Grade 2, Grade 5");
    }

    [TestMethod]
    public void ComposeSummary_UsesTheSamePartsInline()
    {
        QuestionPromptComposer.ComposeSummary(Inputs()).Should()
            .Be("10 questions · 4 easy / 4 medium / 2 hard · Multiple choice, Short answer");
    }

    [TestMethod]
    public void ComposeSummary_NoDifficultyAndNoTypes_UsesDashesAndBalancedMix()
    {
        QuestionPromptComposer.ComposeSummary(Inputs(easy: null, medium: null, hard: null, types: []))
            .Should().Be("10 questions · — · Balanced mix");
    }

    [TestMethod]
    public void TryParse_ARoundTrippedNarrative_RecoversCountAndTypes()
    {
        var narrative = QuestionPromptComposer.Compose(Inputs());

        var matched = QuestionPromptComposer.TryParse(narrative, out var knobs);

        matched.Should().BeTrue();
        knobs.QuestionCount.Should().Be(10);
        knobs.Types.Should().Equal(QuestionTypeDto.MultipleChoice, QuestionTypeDto.ShortAnswer);
    }

    [TestMethod]
    public void TryParse_ARoundTrippedBalancedMix_RecoversNoTypes()
    {
        var narrative = QuestionPromptComposer.Compose(Inputs(types: []));

        QuestionPromptComposer.TryParse(narrative, out var knobs).Should().BeTrue();
        knobs.Types.Should().BeEmpty();
    }

    [TestMethod]
    public void TryParse_AcceptsR4ComposedText()
    {
        var narrative = QuestionPromptComposer.Compose(
            Inputs(resources: ["a.pdf"], strands: ["Fractions"], lessons: ["Halves"]));

        QuestionPromptComposer.IsTemplateMatch(narrative).Should().BeTrue(
            "R4 appends its pick lines to the same skeleton, so the parser must accept them");
    }

    [TestMethod]
    public void TryParse_AcceptsWindowsLineEndings()
    {
        var narrative = QuestionPromptComposer.Compose(Inputs()).Replace("\n", "\r\n");

        QuestionPromptComposer.IsTemplateMatch(narrative).Should().BeTrue();
    }

    [TestMethod]
    public void TryParse_HandWrittenProse_DoesNotMatch()
    {
        QuestionPromptComposer.IsTemplateMatch("10 mixed questions on fractions, 40% hard").Should().BeFalse();
    }

    [TestMethod]
    public void TryParse_AnEditedCountLine_StillMatchesBecauseTheShapeIsIntact()
    {
        var narrative = QuestionPromptComposer.Compose(Inputs()).Replace("Generate 10 questions", "Generate 7 questions");

        QuestionPromptComposer.TryParse(narrative, out var knobs).Should().BeTrue();
        knobs.QuestionCount.Should().Be(7);
    }

    [TestMethod]
    public void TryParse_MissingDifficultyLine_DoesNotMatch()
    {
        var narrative = QuestionPromptComposer.Compose(Inputs());
        var mangled = narrative.Replace("Difficulty mix: 4 easy / 4 medium / 2 hard.\n", string.Empty);

        QuestionPromptComposer.IsTemplateMatch(mangled).Should().BeFalse();
    }

    [TestMethod]
    public void TryParse_TrailingLine_DoesNotMatch()
    {
        var narrative = QuestionPromptComposer.Compose(Inputs()) + "\nAnd make them fun.";

        QuestionPromptComposer.IsTemplateMatch(narrative).Should().BeFalse();
    }

    [TestMethod]
    public void TryParse_TypesOutOfCanonicalOrder_DoNotMatch()
    {
        var narrative = QuestionPromptComposer.Compose(Inputs())
            .Replace("Include: Multiple choice, Short answer.", "Include: Short answer, Multiple choice.");

        QuestionPromptComposer.IsTemplateMatch(narrative).Should().BeFalse();
    }

    [TestMethod]
    public void TryParse_UnknownTypeName_DoesNotMatch()
    {
        var narrative = QuestionPromptComposer.Compose(Inputs())
            .Replace("Include: Multiple choice, Short answer.", "Include: MultipleChoice.");

        QuestionPromptComposer.IsTemplateMatch(narrative).Should().BeFalse();
    }

    [TestMethod]
    public void TryParse_EmptyOrNull_DoesNotMatch()
    {
        QuestionPromptComposer.IsTemplateMatch(null).Should().BeFalse();
        QuestionPromptComposer.IsTemplateMatch(string.Empty).Should().BeFalse();
        QuestionPromptComposer.IsTemplateMatch("   ").Should().BeFalse();
    }

    [TestMethod]
    public void TypeName_CarriesTheCanonicalReadableSpelling()
    {
        QuestionPromptComposer.TypeName(QuestionTypeDto.MultipleChoice).Should().Be("Multiple choice");
        QuestionPromptComposer.TypeName(QuestionTypeDto.TrueFalse).Should().Be("True / false");
        QuestionPromptComposer.TypeName(QuestionTypeDto.ShortAnswer).Should().Be("Short answer");
    }
}
