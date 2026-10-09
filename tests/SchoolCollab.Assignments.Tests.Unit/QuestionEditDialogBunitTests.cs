using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components.Dialogs;
using SchoolCollab.Assignments.Application.Components.Pages.Assignments;
using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// bUnit coverage for <see cref="QuestionEditDialog"/> (round content-questions-modern-ui, spec §6.2
/// QA-11): the per-type question fields that used to be rendered inline by
/// <c>QuestionEditorSection</c> now live here, opened from a row's text or its kebab's Edit action.
///
/// <para>Uses the repo's sanctioned dialog-shell harness — the <em>real</em>
/// <see cref="IDialogService"/> plus a rendered <see cref="FluentDialogProvider"/>, so the
/// show → interact → submit/cancel → <c>dialog.Result</c> path is exercised end to end
/// (see <c>DialogShellTests</c>, Admin.Tests.Unit). JS interop is Loose.</para>
/// </summary>
[TestClass]
public class QuestionEditDialogBunitTests : BunitContext
{
    private IDialogService DialogService => Services.GetRequiredService<IDialogService>();

    public QuestionEditDialogBunitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private (IRenderedComponent<FluentDialogProvider> Provider, Task<QuestionEditResult?> Closed) Open(QuestionEditModel model)
    {
        var provider = Render<FluentDialogProvider>();
        var closed = DialogService.ShowShellDialogAsync<QuestionEditDialog, QuestionEditModel, QuestionEditResult>(
            model, title: "Edit question", size: DialogSize.Medium);
        provider.WaitForAssertion(() => provider.Find("form"));
        return (provider, closed);
    }

    [TestMethod]
    public async Task Instructions_AddedInTheDialog_RoundTripThroughTheWorkingCopy()
    {
        var row = new QuestionEditorRow { QuestionText = "Q?", Type = QuestionTypeDto.ShortAnswer };
        var (provider, closed) = Open(QuestionEditModel.ForEdit(0, row));

        provider.Find("#cq-qedit-instructions-list-add").Click();

        provider.Find("form").Submit();
        var result = await closed;

        result.Should().NotBeNull();
        result!.Row.Instructions.Should().ContainSingle()
            .Which.Kind.Should().Be(InstructionKindDto.Text,
                "the shared instruction editor is wired into the dialog's working copy");
    }

    [TestMethod]
    public async Task ResponseKinds_TeacherMarked_TogglesBindAndThePayloadKeepsCanonicalOrder()
    {
        var row = new QuestionEditorRow { QuestionText = "Q?", Type = QuestionTypeDto.ShortAnswer };
        var (provider, closed) = Open(QuestionEditModel.ForEdit(0, row, GradingFormatDto.TeacherGraded));

        provider.FindAll("#cq-qedit-kinds").Should().ContainSingle(
            "the response picker renders for every format — it is the question's definition");
        provider.FindAll("#cq-qedit-kinds-reason").Should().BeEmpty(
            "Teacher Marked can carry kinds, so there is nothing to explain");

        var boxes = provider.FindComponents<FluentCheckbox>();
        boxes.Should().HaveCount(4, "one box per response kind");
        boxes[0].Instance.Disabled.Should().BeFalse("Teacher Marked can carry every kind");

        // Toggle Image (last) then Video (first): the reverse of the canonical order.
        await provider.InvokeAsync(() => boxes[3].Instance.ValueChanged.InvokeAsync(true));
        await provider.InvokeAsync(() => boxes[0].Instance.ValueChanged.InvokeAsync(true));

        provider.Find("form").Submit();
        var result = await closed;

        result.Should().NotBeNull();
        result!.Row.ResponseKinds.Should().Equal(
            [QuestionResponseKindDto.Video, QuestionResponseKindDto.Image],
            "the payload order is a function of the SET, not of the author's click order");
    }

    [TestMethod]
    public void ResponseKinds_AutoScored_RendersDisabledWithTheReason()
    {
        var row = new QuestionEditorRow { QuestionText = "Q?", Type = QuestionTypeDto.ShortAnswer };
        var (provider, _) = Open(QuestionEditModel.ForEdit(0, row, GradingFormatDto.AutoGraded));

        var boxes = provider.FindComponents<FluentCheckbox>();
        boxes.Should().HaveCount(4, "one box per response kind");
        boxes.Should().OnlyContain(box => box.Instance.Disabled,
            "the media rule forbids every kind on Auto Scored, so none may be picked");
        provider.Find("#cq-qedit-kinds-reason").TextContent.Should()
            .Contain("Teacher Marked", "the reason is stated once rather than left to the author to infer");
    }

