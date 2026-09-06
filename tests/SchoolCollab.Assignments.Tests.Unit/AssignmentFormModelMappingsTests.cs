using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.AI.Abstractions;
using SchoolCollab.Assignments.Application.Components.Pages.Assignments;
using SchoolCollab.Assignments.Application.Helpers;
using SchoolCollab.Assignments.Contracts;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// Unit tests for the DTO → form-model projection on
/// <see cref="AssignmentEditFormModel"/>
/// (<see cref="AssignmentEditFormModel.LoadFrom"/> /
/// <see cref="AssignmentEditFormModel.From"/>) used by the assignment edit page,
/// plus the round-3 additions: editor-row mapping
/// (<see cref="QuestionEditorRow.FromGenerated"/> / type converters /
/// <see cref="QuestionEditorRow.ApplyTypeChange"/>),
/// <see cref="AssignmentEditFormModel.ToCreateRequest"/>,
/// <see cref="AssignmentEditFormModel.QuestionsPassSubmitGate"/>,
/// <see cref="AssignmentEditFormModel.GetQuestionPage"/>, and the
/// add/remove/append mutations. Keeping the projection in named, tested
/// methods (rather than inline field-by-field assignments in the razor)
/// makes the mapping easy to verify and keeps it in lockstep with both
/// types — see documents/solution/dto-form-model-mapping.md.
/// </summary>
[TestClass]
public class AssignmentFormModelMappingsTests
{
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TopicId = Guid.Parse("00000000-0000-0000-0000-000000000010");

    private static AssignmentSummaryDto MakeAssignment(DateTimeOffset? dueDate = null, decimal? maxScore = null) => new(
        Id: Guid.NewGuid(),
        Title: "Test Assignment",
        Description: "A description",
        AssignmentType: AssignmentTypeDto.Digital,
        GradingFormat: GradingFormatDto.TeacherGraded,
        TargetAudienceType: TargetAudienceTypeDto.AllStudents,
        TopicId: TopicId,
        TopicName: "Mathematics",
        GradeLevelId: null,
        GradeName: null,
        Status: AssignmentStatusDto.Draft,
        DueDate: dueDate,
        MaxScore: maxScore,
        MandatoryReview: false,
        CreatedByTeacherId: TeacherId,
        CreatedAt: DateTimeOffset.UtcNow,
        UpdatedAt: DateTimeOffset.UtcNow);

    [TestMethod]
    public void LoadFrom_MapsAllEditableFields()
    {
        var assignment = MakeAssignment(
            dueDate: new DateTimeOffset(2026, 8, 13, 10, 30, 0, TimeSpan.Zero),
            maxScore: 100m);
        var model = new AssignmentEditFormModel();

        model.LoadFrom(assignment);

        model.Title.Should().Be(assignment.Title);
        model.Description.Should().Be(assignment.Description);
        model.MaxScore.Should().Be(assignment.MaxScore);
        // DueDate converts DateTimeOffset? to DateTime? (the DTO's offset is dropped).
        model.DueDate.Should().Be(assignment.DueDate!.Value.DateTime);
        model.DueDate.Should().Be(new DateTime(2026, 8, 13, 10, 30, 0));
    }

    [TestMethod]
    public void LoadFrom_NullDueDate_StaysNull()
    {
        var assignment = MakeAssignment(dueDate: null);

        var model = new AssignmentEditFormModel { DueDate = new DateTime(2020, 1, 1) };
        model.LoadFrom(assignment);

        model.DueDate.Should().BeNull("a null DTO DueDate maps to a null form-model DueDate");
    }

    [TestMethod]
    public void From_ReturnsNewPopulatedModel()
    {
        var assignment = MakeAssignment(dueDate: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), maxScore: 50m);

        var model = AssignmentEditFormModel.From(assignment);

