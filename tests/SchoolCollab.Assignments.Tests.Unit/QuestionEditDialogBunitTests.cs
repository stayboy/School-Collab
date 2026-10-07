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
