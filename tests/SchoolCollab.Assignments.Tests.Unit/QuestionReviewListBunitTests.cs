using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.FluentUI.AspNetCore.Components;
using Moq;
using SchoolCollab.AI.Abstractions;
using SchoolCollab.Assignments.Application.Components.Pages.Assignments;
using SchoolCollab.Assignments.Application.Helpers;
using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// bUnit coverage for <see cref="QuestionReviewList"/> (paged read-only
/// Review list). Per the round-3 plan's binding coverage list:
/// <list type="bullet">
///   <item>Model with 12 questions → 5 rows on page 1 in DisplayOrder
///     order + total; type badges via EnumHelper; the correct option is
///     marked; ShortAnswer model answer text shows; "Resources: none yet"
///     renders (decision (a) consequence).</item>
///   <item>Empty model → the "No questions yet." info bar.</item>
/// </list>
///
/// Follows the in-round convention from <c>AssignmentCreateBunitTests</c>
/// (MSTest + FluentAssertions + bUnit). The review list is read-only —
/// no event callbacks, no IAssignmentQuestionGenerator dependency —
/// so the test setup is the minimum needed.
/// </summary>
[TestClass]
public class QuestionReviewListBunitTests : BunitContext
{
    public QuestionReviewListBunitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private IRenderedComponent<QuestionReviewList> RenderReview(
        AssignmentEditFormModel model)
    {
        return Render<QuestionReviewList>(parameters =>
        {
            parameters.Add(p => p.Model, model);
        });
    }

    [TestMethod]
    public void TwelveQuestions_Page1ShowsFiveRowsInDisplayOrderWithCorrectMarked()
    {
        var model = new AssignmentEditFormModel();
        // Row 0: MultipleChoice, "Pick A", correct at index 0.
        var mc = new QuestionEditorRow
        {
            QuestionText = "Pick A",
            Type = QuestionTypeDto.MultipleChoice,
        };
        mc.Options.Add(new OptionEditorRow { OptionText = "A" });
        mc.Options.Add(new OptionEditorRow { OptionText = "B" });
        mc.CorrectOptionIndex = 0;
        model.Questions.Add(mc);
        // Row 1: TrueFalse, correct = False (index 1).
        var tf = new QuestionEditorRow { QuestionText = "TF?" };
        tf.ApplyTypeChange(QuestionTypeDto.TrueFalse);
        tf.CorrectOptionIndex = 1;
        model.Questions.Add(tf);
        // Row 2: ShortAnswer, with model answer.
        model.Questions.Add(new QuestionEditorRow
        {
            QuestionText = "Name it.",
            Type = QuestionTypeDto.ShortAnswer,
            ModelAnswer = "Glucose",
        });
        // Rows 3-11: padding so 12 total exist.
        for (var i = 3; i < 12; i++)
        {
            model.Questions.Add(new QuestionEditorRow { QuestionText = $"Pad {i + 1}" });
        }

        var cut = RenderReview(model);

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("12 question(s)",
                "the summary row reflects the model's question count");
            cut.Markup.Should().Contain("Resources: none yet",
                "decision (a): the attachment summary renders 'none yet' this round");
        });
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Pick A");
            cut.Markup.Should().Contain("TF?");
            cut.Markup.Should().Contain("Name it.");
            cut.Markup.Should().Contain("Pad 4");
            cut.Markup.Should().Contain("Pad 5");
            cut.Markup.Should().NotContain("Pad 6",
                "page 1 only renders the first 5 rows");
        });
        cut.WaitForAssertion(() =>
        {
            // Type badges use EnumHelper.GetDescription — the enum members
            // don't carry [Description] for QuestionTypeDto, so the default
            // member name ("MultipleChoice"/"TrueFalse"/"ShortAnswer") is used.
            cut.Markup.Should().Contain("MultipleChoice");
            cut.Markup.Should().Contain("TrueFalse");
            cut.Markup.Should().Contain("ShortAnswer");
        });
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("is-correct",
                "the correct option on the MC row is marked with the .is-correct class");
            cut.Markup.Should().Contain("Glucose",
                "ShortAnswer model answer text is rendered");
        });
    }

    [TestMethod]
    public void EmptyModel_RendersNoQuestionsYetInfoBar()
    {
        var model = new AssignmentEditFormModel();

        var cut = RenderReview(model);

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("No questions yet.",
                "an empty model renders the friendly info bar");
            cut.Markup.Should().Contain("Resources: none yet",
                "the attachment summary is still rendered on an empty model");
        });
    }

    [TestMethod]
    public void ShortAnswerRow_RendersModelAnswer()
    {
        var model = new AssignmentEditFormModel();
        model.Questions.Add(new QuestionEditorRow
        {
            QuestionText = "Name the main product of photosynthesis.",
            Type = QuestionTypeDto.ShortAnswer,
            ModelAnswer = "Glucose",
        });

        var cut = RenderReview(model);

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Model answer:");
            cut.Markup.Should().Contain("Glucose",
                "the model answer text is rendered next to the 'Model answer:' sublabel");
        });
    }

    [TestMethod]
    public void ShortAnswerRow_BlankModelAnswer_RendersDash()
    {
        var model = new AssignmentEditFormModel();
        model.Questions.Add(new QuestionEditorRow
        {
            QuestionText = "Q?",
            Type = QuestionTypeDto.ShortAnswer,
            // ModelAnswer left null/blank
        });

        var cut = RenderReview(model);

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Model answer:");
            cut.Markup.Should().Contain("—",
                "a blank model answer renders an em-dash placeholder");
        });
    }

    // ── WS-A1 / ar-4: live "Resources: N file(s)" summary line ────────

    [TestMethod]
    public void TwoAttachments_RendersResourcesTwoFilesSummary()
    {
        // Decision (a) consequence: with the Resources UI now landed, the
        // round-3 placeholder "Resources: none yet" must flip to the live
        // count when the form model carries staged attachments.
        var model = new AssignmentEditFormModel();
        model.Attachments.Add(new AttachmentEditorRow
        {
            FileName = "syllabus.pdf",
            ContentType = "application/pdf",
            FileSize = 2048,
            StoragePath = "tenants/t/staging/g1/syllabus.pdf",
        });
        model.Attachments.Add(new AttachmentEditorRow
        {
            FileName = "rubric.docx",
            ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            FileSize = 4096,
            StoragePath = "tenants/t/staging/g2/rubric.docx",
        });

        var cut = RenderReview(model);

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Resources: 2 file(s)",
                "decision (a): the round-3 placeholder flips to live data when attachments exist");
            cut.Markup.Should().NotContain("Resources: none yet",
                "with staged attachments, the empty-state copy is replaced");
        });
    }
}
