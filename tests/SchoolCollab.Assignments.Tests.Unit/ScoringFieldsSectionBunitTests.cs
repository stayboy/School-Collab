using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using SchoolCollab.Assignments.Application.Components.Pages.Assignments;
using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// WS-A3 (spec §3.3 + §7 Q4) — <see cref="ScoringFieldsSection"/>
/// renders the Pass Score input for AutoGraded / InstantGraded; for
/// TeacherGraded it renders <b>a disabled checkbox whose LABEL carries the
/// reason</b> (D11, assignment-create-edit-redesign — no inline paragraphs,
/// no disabled number field). The <c>ScoringFieldsPassSubmitGate</c>
/// safety rule is unchanged: a TeacherGraded submit never blocks on the
/// disabled state's values.
///
/// Round <c>authoring-compact-fields</c> (OD1): the attempt cap moved out of this section into the
/// authoring page's Basics <c>(Max score · Max attempts)</c> pair, so these tests pin the section's
/// shape — ONE control, the number field or its D11 checkbox — and the shared gate the page still
/// asks for the moved control. The cap's disabled-with-reason behaviour is pinned at page level
/// (<c>AssignmentAuthoringBunitTests.TeacherGraded_ScoringFields_RenderDisabledWithReason</c>).
/// </summary>
[TestClass]
public class ScoringFieldsSectionBunitTests : BunitContext
{
    public ScoringFieldsSectionBunitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private IRenderedComponent<ScoringFieldsSection> Render(AssignmentEditFormModel model, GradingFormatDto grading)
    {
        return Render<ScoringFieldsSection>(parameters =>
        {
            parameters.Add(p => p.Model, model);
            parameters.Add(p => p.GradingFormat, grading);
        });
    }

    [TestMethod]
    public void AutoGraded_RendersThePassScore()
    {
        var cut = Render(new AssignmentEditFormModel(), GradingFormatDto.AutoGraded);

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Pass Score",
                "AutoGraded must show the Pass Score field");
            cut.Markup.Should().NotContain("Max Attempts",
                "OD1: the attempt cap is the authoring page's Basics pair, not this section's");
        });
    }

    [TestMethod]
    public void InstantGraded_RendersThePassScore()
    {
        var cut = Render(new AssignmentEditFormModel(), GradingFormatDto.InstantGraded);

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Pass Score");
            cut.Markup.Should().NotContain("Max Attempts");
        });
    }

    [TestMethod]
    public void TeacherGraded_RendersThePassScoreDisabledWithReason()
    {
        var cut = Render(new AssignmentEditFormModel(), GradingFormatDto.TeacherGraded);

        cut.WaitForAssertion(() =>
        {
            // D11 (assignment-create-edit-redesign): the inapplicable state is a DISABLED
            // CHECKBOX whose LABEL carries the reason — never the number field, never a
            // standalone paragraph; the control id survives in both states.
            cut.FindAll("fluent-number-field").Should().BeEmpty(
                "D11: the number field is replaced by the checkbox while inapplicable");
            var passScore = cut.Find("#scoringFieldsPassScore");
            passScore.TagName.ToLowerInvariant().Should().Be("fluent-checkbox");
            passScore.HasAttribute("disabled").Should().BeTrue(
                "TeacherGraded disables the control");
            passScore.TextContent.Should().Contain(ScoringFieldsSection.ScoringInapplicableReason,
                "the checkbox's label explains why scoring cannot be edited");
            cut.Markup.Should().Contain("Pass Score");
        });
    }

    /// <summary>OD1: the moved attempt cap's gate is the section's own — the page must not restate
    /// the rule, so the shared predicate is public and answers for exactly the two scoring formats.</summary>
    [TestMethod]
    public void IsScoringInapplicable_GatesTheScoringFormats()
    {
        ScoringFieldsSection.IsScoringInapplicable(GradingFormatDto.AutoGraded).Should().BeFalse();
        ScoringFieldsSection.IsScoringInapplicable(GradingFormatDto.InstantGraded).Should().BeFalse();
        ScoringFieldsSection.IsScoringInapplicable(GradingFormatDto.TeacherGraded).Should().BeTrue(
            "TeacherGraded carries no pass threshold and no attempt cap");
    }

    [TestMethod]
    public void AutoGraded_BindToModelEchoesValues()
    {
        var model = new AssignmentEditFormModel { PassScore = 75m, MaxAttempts = 4 };
        var cut = Render(model, GradingFormatDto.AutoGraded);

        // The fields bind both ways: the model's values are reflected in
        // the RENDERED inputs (not just left intact on the model), and a
        // user edit writes back into the form model.
        var fields = cut.FindAll("fluent-number-field");
        fields.Should().ContainSingle("AutoGraded renders the Pass Score number field");

        cut.WaitForAssertion(() => fields[0].GetAttribute("value").Should().Be("75",
            "the Pass Score input must render the model's value (decimal)"));

        // Interaction write-back: edit the Pass Score and assert the model follows.
        fields[0].Change("88");
        model.PassScore.Should().Be(88m, "editing the Pass Score input must write back into the model");
        model.MaxAttempts.Should().Be(4,
            "the cap keeps its loaded value — this section no longer renders it, and must not reset it");
    }
}