        model.Should().NotBeNull();
        model.Title.Should().Be(assignment.Title);
        model.Description.Should().Be(assignment.Description);
        model.DueDate.Should().Be(assignment.DueDate!.Value.DateTime);
        model.MaxScore.Should().Be(assignment.MaxScore);
    }

    [TestMethod]
    public void LoadFrom_OverwritesPriorValues()
    {
        var assignment = MakeAssignment(dueDate: new DateTimeOffset(2026, 5, 5, 8, 0, 0, TimeSpan.Zero), maxScore: 75m);
        var model = new AssignmentEditFormModel
        {
            Title = "Old",
            Description = "Old desc",
            DueDate = new DateTime(2000, 1, 1),
            MaxScore = 10m,
        };

        model.LoadFrom(assignment);

        model.Title.Should().Be(assignment.Title);
        model.Description.Should().Be(assignment.Description);
        model.DueDate.Should().Be(assignment.DueDate!.Value.DateTime);
        model.MaxScore.Should().Be(assignment.MaxScore);
    }

    // ── QuestionEditorRow.FromGenerated / type converters (decision (b)) ──

    [TestMethod]
    public void QuestionEditorRow_FromGenerated_MultipleChoice_PreservesOptionOrderAndCorrectIndex()
    {
        var dto = new GeneratedQuestionDto(
            Text: "Pick A.",
            Type: GeneratedQuestionType.MultipleChoice,
            Options: new[]
            {
                new GeneratedQuestionOptionDto("A", IsCorrect: true),
                new GeneratedQuestionOptionDto("B"),
                new GeneratedQuestionOptionDto("C"),
            });

        var row = QuestionEditorRow.FromGenerated(dto);

        row.QuestionText.Should().Be("Pick A.");
        row.Type.Should().Be(QuestionTypeDto.MultipleChoice);
        row.Options.Should().HaveCount(3);
        row.Options[0].OptionText.Should().Be("A");
        row.Options[1].OptionText.Should().Be("B");
        row.Options[2].OptionText.Should().Be("C");
        row.CorrectOptionIndex.Should().Be(0, "the only IsCorrect option is at index 0");
    }

    [TestMethod]
    public void QuestionEditorRow_FromGenerated_TrueFalse_HasTwoOptionsAndCorrectIndex()
    {
        var dto = new GeneratedQuestionDto(
            Text: "Photosynthesis requires sunlight.",
            Type: GeneratedQuestionType.TrueFalse,
            Options: new[]
            {
                new GeneratedQuestionOptionDto("True", IsCorrect: true),
                new GeneratedQuestionOptionDto("False"),
            });

        var row = QuestionEditorRow.FromGenerated(dto);

        row.Type.Should().Be(QuestionTypeDto.TrueFalse);
        row.Options.Should().HaveCount(2);
        row.Options[0].OptionText.Should().Be("True");
        row.Options[1].OptionText.Should().Be("False");
        row.CorrectOptionIndex.Should().Be(0);
    }

    [TestMethod]
    public void QuestionEditorRow_FromGenerated_ShortAnswer_NoOptions_ModelAnswerMapped()
    {
        var dto = new GeneratedQuestionDto(
            Text: "Name the main product of photosynthesis.",
            Type: GeneratedQuestionType.ShortAnswer,
            Options: null,
            ModelAnswer: "Glucose");

        var row = QuestionEditorRow.FromGenerated(dto);

        row.Type.Should().Be(QuestionTypeDto.ShortAnswer);
        row.Options.Should().BeEmpty();
        row.ModelAnswer.Should().Be("Glucose");
    }

    [TestMethod]
    public void QuestionEditorRow_FromGenerated_ShortAnswer_NullModelAnswerStaysNull()
    {
        var dto = new GeneratedQuestionDto(
            Text: "MC question",
            Type: GeneratedQuestionType.MultipleChoice,
            Options: new[] { new GeneratedQuestionOptionDto("A", IsCorrect: true) });

        var row = QuestionEditorRow.FromGenerated(dto);

        row.ModelAnswer.Should().BeNull("ModelAnswer must default to null when not supplied — preserves the prior contract");
    }

    [TestMethod]
    public void QuestionEditorRow_FromGeneratedType_RoundTripsAllValues()
    {
        foreach (var value in Enum.GetValues<GeneratedQuestionType>())
        {
            var mapped = QuestionEditorRow.FromGeneratedType(value);
            var back = QuestionEditorRow.ToGeneratedType(mapped);
            back.Should().Be(value, "the two enums are int-mirrored but the explicit switch must round-trip");
        }
    }

    [TestMethod]
    public void QuestionEditorRow_ToGeneratedType_RoundTripsAllValues()
    {
        foreach (var value in Enum.GetValues<QuestionTypeDto>())
        {
            var mapped = QuestionEditorRow.ToGeneratedType(value);
            var back = QuestionEditorRow.FromGeneratedType(mapped);
            back.Should().Be(value);
        }
    }

    // ── QuestionEditorRow.ApplyTypeChange (EC-5) ──────────────────────

    [TestMethod]
    public void QuestionEditorRow_ApplyTypeChange_TrueFalse_AutoFillsCanonicalOptions()
    {
        var row = QuestionEditorRow.NewMultipleChoice();
        row.QuestionText = "TF?";
        row.CorrectOptionIndex = 1;

        row.ApplyTypeChange(QuestionTypeDto.TrueFalse);

        row.Type.Should().Be(QuestionTypeDto.TrueFalse);
        row.Options.Should().HaveCount(2);
        row.Options[0].OptionText.Should().Be("True");
        row.Options[1].OptionText.Should().Be("False");
        row.CorrectOptionIndex.Should().BeNull("EC-5: TF type change leaves the correct pick unset");
    }

    [TestMethod]
    public void QuestionEditorRow_ApplyTypeChange_ShortAnswer_ClearsOptions()
    {
        var row = QuestionEditorRow.NewMultipleChoice();
        row.QuestionText = "Name it.";

        row.ApplyTypeChange(QuestionTypeDto.ShortAnswer);

        row.Type.Should().Be(QuestionTypeDto.ShortAnswer);
        row.Options.Should().BeEmpty();
        row.CorrectOptionIndex.Should().BeNull();
    }

    [TestMethod]
    public void QuestionEditorRow_ApplyTypeChange_MultipleChoice_SeedsTwoBlankOptionsAndClearsCorrect()
    {
        // Tester iteration 1 fix (P2): switching to MultipleChoice must
        // mirror NewMultipleChoice — two blank option rows so the teacher
        // never lands on a 0-option MC row. CorrectOptionIndex still
        // resets (the submit gate re-enforces correctness on submit).
        var row = new QuestionEditorRow
        {
            Type = QuestionTypeDto.TrueFalse,
            QuestionText = "?",
            Options = { new OptionEditorRow { OptionText = "True" }, new OptionEditorRow { OptionText = "False" } },
            CorrectOptionIndex = 0,
        };

        row.ApplyTypeChange(QuestionTypeDto.MultipleChoice);

        row.Type.Should().Be(QuestionTypeDto.MultipleChoice);
        row.Options.Should().HaveCount(2, "the MC switch seeds two blank option rows");
        row.Options[0].OptionText.Should().BeNull("the seeded rows are blank for the teacher to author");
        row.Options[1].OptionText.Should().BeNull();
        row.CorrectOptionIndex.Should().BeNull("the correct pick is cleared on a type switch");
    }

    [TestMethod]
    public void QuestionEditorRow_NewMultipleChoice_HasTwoBlankOptions()
    {
        var row = QuestionEditorRow.NewMultipleChoice();

        row.Type.Should().Be(QuestionTypeDto.MultipleChoice);
        row.Options.Should().HaveCount(2);
        row.Options[0].OptionText.Should().BeNull();
        row.Options[1].OptionText.Should().BeNull();
    }

    [TestMethod]
    public void QuestionEditorRow_RemoveOptionAt_ClearsCorrectWhenItPointsAtRemovedIndex()
    {
        var row = QuestionEditorRow.NewMultipleChoice();
        row.Options[0].OptionText = "A";
        row.Options[1].OptionText = "B";
        row.CorrectOptionIndex = 0;

        row.RemoveOptionAt(0);

        row.Options.Should().HaveCount(1);
        row.CorrectOptionIndex.Should().BeNull("removing the correct row clears the pick");
    }

    [TestMethod]
    public void QuestionEditorRow_RemoveOptionAt_ShiftsCorrectWhenItPointsAfterRemovedIndex()
    {
        var row = new QuestionEditorRow { Type = QuestionTypeDto.MultipleChoice };
        row.Options.Add(new OptionEditorRow { OptionText = "A" });
        row.Options.Add(new OptionEditorRow { OptionText = "B" });
        row.Options.Add(new OptionEditorRow { OptionText = "C" });
        row.CorrectOptionIndex = 2;

        row.RemoveOptionAt(0);

        row.Options.Should().HaveCount(2);
        row.CorrectOptionIndex.Should().Be(1, "removing index 0 shifts the correct index 2 → 1");
    }

    // ── AssignmentEditFormModel.ToCreateRequest ────────────────────────

    [TestMethod]
    public void ToCreateRequest_EmptyQuestionsAndAttachments_NullLists_AndAiPromptOverrideRoundTrips()
    {
        var model = new AssignmentEditFormModel
        {
            Title = "T",
            Description = "D",
            AiPromptOverride = "Be concise",
        };

        var req = model.ToCreateRequest(
            assignmentType: AssignmentTypeDto.Digital,
            gradingFormat: GradingFormatDto.AutoGraded,
            targetAudienceType: TargetAudienceTypeDto.AllStudents,
            topicId: TopicId,
            gradeLevelId: null,
            mandatoryReview: true);

        req.Title.Should().Be("T");
        req.Description.Should().Be("D");
        req.Questions.Should().BeNull("an empty collection projects to the wire default (null)");
        req.Attachments.Should().BeNull();
        req.AiPromptOverride.Should().Be("Be concise");
    }

    [TestMethod]
    public void ToCreateRequest_NullAiPromptOverride_StaysNull()
    {
        var model = new AssignmentEditFormModel { Title = "T" };

        var req = model.ToCreateRequest(
            AssignmentTypeDto.Manual,
            GradingFormatDto.TeacherGraded,
            TargetAudienceTypeDto.AllStudents,
            TopicId,
            null,
            true);

        req.AiPromptOverride.Should().BeNull();
        req.Questions.Should().BeNull();
        req.Attachments.Should().BeNull();
    }

    [TestMethod]
    public void ToCreateRequest_TwoQuestions_ReindexesDisplayOrderAndMapsIsCorrect()
    {
        var model = new AssignmentEditFormModel { Title = "T" };
        var mc = new QuestionEditorRow
        {
            Type = QuestionTypeDto.MultipleChoice,
            QuestionText = "MC?",
            CorrectOptionIndex = 1,
        };
        mc.Options.Add(new OptionEditorRow { OptionText = "A" });
        mc.Options.Add(new OptionEditorRow { OptionText = "B" });
        var tf = new QuestionEditorRow
        {
            Type = QuestionTypeDto.TrueFalse,
            QuestionText = "TF?",
            CorrectOptionIndex = 0,
        };
        tf.ApplyTypeChange(QuestionTypeDto.TrueFalse);
        model.Questions.Add(mc);
        model.Questions.Add(tf);

        var req = model.ToCreateRequest(
            AssignmentTypeDto.Digital,
            GradingFormatDto.AutoGraded,
            TargetAudienceTypeDto.AllStudents,
            TopicId,
            null,
            true);

        req.Questions.Should().NotBeNull();
        req.Questions!.Should().HaveCount(2);
        req.Questions[0].QuestionType.Should().Be(QuestionTypeDto.MultipleChoice);
        req.Questions[0].DisplayOrder.Should().Be(0, "EC-7: DisplayOrder is re-indexed 0..n in the projection");
        req.Questions[0].Options.Should().NotBeNull();
        req.Questions[0].Options!.Should().HaveCount(2);
        req.Questions[0].Options![0].IsCorrect.Should().BeFalse("index 1 is the correct option, not 0");
        req.Questions[0].Options![1].IsCorrect.Should().BeTrue();
        req.Questions[1].QuestionType.Should().Be(QuestionTypeDto.TrueFalse);
        req.Questions[1].DisplayOrder.Should().Be(1);
        req.Questions[1].Options!.Should().HaveCount(2);
    }

    [TestMethod]
    public void ToCreateRequest_ShortAnswerQuestion_OptionsNull_ModelAnswerMapped()
    {
        var model = new AssignmentEditFormModel { Title = "T" };
        model.Questions.Add(new QuestionEditorRow
        {
            Type = QuestionTypeDto.ShortAnswer,
            QuestionText = "Name it.",
            ModelAnswer = "Glucose",
        });

        var req = model.ToCreateRequest(
            AssignmentTypeDto.Digital,
            GradingFormatDto.AutoGraded,
            TargetAudienceTypeDto.AllStudents,
            TopicId,
            null,
            true);

        req.Questions![0].Options.Should().BeNull("ShortAnswer questions have no options on the wire");
        req.Questions[0].ModelAnswer.Should().Be("Glucose");
    }

    [TestMethod]
    public void ToCreateRequest_OneAttachment_FieldsMapped()
    {
        var model = new AssignmentEditFormModel { Title = "T" };
        model.Attachments.Add(new AttachmentEditorRow
        {
            FileName = "syllabus.pdf",
            ContentType = "application/pdf",
            FileSize = 12345,
            StoragePath = "tenants/x/assignments/y/syllabus.pdf",
        });

        var req = model.ToCreateRequest(
            AssignmentTypeDto.Digital,
            GradingFormatDto.AutoGraded,
            TargetAudienceTypeDto.AllStudents,
            TopicId,
            null,
            true);

        req.Attachments.Should().NotBeNull();
        req.Attachments!.Should().HaveCount(1);
        req.Attachments[0].FileName.Should().Be("syllabus.pdf");
        req.Attachments[0].ContentType.Should().Be("application/pdf");
        req.Attachments[0].FileSize.Should().Be(12345);
        req.Attachments[0].StoragePath.Should().Be("tenants/x/assignments/y/syllabus.pdf");
    }

    // ── AssignmentEditFormModel.QuestionsPassSubmitGate ─────────────────

    [TestMethod]
    public void QuestionsPassSubmitGate_ZeroQuestionsTeacherGraded_Passes()
    {
        var model = new AssignmentEditFormModel();
        var ok = model.QuestionsPassSubmitGate(GradingFormatDto.TeacherGraded, out var error);
        ok.Should().BeTrue();
        error.Should().BeNull();
    }

    [TestMethod]
    public void QuestionsPassSubmitGate_ZeroQuestionsAutoGraded_Fails()
    {
        var model = new AssignmentEditFormModel();
        var ok = model.QuestionsPassSubmitGate(GradingFormatDto.AutoGraded, out var error);
        ok.Should().BeFalse();
        error.Should().Contain("at least one question");
    }

    [TestMethod]
    public void QuestionsPassSubmitGate_ZeroQuestionsInstantGraded_Fails()
    {
        var model = new AssignmentEditFormModel();
        var ok = model.QuestionsPassSubmitGate(GradingFormatDto.InstantGraded, out var error);
        ok.Should().BeFalse();
        error.Should().Contain("at least one question");
    }

    [TestMethod]
    public void QuestionsPassSubmitGate_MultipleChoiceNoCorrect_Fails()
    {
        var model = new AssignmentEditFormModel();
        var q = QuestionEditorRow.NewMultipleChoice();
        q.QuestionText = "Pick one.";
        q.Options[0].OptionText = "A";
        q.Options[1].OptionText = "B";
        model.Questions.Add(q);

        var ok = model.QuestionsPassSubmitGate(GradingFormatDto.AutoGraded, out var error);
        ok.Should().BeFalse();
        error.Should().Contain("needs one correct option");
    }

    [TestMethod]
    public void QuestionsPassSubmitGate_MultipleChoiceCorrectIndexOutOfRange_Fails()
    {
        var model = new AssignmentEditFormModel();
        var q = QuestionEditorRow.NewMultipleChoice();
        q.QuestionText = "Pick one.";
        q.Options[0].OptionText = "A";
        q.Options[1].OptionText = "B";
        q.CorrectOptionIndex = 5; // out of range
        model.Questions.Add(q);

        model.QuestionsPassSubmitGate(GradingFormatDto.AutoGraded, out var error).Should().BeFalse();
        error.Should().Contain("needs one correct option");
    }

    [TestMethod]
    public void QuestionsPassSubmitGate_MultipleChoiceLessThanTwoOptions_Fails()
    {
        var model = new AssignmentEditFormModel();
        var q = new QuestionEditorRow { Type = QuestionTypeDto.MultipleChoice, QuestionText = "Q?" };
        q.Options.Add(new OptionEditorRow { OptionText = "A" });
        q.CorrectOptionIndex = 0;
        model.Questions.Add(q);

        model.QuestionsPassSubmitGate(GradingFormatDto.AutoGraded, out var error).Should().BeFalse();
        error.Should().Contain("at least 2 options");
    }

    [TestMethod]
    public void QuestionsPassSubmitGate_MultipleChoiceBlankOption_Fails()
    {
        var model = new AssignmentEditFormModel();
        var q = QuestionEditorRow.NewMultipleChoice();
        q.QuestionText = "Pick one.";
        q.Options[0].OptionText = "A";
        q.Options[1].OptionText = "  ";
        q.CorrectOptionIndex = 0;
        model.Questions.Add(q);

        model.QuestionsPassSubmitGate(GradingFormatDto.AutoGraded, out var error).Should().BeFalse();
        error.Should().Contain("empty option");
    }

    [TestMethod]
    public void QuestionsPassSubmitGate_BlankQuestionText_Fails()
    {
        var model = new AssignmentEditFormModel();
        var q = QuestionEditorRow.NewMultipleChoice();
        q.Options[0].OptionText = "A";
        q.Options[1].OptionText = "B";
        q.CorrectOptionIndex = 0;
        // QuestionText left null
        model.Questions.Add(q);

        model.QuestionsPassSubmitGate(GradingFormatDto.AutoGraded, out var error).Should().BeFalse();
        error.Should().Contain("needs text");
    }

    [TestMethod]
    public void QuestionsPassSubmitGate_TrueFalseCanonicalPairWithCorrect_Passes()
    {
        var model = new AssignmentEditFormModel();
        var q = new QuestionEditorRow { Type = QuestionTypeDto.TrueFalse, QuestionText = "TF?" };
        q.ApplyTypeChange(QuestionTypeDto.TrueFalse);
        q.CorrectOptionIndex = 0;
        model.Questions.Add(q);

        model.QuestionsPassSubmitGate(GradingFormatDto.AutoGraded, out var error).Should().BeTrue();
        error.Should().BeNull();
    }

    [TestMethod]
    public void QuestionsPassSubmitGate_TrueFalseMissingCorrect_Fails()
    {
        var model = new AssignmentEditFormModel();
        var q = new QuestionEditorRow { Type = QuestionTypeDto.TrueFalse, QuestionText = "TF?" };
        q.ApplyTypeChange(QuestionTypeDto.TrueFalse);
        q.CorrectOptionIndex = null;
        model.Questions.Add(q);

        model.QuestionsPassSubmitGate(GradingFormatDto.AutoGraded, out var error).Should().BeFalse();
        error.Should().Contain("needs one correct option");
    }

    [TestMethod]
    public void QuestionsPassSubmitGate_TrueFalseNonCanonicalLabels_Fails()
    {
        var model = new AssignmentEditFormModel();
        var q = new QuestionEditorRow { Type = QuestionTypeDto.TrueFalse, QuestionText = "TF?" };
        q.Options.Add(new OptionEditorRow { OptionText = "Yes" });
        q.Options.Add(new OptionEditorRow { OptionText = "No" });
        q.CorrectOptionIndex = 0;
        model.Questions.Add(q);

        model.QuestionsPassSubmitGate(GradingFormatDto.AutoGraded, out var error).Should().BeFalse();
        error.Should().Contain("True and False");
    }

    [TestMethod]
    public void QuestionsPassSubmitGate_ShortAnswerBare_Passes()
    {
        var model = new AssignmentEditFormModel();
        model.Questions.Add(new QuestionEditorRow
        {
            Type = QuestionTypeDto.ShortAnswer,
            QuestionText = "Name it.",
        });

        model.QuestionsPassSubmitGate(GradingFormatDto.AutoGraded, out var error).Should().BeTrue();
        error.Should().BeNull();
    }

    // ── AddQuestion / RemoveQuestionAt / AppendGenerated (decision (d)) ─

    [TestMethod]
    public void AddQuestion_AppendsAndReindexesDisplayOrder()
    {
        var model = new AssignmentEditFormModel();
        var a = QuestionEditorRow.NewMultipleChoice();
        var b = QuestionEditorRow.NewMultipleChoice();
        a.DisplayOrder = 7; // pre-existing index to prove re-index happens
        model.Questions.Add(a);

        model.AddQuestion(b);

        model.Questions.Should().HaveCount(2);
        model.Questions[0].Should().BeSameAs(a);
        model.Questions[1].Should().BeSameAs(b);
        model.Questions[0].DisplayOrder.Should().Be(0, "DisplayOrder is re-indexed 0..n over the whole list (EC-7)");
        model.Questions[1].DisplayOrder.Should().Be(1);
    }

    [TestMethod]
    public void RemoveQuestionAt_ReindexesDisplayOrder()
    {
        var model = new AssignmentEditFormModel();
        var a = new QuestionEditorRow { QuestionText = "A" };
        var b = new QuestionEditorRow { QuestionText = "B" };
        var c = new QuestionEditorRow { QuestionText = "C" };
        a.DisplayOrder = 5; b.DisplayOrder = 6; c.DisplayOrder = 7;
        model.Questions.Add(a);
        model.Questions.Add(b);
        model.Questions.Add(c);

        model.RemoveQuestionAt(1);

        model.Questions.Should().HaveCount(2);
        model.Questions[0].Should().BeSameAs(a);
        model.Questions[1].Should().BeSameAs(c);
        model.Questions[0].DisplayOrder.Should().Be(0);
        model.Questions[1].DisplayOrder.Should().Be(1);
    }

    [TestMethod]
    public void RemoveQuestionAt_OutOfRange_IsNoOp()
    {
        var model = new AssignmentEditFormModel();
        model.Questions.Add(QuestionEditorRow.NewMultipleChoice());

        model.RemoveQuestionAt(99);

        model.Questions.Should().HaveCount(1, "RemoveQuestionAt must silently ignore out-of-range indices");
    }

    [TestMethod]
    public void AppendGenerated_KeepsExistingRowsAndReindexes()
    {
        var model = new AssignmentEditFormModel();
        var existing = QuestionEditorRow.NewMultipleChoice();
        existing.QuestionText = "Hand-written";
        model.Questions.Add(existing);

        var generated = new[]
        {
            new GeneratedQuestionDto("Q1?", GeneratedQuestionType.MultipleChoice, new[] { new GeneratedQuestionOptionDto("A", true) }),
            new GeneratedQuestionDto("Q2?", GeneratedQuestionType.TrueFalse, new[]
            {
                new GeneratedQuestionOptionDto("True", true), new GeneratedQuestionOptionDto("False"),
            }),
        };

        model.AppendGenerated(generated);

        model.Questions.Should().HaveCount(3, "decision (d): generate twice = append, never replace");
        model.Questions[0].QuestionText.Should().Be("Hand-written", "pre-existing rows survive ahead of the appended ones");
        model.Questions[0].DisplayOrder.Should().Be(0);
        model.Questions[1].QuestionText.Should().Be("Q1?");
        model.Questions[1].DisplayOrder.Should().Be(1);
        model.Questions[2].QuestionText.Should().Be("Q2?");
        model.Questions[2].DisplayOrder.Should().Be(2);
    }

    [TestMethod]
    public void AppendGenerated_EmptyList_IsNoOp()
    {
        var model = new AssignmentEditFormModel();
        model.AppendGenerated([]);
        model.Questions.Should().BeEmpty();
    }

    [TestMethod]
    public void AppendGenerated_OnEmptyList_IndexesFromZero()
    {
        var model = new AssignmentEditFormModel();
        model.AppendGenerated(new[]
        {
            new GeneratedQuestionDto("Q?", GeneratedQuestionType.ShortAnswer, null, "Ans"),
        });
        model.Questions.Should().HaveCount(1);
        model.Questions[0].DisplayOrder.Should().Be(0);
    }

    // ── GetQuestionPage ────────────────────────────────────────────────

    [TestMethod]
    public void QuestionPageSize_DefaultIsFive()
    {
        AssignmentEditFormModel.QuestionPageSize.Should().Be(5, "FR-240: page size is fixed at 5");
    }

    [TestMethod]
    public void GetQuestionPage_TwelveRows_Slices5_5_2()
    {
        var model = new AssignmentEditFormModel();
        for (var i = 0; i < 12; i++)
        {
            model.Questions.Add(new QuestionEditorRow { QuestionText = $"Q{i + 1}" });
        }

        model.GetQuestionPage(0).Select(r => r.QuestionText).Should().Equal("Q1", "Q2", "Q3", "Q4", "Q5");
        model.GetQuestionPage(1).Select(r => r.QuestionText).Should().Equal("Q6", "Q7", "Q8", "Q9", "Q10");
        model.GetQuestionPage(2).Select(r => r.QuestionText).Should().Equal("Q11", "Q12");
    }

    [TestMethod]
    public void GetQuestionPage_EmptyList_ReturnsEmpty()
    {
        var model = new AssignmentEditFormModel();
        model.GetQuestionPage(0).Should().BeEmpty();
    }

    [TestMethod]
    public void GetQuestionPage_OutOfRangePageIndex_ClampsToLastPage()
    {
        var model = new AssignmentEditFormModel();
        for (var i = 0; i < 7; i++)
        {
            model.Questions.Add(new QuestionEditorRow { QuestionText = $"Q{i + 1}" });
        }

        model.GetQuestionPage(99).Should().HaveCount(2, "page 99 clamps to the last page (rows 6–7)");
    }

    [TestMethod]
    public void GetQuestionPage_NegativePageIndex_ClampsToFirst()
    {
        var model = new AssignmentEditFormModel();
        model.Questions.Add(new QuestionEditorRow { QuestionText = "Q1" });
        model.Questions.Add(new QuestionEditorRow { QuestionText = "Q2" });

        model.GetQuestionPage(-3).Select(r => r.QuestionText).Should().Equal("Q1", "Q2");
    }

    [TestMethod]
    public void QuestionPageCount_AlwaysAtLeastOne()
    {
        var model = new AssignmentEditFormModel();
        model.QuestionPageCount().Should().Be(1, "an empty list still has a single (empty) page so callers can pin CurrentPageIndex=0");
        model.Questions.Add(new QuestionEditorRow());
        model.QuestionPageCount().Should().Be(1);
        for (var i = 0; i < 4; i++) model.Questions.Add(new QuestionEditorRow());
        model.QuestionPageCount().Should().Be(1);
        model.Questions.Add(new QuestionEditorRow());
        model.QuestionPageCount().Should().Be(2);
    }

    // ── QuestionGenerationGate (FR-220) ────────────────────────────────

    [TestMethod]
    public void QuestionGenerationGate_IsEnabled_TruthTable()
    {
        QuestionGenerationGate.IsEnabled(AssignmentTypeDto.Digital, GradingFormatDto.AutoGraded).Should().BeTrue();
        QuestionGenerationGate.IsEnabled(AssignmentTypeDto.Digital, GradingFormatDto.InstantGraded).Should().BeTrue();
        QuestionGenerationGate.IsEnabled(AssignmentTypeDto.SemiManual, GradingFormatDto.AutoGraded).Should().BeTrue();
        QuestionGenerationGate.IsEnabled(AssignmentTypeDto.SemiManual, GradingFormatDto.InstantGraded).Should().BeTrue();

        // Manual type: never enabled.
        QuestionGenerationGate.IsEnabled(AssignmentTypeDto.Manual, GradingFormatDto.AutoGraded).Should().BeFalse();
        QuestionGenerationGate.IsEnabled(AssignmentTypeDto.Manual, GradingFormatDto.InstantGraded).Should().BeFalse();
        QuestionGenerationGate.IsEnabled(AssignmentTypeDto.Manual, GradingFormatDto.TeacherGraded).Should().BeFalse();

        // Any type with TeacherGraded: never enabled.
        QuestionGenerationGate.IsEnabled(AssignmentTypeDto.Digital, GradingFormatDto.TeacherGraded).Should().BeFalse();
        QuestionGenerationGate.IsEnabled(AssignmentTypeDto.SemiManual, GradingFormatDto.TeacherGraded).Should().BeFalse();

        // Null assignment type: never enabled.
        QuestionGenerationGate.IsEnabled(null, GradingFormatDto.AutoGraded).Should().BeFalse();
    }

    [TestMethod]
    public void QuestionGenerationGate_HintText_SelectsEnabledOrDisabled()
    {
        QuestionGenerationGate.HintText(AssignmentTypeDto.Digital, GradingFormatDto.AutoGraded)
            .Should().Be(QuestionGenerationGate.EnabledHint);
        QuestionGenerationGate.HintText(AssignmentTypeDto.Manual, GradingFormatDto.TeacherGraded)
            .Should().Be(QuestionGenerationGate.DisabledHint);
    }
}
