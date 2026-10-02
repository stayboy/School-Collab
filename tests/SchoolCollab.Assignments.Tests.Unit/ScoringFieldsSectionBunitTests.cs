using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using SchoolCollab.Assignments.Application.Components.Pages.Assignments;
using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// WS-A3 (spec §3.3 + §7 Q4) — <see cref="ScoringFieldsSection"/>
/// renders the Pass Score + Max Attempts inputs for AutoGraded / InstantGraded; for
/// TeacherGraded they render <b>disabled with an inline reason</b>
/// (assignment-authoring-compartments UX-17/D12 — never hidden, so the authoring page does
/// not reflow when the grading format changes). The <c>ScoringFieldsPassSubmitGate</c>
/// safety rule is unchanged: a TeacherGraded submit never blocks on the hidden-then,
/// disabled-now values.
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
    public void AutoGraded_RendersPassScoreAndMaxAttempts()
    {
        var cut = Render(new AssignmentEditFormModel(), GradingFormatDto.AutoGraded);

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Pass Score",
                "AutoGraded must show the Pass Score field");
            cut.Markup.Should().Contain("Max Attempts",
                "AutoGraded must show the Max Attempts field");
        });
    }

    [TestMethod]
    public void InstantGraded_RendersPassScoreAndMaxAttempts()
    {
        var cut = Render(new AssignmentEditFormModel(), GradingFormatDto.InstantGraded);

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Pass Score");
            cut.Markup.Should().Contain("Max Attempts");
        });
    }

    [TestMethod]
    public void TeacherGraded_RendersBothInputsDisabledWithReason()
    {
        var cut = Render(new AssignmentEditFormModel(), GradingFormatDto.TeacherGraded);

        cut.WaitForAssertion(() =>
        {
            // UX-17 / D12 (assignment-authoring-compartments §12): inapplicable controls are
            // disabled with an inline reason, never hidden — the compartment must not reflow
            // when the grading format changes.
            var fields = cut.FindAll("fluent-number-field");
            fields.Should().HaveCount(2, "both fields stay rendered for TeacherGraded");
            fields.Should().OnlyContain(f => f.HasAttribute("disabled"),
                "TeacherGraded disables the Pass Score and Max Attempts fields");
            cut.Markup.Should().Contain("Pass Score");
            cut.Markup.Should().Contain("Max Attempts");
            cut.Markup.Should().Contain(ScoringFieldsSection.ScoringInapplicableReason,
                "the inline reason explains why the fields cannot be edited");
        });
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
        fields.Should().HaveCount(2, "AutoGraded must render the Pass Score and Max Attempts number fields");

        cut.WaitForAssertion(() =>
        {
            fields[0].GetAttribute("value").Should().Be("75",
                "the Pass Score input must render the model's value (decimal)");
            fields[1].GetAttribute("value").Should().Be("4",
                "the Max Attempts input must render the model's value (int)");
        });

        // Interaction write-back: edit the Pass Score and assert the model follows.
        fields[0].Change("88");
        model.PassScore.Should().Be(88m, "editing the Pass Score input must write back into the model");

        fields[1].Change("6");
        model.MaxAttempts.Should().Be(6, "editing the Max Attempts input must write back into the model");
    }
}
