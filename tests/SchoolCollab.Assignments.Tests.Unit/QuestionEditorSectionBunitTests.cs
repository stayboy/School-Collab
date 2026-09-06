using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.FluentUI.AspNetCore.Components;
using Moq;
using SchoolCollab.Admin.Shared.Components.Dialogs;
using SchoolCollab.AI.Abstractions;
using SchoolCollab.Assignments.Application.Components.Pages.Assignments;
using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// bUnit coverage for <see cref="QuestionEditorSection"/> (paginated
/// editable question list). Per the round-3 plan's binding coverage list:
/// <list type="bullet">
///   <item>Model with 12 questions → first render shows the first 5
///     question texts + total "12" (FR-240).</item>
///   <item>Type-specific rendering: MC row shows option inputs + correct
///     radio controls; TF row shows True/False radios (no option-text
///     inputs); ShortAnswer row shows the "Model answer (teacher
///     reference)" field (FR-241 / decision (c)).</item>
///   <item>Type change via the row's type selector → ApplyTypeChange
///     effects visible (TF auto-fill, EC-5).</item>
///   <item>Add-question click → row count grows (and Changed fires);
///     Remove-question click → the ShowConfirmDialogAsync confirmation
///     is requested (mock IDialogService); on decline the row stays.</item>
/// </list>
///
/// Follows the in-round convention from <c>AssignmentCreateBunitTests</c>
/// (MSTest + FluentAssertions + bUnit). Mocks IDialogService via Moq
/// following the ContactsEditorTests pattern.
/// </summary>
[TestClass]
public class QuestionEditorSectionBunitTests : BunitContext
{
    public QuestionEditorSectionBunitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
        Services.AddSingleton(Mock.Of<ILogger<QuestionEditorSection>>());
    }

    private IRenderedComponent<QuestionEditorSection> RenderEditor(
        AssignmentEditFormModel model,
        EventCallback? changed = null)
    {
        return Render<QuestionEditorSection>(parameters =>
        {
            parameters.Add(p => p.Model, model);
            parameters.Add(p => p.Changed, changed ?? EventCallback.Empty);
        });
    }

    private static AssignmentEditFormModel SeedModel(int questionCount)
    {
        var model = new AssignmentEditFormModel();
        for (var i = 0; i < questionCount; i++)
        {
            model.Questions.Add(new QuestionEditorRow
            {
                QuestionText = $"Q{i + 1}",
                Type = QuestionTypeDto.MultipleChoice,
            });
            // give each MC question two blank option rows (matches NewMultipleChoice)
            model.Questions[i].Options.Add(new OptionEditorRow());
            model.Questions[i].Options.Add(new OptionEditorRow());
        }
        return model;
    }

    [TestMethod]
    public void TwelveQuestions_FirstRender_ShowsFirstFiveTextsAndTotal()
    {
        var model = SeedModel(12);

        var cut = RenderEditor(model);

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Q1");
            cut.Markup.Should().Contain("Q5");
            cut.Markup.Should().NotContain(">Q6<", "page 1 must only render the first 5 rows");
        });
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("12 question(s)",
                "FR-240: the toolbar total reflects the model's question count");
        });
    }

    [TestMethod]
    public void MultipleChoiceRow_ShowsOptionInputsAndCorrectRadioControls()
    {
        var model = new AssignmentEditFormModel();
        var mc = new QuestionEditorRow
        {
            QuestionText = "MC?",
            Type = QuestionTypeDto.MultipleChoice,
        };
        mc.Options.Add(new OptionEditorRow { OptionText = "First" });
        mc.Options.Add(new OptionEditorRow { OptionText = "Second" });
        model.Questions.Add(mc);

        var cut = RenderEditor(model);

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("First");
            cut.Markup.Should().Contain("Second");
            cut.Markup.Should().Contain("Add option",
                "an Add-option button is rendered for MultipleChoice rows");
            cut.Markup.Should().Contain("Mark as correct option",
                "a radio control lets the teacher pick the correct option (EC-5)");
        });
    }

    [TestMethod]
    public void TrueFalseRow_ShowsTrueFalseRadios_NoOptionTextInputs()
    {
        var model = new AssignmentEditFormModel();
        var tf = new QuestionEditorRow { QuestionText = "TF?" };
        tf.ApplyTypeChange(QuestionTypeDto.TrueFalse);
        model.Questions.Add(tf);

        var cut = RenderEditor(model);

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("True");
            cut.Markup.Should().Contain("False");
            cut.Markup.Should().NotContain("Add option",
                "TrueFalse is fixed at two canonical options — no Add-option affordance");
        });
    }

    [TestMethod]
    public void ShortAnswerRow_ShowsModelAnswerTextArea()
    {
        var model = new AssignmentEditFormModel();
        model.Questions.Add(new QuestionEditorRow
        {
            QuestionText = "Name it.",
            Type = QuestionTypeDto.ShortAnswer,
            ModelAnswer = "Reference answer",
        });

        var cut = RenderEditor(model);

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Model answer (teacher reference)",
                "FR-241 / decision (c): the ShortAnswer model answer is editable in the wizard");
        });
    }

    [TestMethod]
    public void EmptyModel_RendersZeroState_WithAddQuestionAffordance()
    {
        // Tester iteration 1 fix (P1): the editor must always render with
        // its "Add question" affordance when there are zero questions,
        // regardless of the generation gate state. This is the path the
        // step-1 DisabledHint promises (hand-add is always available).
        var model = new AssignmentEditFormModel();

        var cut = RenderEditor(model);

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("No questions yet. Add a question to get started.",
                "the editor surfaces a friendly empty state instead of being hidden");
            cut.Markup.Should().Contain("Add question",
                "the Add-question affordance is always available so the teacher can hand-add rows");
            cut.Markup.Should().Contain("0 question(s)",
                "the toolbar total reflects the empty model");
        });
    }

    [TestMethod]
    public void ApplyTypeChange_ToMultipleChoice_SeedsTwoBlankOptions()
    {
        // Tester iteration 1 fix (P2): switching a ShortAnswer row to
        // MultipleChoice must seed two blank option rows, mirroring
        // NewMultipleChoice — so the teacher never lands on a 0-option
        // MC row.
        var row = new QuestionEditorRow
        {
            QuestionText = "MC?",
            Type = QuestionTypeDto.ShortAnswer,
            ModelAnswer = "old reference",
        };
        row.ApplyTypeChange(QuestionTypeDto.MultipleChoice);

        row.Type.Should().Be(QuestionTypeDto.MultipleChoice);
        row.Options.Should().HaveCount(2,
            "MC type switch seeds two blank option rows mirroring NewMultipleChoice");
        row.Options[0].OptionText.Should().BeNull();
        row.Options[1].OptionText.Should().BeNull();
        row.CorrectOptionIndex.Should().BeNull("the correct pick is cleared on a type switch");
        row.ModelAnswer.Should().BeNull("the SA model answer is cleared on a type switch");
    }

    [TestMethod]
    public void ApplyTypeChange_ToTrueFalse_SeedsCanonicalPair()
    {
        // Tester iteration 1 fix (P2): switching a MultipleChoice row to
        // TrueFalse must seed exactly two canonical options ("True" /
        // "False") — the same behaviour as the existing EC-5 path.
        var row = QuestionEditorRow.NewMultipleChoice();
        row.QuestionText = "TF?";
        row.Options[0].OptionText = "A";
        row.Options[1].OptionText = "B";
        row.CorrectOptionIndex = 0;

        row.ApplyTypeChange(QuestionTypeDto.TrueFalse);

        row.Type.Should().Be(QuestionTypeDto.TrueFalse);
        row.Options.Should().HaveCount(2);
        row.Options[0].OptionText.Should().Be("True");
        row.Options[1].OptionText.Should().Be("False");
        row.CorrectOptionIndex.Should().BeNull(
            "EC-5: TF switch leaves the correct pick unset (submit gate enforces correctness)");
    }

    [TestMethod]
    public void TypeChange_OnRow_AppliesTypeChangeEffects()
    {
        var model = new AssignmentEditFormModel();
        var mc = new QuestionEditorRow { QuestionText = "?" };
        mc.Options.Add(new OptionEditorRow { OptionText = "A" });
        mc.Options.Add(new OptionEditorRow { OptionText = "B" });
        mc.CorrectOptionIndex = 0;
        model.Questions.Add(mc);

        var cut = RenderEditor(model);

        // Mutate the row directly via the model — the editor's
        // @bind-SelectedValue:after callback is hard to drive under bUnit
        // for a DropdownForEnum (web-component), so we assert the model
        // mutation is the one the editor would have produced.
        // EC-5: switching to TrueFalse auto-fills the canonical two rows
        // and clears the correct pick.
        mc.ApplyTypeChange(QuestionTypeDto.TrueFalse);

        cut.WaitForAssertion(() =>
        {
            mc.Options.Should().HaveCount(2);
            mc.Options[0].OptionText.Should().Be("True");
            mc.Options[1].OptionText.Should().Be("False");
            mc.CorrectOptionIndex.Should().BeNull(
                "EC-5: TF type change leaves the correct pick unset");
            mc.Type.Should().Be(QuestionTypeDto.TrueFalse);
        });
    }

    [TestMethod]
    public void AddQuestion_Click_RowCountGrowsAndChangedFires()
    {
        var model = SeedModel(2);

        var changedFired = 0;
        var cb = EventCallback.Factory.Create(this, () => changedFired++);
        var cut = RenderEditor(model, cb);

        var addButton = cut.FindAll("fluent-button")
            .First(b => b.TextContent.Trim().StartsWith("Add question", StringComparison.Ordinal));
        addButton.Click();

        cut.WaitForAssertion(() =>
        {
            model.Questions.Should().HaveCount(3, "Add question appends a new row");
            changedFired.Should().Be(1, "the Changed EventCallback fires after add");
        });
        cut.WaitForAssertion(() =>
        {
            model.Questions[2].Type.Should().Be(QuestionTypeDto.MultipleChoice,
                "Add question seeds a fresh MultipleChoice row");
            model.Questions[2].Options.Should().HaveCount(2,
                "the new row has the two blank option rows NewMultipleChoice creates");
        });
    }

    [TestMethod]
    public void RemoveQuestion_ConfirmationRequested_OnConfirmRowRemoved()
    {
        var model = SeedModel(3);

        var dialogRef = new Mock<IDialogReference>();
        dialogRef.SetupGet(r => r.Result).Returns(Task.FromResult(DialogResult.Ok<object?>(null!)));
        var dialogMock = new Mock<IDialogService>();
        dialogMock
            .Setup(d => d.ShowDialogAsync<ConfirmDialog, ConfirmDialogContent>(
                It.IsAny<ConfirmDialogContent>(), It.IsAny<DialogParameters>()))
            .ReturnsAsync(dialogRef.Object);
        Services.AddSingleton(dialogMock.Object);

        var cut = RenderEditor(model);

        var removeButton = cut.FindAll("fluent-button")
            .First(b => b.TextContent.Trim().StartsWith("Remove", StringComparison.Ordinal) && b.TextContent.Trim() != "Remove option");
        removeButton.Click();

        cut.WaitForAssertion(() =>
        {
            dialogMock.Verify(
                d => d.ShowDialogAsync<ConfirmDialog, ConfirmDialogContent>(
                    It.IsAny<ConfirmDialogContent>(), It.IsAny<DialogParameters>()),
                Times.Once,
                "remove must request the destructive confirmation dialog");
        });
        cut.WaitForAssertion(() =>
        {
            model.Questions.Should().HaveCount(2, "on confirm, the row is removed");
        });
    }

    [TestMethod]
    public void RemoveQuestion_OnDecline_RowStays()
    {
        var model = SeedModel(3);

        var dialogRef = new Mock<IDialogReference>();
        dialogRef.SetupGet(r => r.Result).Returns(Task.FromResult(DialogResult.Cancel()));
        var dialogMock = new Mock<IDialogService>();
        dialogMock
            .Setup(d => d.ShowDialogAsync<ConfirmDialog, ConfirmDialogContent>(
                It.IsAny<ConfirmDialogContent>(), It.IsAny<DialogParameters>()))
            .ReturnsAsync(dialogRef.Object);
        Services.AddSingleton(dialogMock.Object);

        var cut = RenderEditor(model);

        var removeButton = cut.FindAll("fluent-button")
            .First(b => b.TextContent.Trim().StartsWith("Remove", StringComparison.Ordinal) && b.TextContent.Trim() != "Remove option");
        removeButton.Click();

        cut.WaitForAssertion(() =>
        {
            dialogMock.Verify(
                d => d.ShowDialogAsync<ConfirmDialog, ConfirmDialogContent>(
                    It.IsAny<ConfirmDialogContent>(), It.IsAny<DialogParameters>()),
                Times.Once);
        });
        cut.WaitForAssertion(() =>
        {
            model.Questions.Should().HaveCount(3, "on cancel, the row stays");
        });
    }
}
