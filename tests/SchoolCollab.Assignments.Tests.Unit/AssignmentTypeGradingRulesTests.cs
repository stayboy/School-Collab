using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// D15 (owner, 2026-10-09): the assignment type defines which grading formats are possible —
/// Offline is Teacher Marked only (handwritten work cannot be auto-scored), while Online and
/// Hybrid keep all three formats. One matrix, two consumers: the authoring page's picker filter
/// and the create/update command guard.
/// </summary>
[TestClass]
public class AssignmentTypeGradingRulesTests
{
    [TestMethod]
    public void Offline_PermitsTeacherMarkedOnly()
    {
        AssignmentTypeGradingRules.PermittedFormats(AssignmentTypeDto.Manual)
            .Should().Equal([GradingFormatDto.TeacherGraded],
                "offline work is written by hand — there is nothing for the engine to score");

        AssignmentTypeGradingRules.IsPermitted(AssignmentTypeDto.Manual, GradingFormatDto.TeacherGraded)
            .Should().BeTrue();
        AssignmentTypeGradingRules.IsPermitted(AssignmentTypeDto.Manual, GradingFormatDto.AutoGraded)
            .Should().BeFalse();
        AssignmentTypeGradingRules.IsPermitted(AssignmentTypeDto.Manual, GradingFormatDto.InstantGraded)
            .Should().BeFalse();
    }

    [TestMethod]
    [DataRow(AssignmentTypeDto.Digital)]
    [DataRow(AssignmentTypeDto.SemiManual)]
    public void OnlineAndHybrid_KeepAllThreeFormats(AssignmentTypeDto type)
    {
        AssignmentTypeGradingRules.PermittedFormats(type).Should().Equal(
            [GradingFormatDto.TeacherGraded, GradingFormatDto.AutoGraded, GradingFormatDto.InstantGraded],
            "the details are published online, so the teacher may mark by hand or let the engine score");
    }

    [TestMethod]
    public void FallbackFor_IsTheTypesFirstPermittedFormat() =>
        AssignmentTypeGradingRules.FallbackFor(AssignmentTypeDto.Manual)
            .Should().Be(GradingFormatDto.TeacherGraded,
                "the auto-switch target when a type change invalidates the current pick");

    [TestMethod]
    public void EnsurePermitted_RejectsTheImpossiblePair_InTheOwnersVocabulary()
    {
        var act = () => AssignmentTypeGradingRules.EnsurePermitted(
            AssignmentTypeDto.Manual, GradingFormatDto.AutoGraded);

        act.Should().Throw<AssignmentTypeGradingValidationException>()
            .WithMessage("*Offline*", "the message names the type by its user-facing description")
            .WithMessage("*Auto Scored*", "and the grading format by its description");
    }

    [TestMethod]
    public void EnsurePermitted_AcceptsEveryPermittedPair()
    {
        foreach (var type in new[] { AssignmentTypeDto.Digital, AssignmentTypeDto.SemiManual, AssignmentTypeDto.Manual })
        {
            foreach (var format in AssignmentTypeGradingRules.PermittedFormats(type))
            {
                var act = () => AssignmentTypeGradingRules.EnsurePermitted(type, format);
                act.Should().NotThrow($"{type} + {format} is inside the type's matrix");
            }
        }
    }

    [TestMethod]
    public void MeaningOf_StatesEachTypesOwnerDefinition()
    {
        AssignmentTypeGradingRules.MeaningOf(AssignmentTypeDto.Digital)
            .Should().Be(AssignmentTypeGradingRules.OnlineMeaning);
        AssignmentTypeGradingRules.MeaningOf(AssignmentTypeDto.SemiManual)
            .Should().Be(AssignmentTypeGradingRules.HybridMeaning);
        AssignmentTypeGradingRules.MeaningOf(AssignmentTypeDto.Manual)
            .Should().Be(AssignmentTypeGradingRules.OfflineMeaning,
                "the owner's definitions are the durable text the form's hint quotes verbatim");
    }
}
