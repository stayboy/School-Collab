using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.AI.Abstractions;
using SchoolCollab.Assignments.Application.Helpers;
using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="GeneratedQuestionMapper"/> (WS-B2 / spec §3.4
/// line 73) — maps a generated AI <see cref="GeneratedQuestionDto"/> onto the
/// wire <see cref="NewQuestionDto"/> shape used to stage a questions draft.
/// Mirrors the wizard's append path (via <see cref="QuestionEditorRow.FromGenerated"/>)
/// so the staged draft and the appended question rows stay in lockstep.
/// </summary>
[TestClass]
public class GeneratedQuestionMapperTests
{
    [TestMethod]
    public void Maps_MultipleChoiceWithOptions()
    {
        var dto = new GeneratedQuestionDto(
            Text: "Which organ pumps blood?",
            Type: GeneratedQuestionType.MultipleChoice,
            Options: new[]
            {
                new GeneratedQuestionOptionDto("Heart", IsCorrect: true),
                new GeneratedQuestionOptionDto("Lungs"),
                new GeneratedQuestionOptionDto("Kidneys"),
            });

        var result = GeneratedQuestionMapper.ToNewQuestionDto(dto);

        result.QuestionText.Should().Be("Which organ pumps blood?");
        result.QuestionType.Should().Be(QuestionTypeDto.MultipleChoice);
        result.QuestionText.Should().NotBeEmpty();
        result.Options.Should().NotBeNull();
        result.Options!.Should().HaveCount(3);
        result.Options[0].OptionText.Should().Be("Heart");
        result.Options[0].IsCorrect.Should().BeTrue("the single IsCorrect option maps to true");
        result.Options[1].OptionText.Should().Be("Lungs");
        result.Options[1].IsCorrect.Should().BeFalse();
        result.Options[2].OptionText.Should().Be("Kidneys");
        result.Options[2].IsCorrect.Should().BeFalse();
    }

    [TestMethod]
    public void Maps_TrueFalse_Canonicalized()
    {
        var dto = new GeneratedQuestionDto(
            Text: "Photosynthesis requires sunlight.",
            Type: GeneratedQuestionType.TrueFalse,
            Options: new[]
            {
                new GeneratedQuestionOptionDto("True", IsCorrect: true),
                new GeneratedQuestionOptionDto("False"),
            });

        var result = GeneratedQuestionMapper.ToNewQuestionDto(dto);

        result.QuestionText.Should().Be("Photosynthesis requires sunlight.");
        result.QuestionType.Should().Be(QuestionTypeDto.TrueFalse);
        result.Options.Should().NotBeNull();
        result.Options!.Should().HaveCount(2);
        result.Options[0].OptionText.Should().Be("True");
        result.Options[1].OptionText.Should().Be("False");
        result.Options[0].IsCorrect.Should().BeTrue("the canonical True option carries correctness");
        result.Options[1].IsCorrect.Should().BeFalse();
    }

    [TestMethod]
    public void Maps_ShortAnswerWithModelAnswer()
    {
        var dto = new GeneratedQuestionDto(
            Text: "Name the main product of photosynthesis.",
            Type: GeneratedQuestionType.ShortAnswer,
            Options: null,
            ModelAnswer: "Glucose");

        var result = GeneratedQuestionMapper.ToNewQuestionDto(dto);

        result.QuestionText.Should().Be("Name the main product of photosynthesis.");
        result.QuestionType.Should().Be(QuestionTypeDto.ShortAnswer);
        result.Options.Should().BeNull("ShortAnswer questions have no options on the wire");
        result.ModelAnswer.Should().Be("Glucose");
    }
}
