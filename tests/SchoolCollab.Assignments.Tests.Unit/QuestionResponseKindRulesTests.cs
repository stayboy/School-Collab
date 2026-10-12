using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// Q5 (spec <c>question-response-types</c> §5.3) plus Q1(ii) (owner, 2026-10-09): every response
/// kind is media, so kinds are permitted on Teacher Marked alone — and the mandatory-kind rule is
/// tied to that permission, never looser, so a payload can never be either unsatisfiable (mandatory
/// where forbidden) or undefined (forbidden where mandatory).
/// </summary>
[TestClass]
public class QuestionResponseKindRulesTests
{
    private static IEnumerable<GradingFormatDto> AllFormats() => Enum.GetValues<GradingFormatDto>();

    [TestMethod]
    public void RequiresResponseKinds_IsExactlyWhereKindsArePermitted()
    {
        foreach (var format in AllFormats())
        {
            QuestionResponseKindRules.RequiresResponseKinds(format).Should().Be(
                QuestionResponseKindRules.IsPermitted(format, [QuestionResponseKindDto.Video]),
                $"{format}: the rule that makes a kind mandatory must hold exactly where a kind is "
                + "allowed — otherwise the two rules contradict each other on some format");
        }
    }

    [TestMethod]
    public void RequiresResponseKinds_IsTrueForTeacherMarkedOnly()
    {
        foreach (var format in AllFormats())
        {
            QuestionResponseKindRules.RequiresResponseKinds(format)
                .Should().Be(format == GradingFormatDto.TeacherGraded,
                    "a media answer can only be reviewed by a teacher, so that is the one format "
                    + "where the question must state what it expects");
        }
    }

    [TestMethod]
    public void IsPermitted_AllowsNoKinds_OnEveryFormat()
    {
        foreach (var format in AllFormats())
        {
            QuestionResponseKindRules.IsPermitted(format, [])
                .Should().BeTrue($"{format}: 'no media answer expected' is a legal state");
            QuestionResponseKindRules.IsPermitted(format, null)
                .Should().BeTrue($"{format}: an absent set is the same state as an empty one");
        }
    }

    [TestMethod]
    public void IsPermitted_RejectsMediaKinds_OnEveryFormatButTeacherMarked()
    {
        foreach (var format in AllFormats().Where(f => f != GradingFormatDto.TeacherGraded))
        {
            foreach (var kind in Enum.GetValues<QuestionResponseKindDto>())
            {
                QuestionResponseKindRules.IsPermitted(format, [kind])
                    .Should().BeFalse($"{format} has nothing to score a {kind} answer with");
            }
        }
    }

    [TestMethod]
    public void IsPermitted_AcceptsEveryKind_OnTeacherMarked()
    {
        foreach (var kind in Enum.GetValues<QuestionResponseKindDto>())
        {
            QuestionResponseKindRules.IsPermitted(GradingFormatDto.TeacherGraded, [kind])
                .Should().BeTrue("every kind is reviewable by hand");
        }
    }

    [TestMethod]
    public void EnsurePermitted_ThrowsTheTypedRejection_TheRoutesMapTo400()
    {
        var act = () => QuestionResponseKindRules.EnsurePermitted(
            GradingFormatDto.AutoGraded, [QuestionResponseKindDto.Document]);

        act.Should().Throw<QuestionResponseKindValidationException>()
            .WithMessage("*Teacher Marked*", "the message names the fix in the owner's vocabulary")
            .WithMessage("*Auto Scored*", "and the offending format by its description");
    }

    [TestMethod]
    public void EnsurePermitted_DoesNotThrowForThePermittedPair_OrForNoKinds()
    {
        var teacherMarked = () => QuestionResponseKindRules.EnsurePermitted(
            GradingFormatDto.TeacherGraded, [QuestionResponseKindDto.Audio]);
        teacherMarked.Should().NotThrow();

        var noKinds = () => QuestionResponseKindRules.EnsurePermitted(GradingFormatDto.InstantGraded, []);
        noKinds.Should().NotThrow("the media rule is vacuous when nothing is defined");
    }
}