    [TestMethod]
    public async Task ResponseKinds_AndInstructions_SurviveAnEditThroughTheDialog()
    {
        var row = new QuestionEditorRow { QuestionText = "Q?", Type = QuestionTypeDto.ShortAnswer };
        row.ResponseKinds.Add(QuestionResponseKindDto.Image);
        row.Instructions.Add(new InstructionEditorRow { Kind = InstructionKindDto.Text, Text = "Show working." });

        var (provider, closed) = Open(QuestionEditModel.ForEdit(0, row));

        provider.Find("form").Submit();
        var result = await closed;

        result.Should().NotBeNull();
        result!.Row.ResponseKinds.Should().Equal([QuestionResponseKindDto.Image],
            "the dialog edits a COPY — a copy that dropped the definition would wipe it on confirm");
        result.Row.Instructions.Should().ContainSingle()
            .Which.Text.Should().Be("Show working.", "the instruction blocks ride the copy too");
    }

    [TestMethod]
    public async Task MultipleChoice_RendersOptionInputsAndCorrectRadio_ThenSavesTheRow()
    {
        var row = new QuestionEditorRow { QuestionText = "MC?", Type = QuestionTypeDto.MultipleChoice };
        row.Options.Add(new OptionEditorRow { OptionText = "First" });
        row.Options.Add(new OptionEditorRow { OptionText = "Second" });
        row.CorrectOptionIndex = 1;

        var (provider, closed) = Open(QuestionEditModel.ForEdit(0, row));

        provider.Markup.Should().Contain("First");
        provider.Markup.Should().Contain("Second");
        provider.Markup.Should().Contain("Add option",
            "an Add-option affordance is rendered for MultipleChoice rows");
        provider.Markup.Should().Contain("Mark as correct option",
            "a radio control lets the teacher pick the correct option (EC-5)");

        provider.Find("form").Submit();

        var result = await closed;
        result.Should().NotBeNull();
        result!.Row.QuestionText.Should().Be("MC?");
        result.Row.Options.Should().HaveCount(2);
        result.Row.CorrectOptionIndex.Should().Be(1);
    }

    [TestMethod]
    public void TrueFalse_RendersTrueFalseRadios_NoOptionTextInputs()
    {
        var row = new QuestionEditorRow { QuestionText = "TF?" };
        row.ApplyTypeChange(QuestionTypeDto.TrueFalse);

        var (provider, _) = Open(QuestionEditModel.ForEdit(0, row));

        provider.Markup.Should().Contain("True");
        provider.Markup.Should().Contain("False");
        provider.Markup.Should().NotContain("Add option",
            "TrueFalse is fixed at two canonical options — no Add-option affordance");
    }

    [TestMethod]
    public void ShortAnswer_RendersModelAnswerTextArea()
    {
        var row = new QuestionEditorRow
        {
            QuestionText = "Name it.",
            Type = QuestionTypeDto.ShortAnswer,
            ModelAnswer = "Reference answer",
        };

        var (provider, _) = Open(QuestionEditModel.ForEdit(0, row));

        provider.Markup.Should().Contain("Model answer (teacher reference)",
            "FR-241 / decision (c): the ShortAnswer model answer is editable");
    }

    [TestMethod]
    public async Task AddOption_AppendsAnOptionRow_OnSave()
    {
        var (provider, closed) = Open(QuestionEditModel.ForCreate(0));

        provider.FindAll("fluent-button")
            .Single(b => b.TextContent.Trim() == "Add option")
            .Click();
        provider.Find("form").Submit();

        var result = await closed;
        result.Should().NotBeNull();
        result!.Row.Options.Should().HaveCount(3, "Add option appends one row to the two seeded ones");
    }

    [TestMethod]
    public async Task Cancel_DiscardsTheEdit_AndNeverTouchesTheSourceRow()
    {
        var row = new QuestionEditorRow
        {
            QuestionText = "Original",
            Type = QuestionTypeDto.ShortAnswer,
            ModelAnswer = "A",
        };
        var model = QuestionEditModel.ForEdit(0, row);

        model.Row.Should().NotBeSameAs(row,
            "the dialog edits a detached working copy — otherwise Cancel would half-apply the edit");

        var (provider, closed) = Open(model);

        provider.FindAll("fluent-button")
            .Single(b => b.TextContent.Contains("Cancel"))
            .Click();

        var result = await closed;
        result.Should().BeNull("a cancelled dialog returns null");
        row.QuestionText.Should().Be("Original");
        row.ModelAnswer.Should().Be("A");
    }
}
