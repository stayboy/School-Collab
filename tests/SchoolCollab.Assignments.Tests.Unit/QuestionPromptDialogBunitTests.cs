using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components.Dialogs;
using SchoolCollab.Assignments.Application.Components.Pages.Assignments;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// bUnit coverage for <see cref="QuestionPromptDialog"/> (R3, spec §6.1a PB-1…PB-11). The generation
/// knobs used to render inline in the composer; they now live here, so this is where the EC-10
/// <c>min="1"</c> guard and the read-only context preview are asserted.
///
/// <para>Uses the repo's sanctioned dialog-shell harness — the <em>real</em>
/// <see cref="IDialogService"/> plus a rendered <see cref="FluentDialogProvider"/>, so the
/// show → interact → submit path is exercised end to end. JS interop is Loose.</para>
/// </summary>
[TestClass]
public class QuestionPromptDialogBunitTests : BunitContext
{
    private IDialogService DialogService => Services.GetRequiredService<IDialogService>();

    public QuestionPromptDialogBunitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private static QuestionPromptModel Model(
        string? topicName = "Photosynthesis",
        IReadOnlyList<int>? grades = null,
        IReadOnlyList<string>? resources = null,
        bool promptLocked = false,
        int count = 10,
        bool multiple = true,
        bool trueFalse = false,
        bool shortAnswer = true) =>
        new()
        {
            TopicName = topicName,
            GradeLevels = grades ?? [3],
            ResourceNames = resources ?? [],
            PromptLocked = promptLocked,
            QuestionCount = count,
            DifficultyEasyCount = 4,
            DifficultyMediumCount = 4,
            DifficultyHardCount = 2,
            IncludeMultipleChoice = multiple,
            IncludeTrueFalse = trueFalse,
            IncludeShortAnswer = shortAnswer,
        };

    private (IRenderedComponent<FluentDialogProvider> Provider, Task<QuestionPromptResult?> Closed) Open(
        QuestionPromptModel model)
    {
        var provider = Render<FluentDialogProvider>();
        var closed = DialogService.ShowShellDialogAsync<QuestionPromptDialog, QuestionPromptModel, QuestionPromptResult>(
            model, title: "Configure questions", size: DialogSize.Medium);
        provider.WaitForAssertion(() => provider.Find("form"));
        return (provider, closed);
    }

    [TestMethod]
    public void ContextBlock_RendersTheSubjectGradesAndGroundingNames()
    {
        var (provider, _) = Open(Model(grades: [1, 2], resources: ["syllabus.pdf", "example.com/article"]));

        provider.Markup.Should().Contain("Photosynthesis", "PB-3: the subject is previewed");
        provider.Markup.Should().Contain("Grade 1, Grade 2", "PB-3: the targeted grades are previewed");
        provider.Markup.Should().Contain("syllabus.pdf");
        provider.Markup.Should().Contain("example.com/article");
        provider.Markup.Should().Contain("(2)", "PB-3: the grounding row carries the resource count");
    }

    [TestMethod]
    public void ContextBlock_NoResources_OmitsTheGroundingRow()
    {
        var (provider, _) = Open(Model(resources: []));

        provider.Markup.Should().NotContain("Grounding",
            "PB-3: the grounding row renders only when resources exist");
    }

    [TestMethod]
    public void ContextBlock_NoGradeTargets_RendersDash()
    {
        var (provider, _) = Open(Model(grades: []));

        provider.Markup.Should().Contain("—", "PB-3: no grade target renders an em dash, never an empty cell");
    }

    [TestMethod]
    public void Knobs_OpenFromTheSuppliedValues_AndTheCountKeepsItsMinOneGuard()
    {
        var (provider, _) = Open(Model(count: 7));

        provider.Find("#cq-prompt-count").GetAttribute("value").Should().Be("7",
            "PB-6(i): the dialog opens from the values the caller seeded");
        provider.Find("#cq-prompt-count").GetAttribute("min").Should().Be("1",
            "EC-10: the count's min=1 guard moved here with the field");
        provider.Find("#cq-prompt-easy").GetAttribute("value").Should().Be("4");
        provider.Find("#cq-prompt-hard").GetAttribute("value").Should().Be("2");
    }

    [TestMethod]
    public void TypeChips_AreToggleButtonsCarryingAriaPressed()
    {
        var (provider, _) = Open(Model(multiple: true, trueFalse: false, shortAnswer: true));

        var toggles = provider.FindAll("button.chip-toggle");
        toggles.Should().HaveCount(3, "PB-2: one selectable chip per question type");

        var pressed = toggles.Select(b => b.GetAttribute("aria-pressed")).ToList();
        pressed.Should().Equal(
            new List<string> { "true", "false", "true" },
            "the chips mirror the seeded include-flags in canonical order (and aria-pressed is a real value, never a bare attribute)");
    }

    [TestMethod]
    public void AllTypesOff_ExplainsTheBalancedMix()
    {
        var (provider, _) = Open(Model(multiple: false, trueFalse: false, shortAnswer: false));

        provider.Find("#cq-prompt-all-off").TextContent.Should().Contain("No type selected",
            "PB-2: the all-off state keeps today's meaning and says so");
        provider.FindAll("button[aria-pressed='true'].chip-toggle").Should().BeEmpty();
    }

    [TestMethod]
    public void PromptLocked_KeepsTheKnobsLiveAndExplainsTheLock()
    {
        var (provider, _) = Open(Model(promptLocked: true));

        provider.Find("#cq-prompt-locked").TextContent.Should().Contain("locked",
            "PB-8: the lock is explained on the dialog");
        provider.Find("#cq-prompt-count").HasAttribute("disabled").Should().BeFalse(
            "PB-8: the knobs stay enabled under a lock — they still reach the model");
        provider.Find("#cq-prompt-count").GetAttribute("value").Should().Be("10");
    }

    [TestMethod]
    public async Task Submit_ReturnsTheEditedKnobs()
    {
        var (provider, closed) = Open(Model(count: 7));

        provider.Find("#cq-prompt-count").Change("12");
        provider.Find("form").Submit();

        var result = await closed;

        result.Should().NotBeNull();
        result!.QuestionCount.Should().Be(12, "the edited count is what the caller applies");
        result.DifficultyEasyCount.Should().Be(4);
        result.IncludeMultipleChoice.Should().BeTrue();
        result.IncludeTrueFalse.Should().BeFalse();
        result.IncludeShortAnswer.Should().BeTrue();
    }

    [TestMethod]
    public async Task Cancel_ReturnsNull_SoTheCallerLeavesTheKnobsUntouched()
    {
        var (provider, closed) = Open(Model());

        provider.FindAll("fluent-button")
            .First(b => b.TextContent.Trim() == "Cancel")
            .Click();

        var result = await closed;

        result.Should().BeNull("a cancelled dialog must not change anything (PB-5)");
    }
}
