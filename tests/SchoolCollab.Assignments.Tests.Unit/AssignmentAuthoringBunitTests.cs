using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.FluentUI.AspNetCore.Components;
using Moq;
using RichardSzalay.MockHttp;
using SchoolCollab.Admin.Shared.Components.Dialogs;
using SchoolCollab.Assignments.Application.Components.Pages.Assignments;
using SchoolCollab.Assignments.Application.Helpers;
using SchoolCollab.Assignments.Application.Services;
using SchoolCollab.Assignments.Contracts;
using SchoolCollab.Core.Features;
using SchoolCollab.Students.Application.Services;
using Authoring = SchoolCollab.Assignments.Application.Components.Pages.Assignments.AssignmentAuthoring;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// R1 acceptance (documents/specs/assignment-authoring-compartments.md §16; round
/// <c>round-assignment-authoring-r1</c> criteria 3–7) — the shared
/// <see cref="AssignmentAuthoring"/> component is the ONE authoring surface (D5/UX-9):
/// six compartments in Create / Edit / View, Edit parity with Create while Draft|Scheduled,
/// the §11 status-driven action bar, disabled-with-reason gating (UX-17/UX-19) and the
/// read-only policy values with the inherited badge (UX-13).
/// </summary>
[TestClass]
public class AssignmentAuthoringBunitTests : BunitContext
{
    private readonly MockHttpMessageHandler _mockHttp;
    private readonly JsonSerializerOptions _apiJsonOptions;
    private readonly StubFlagService _flags = new();

    /// <summary>The resolved signature-requirement body the authoring page's
    /// <c>/signature-default</c> read returns. Mutable so a test can switch the resolved
    /// policy to Mandatory before rendering.</summary>
    private string _signatureDefaultBody = "{\"requiresSignature\":false,\"signatureMode\":\"Disabled\"}";

    public AssignmentAuthoringBunitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();

        _apiJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters =
            {
                new JsonStringEnumConverter<AssignmentTypeDto>(),
                new JsonStringEnumConverter<AssignmentStatusDto>(),
                new JsonStringEnumConverter<GradingFormatDto>(),
                new JsonStringEnumConverter<TargetAudienceTypeDto>(),
                new JsonStringEnumConverter<QuestionTypeDto>(),
                new JsonStringEnumConverter<ApprovalStatusDto>(),
                new JsonStringEnumConverter<ModuleTypeDto>(),
                new JsonStringEnumConverter<ResourceKindDto>(),
            }
        };

        _mockHttp = new MockHttpMessageHandler();
        var httpClient = _mockHttp.ToHttpClient();
        httpClient.BaseAddress = new Uri("http://localhost");

        Services.AddSingleton(httpClient);
        Services.AddSingleton<AssignmentsApiClient>();
        Services.AddSingleton<StudentsApiClient>();
        Services.AddSingleton<SchoolCollab.Admin.Shared.Services.CodedValuesApiClient>();
        Services.AddSingleton(Mock.Of<ILogger<AssignmentsApiClient>>());
        Services.AddSingleton(Mock.Of<ILogger<StudentsApiClient>>());
        Services.AddSingleton(Mock.Of<ILogger<Authoring>>());
        // The Questions & AI compartment injects the generation seams; these tests never
        // click Generate, so bare mocks are the right shape.
        Services.AddSingleton(Mock.Of<IAssignmentQuestionGenerator>());
        Services.AddSingleton(Mock.Of<IUrlTextExtractor>());
        Services.AddSingleton<IFeatureFlagService>(_flags);

        // Baseline backends every mode touches: the optional policy reads are always
        // 200 (fail-open), and the pickers are empty.
        _mockHttp.When(HttpMethod.Get, "http://localhost/assignments/ai-prompt-policy")
            .Respond(HttpStatusCode.OK, "application/json", "{\"aiPromptLocked\":false}");
        _mockHttp.When(HttpMethod.Get, "http://localhost/assignments/signature-default")
            .Respond(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_signatureDefaultBody, Encoding.UTF8, "application/json")
            });
        _mockHttp.When(HttpMethod.Get, "http://localhost/students/grade-levels")
            .Respond(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(_gradeLevels, _apiJsonOptions), Encoding.UTF8, "application/json")
            });
        // R2 (TGT-5): the stream picker sources its options from the grade-stream listing.
        _mockHttp.When(HttpMethod.Get, "http://localhost/students/grade-levels/*/streams")
            .Respond(HttpStatusCode.OK, "application/json", "[]");
        // R2 (TGT-16): the live recipient preview — advisory, always 200.
        // Left for individual tests to configure so degraded/counts cases can
        // supply their own shaped responses without fighting registration order.
        _mockHttp.When(HttpMethod.Get, "http://localhost/activity-groups*")
            .Respond(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(_activityGroups, _apiJsonOptions), Encoding.UTF8, "application/json")
            });
    }

    /// <summary>Activity groups the students API offers to the picker. Mutable so a test can
    /// seed a group before rendering — this matcher is registered first and MockHttp matches
    /// in registration order.</summary>
    private ActivityGroupDto[] _activityGroups = [];

    /// <summary>Grade levels the students API offers (R2: the grade TARGET picker's options). The
    /// base registration answers an empty list; a test that needs grades registers its own first.</summary>
    private GradeLevelDto[] _gradeLevels = [];

    private sealed class StubFlagService : IFeatureFlagService
    {
        public bool ActivityGroupsEnabled { get; set; } = true;
        public bool IsEnabled(string featureKey) => ActivityGroupsEnabled;
        public Task<bool> IsEnabledAsync(string featureKey, CancellationToken ct = default) => Task.FromResult(ActivityGroupsEnabled);
        public IDictionary<string, bool> GetAllFlags() => new Dictionary<string, bool>();
        public Task<IReadOnlyDictionary<string, bool>> GetAllFlagsAsync(Guid? tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, bool>>(new Dictionary<string, bool>());
    }

    private static AssignmentSummaryDto MakeDto(
        AssignmentStatusDto status,
        AssignmentTypeDto type = AssignmentTypeDto.Digital,
        GradingFormatDto grading = GradingFormatDto.TeacherGraded,
        string? instructions = null,
        TargetAudienceTypeDto audience = TargetAudienceTypeDto.AllStudents) =>
        new(
            Id: Guid.NewGuid(),
            Title: "Math HW",
            Description: "internal note",
            AssignmentType: type,
            GradingFormat: grading,
            TargetAudienceType: audience,
            TopicId: Guid.NewGuid(),
            TopicName: "Math",
            GradeLevelId: null,
            GradeName: null,
            Status: status,
            DueDate: null,
            MaxScore: null,
            MandatoryReview: true,
            CreatedByTeacherId: Guid.NewGuid(),
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow,
            RequiresApproval: false,
            Instructions: instructions);

    /// <summary>Registers the detail read plus the self-loading section backends
    /// (questions draft) so Edit mode renders without tripping the mock's unmatched-request
    /// exception. The specific routes are registered BEFORE any generic one (MockHttp v6
    /// first-match ordering). <paramref name="children"/> is the assignment-authoring child
    /// read the Edit surface loads its editors from (P1 rework);
    /// <paramref name="childrenReadFails"/> reproduces a failed read so the fail-closed
    /// rendering can be asserted.</summary>
    private AssignmentSummaryDto SetupAssignment(
        AssignmentSummaryDto dto,
        AssignmentAuthoringChildrenDto? children = null,
        Guid[]? linkedGroupIds = null,
        bool childrenReadFails = false)
    {
        if (childrenReadFails)
        {
            _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{dto.Id}/authoring")
                .Respond(HttpStatusCode.InternalServerError);
        }
        else
        {
            _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{dto.Id}/authoring")
                .Respond(HttpStatusCode.OK, "application/json",
                    JsonSerializer.Serialize(children ?? MakeChildren(), _apiJsonOptions));
        }

        _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{dto.Id}/groups")
            .Respond(HttpStatusCode.OK, "application/json", JsonSerializer.Serialize(
                (linkedGroupIds ?? []).Select(id => new { id, name = "Group " + id.ToString()[..4], isActive = true }),
                _apiJsonOptions));

        _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{dto.Id}/questions-draft")
            .Respond(HttpStatusCode.OK, "application/json", "[]");
        _mockHttp.When(HttpMethod.Get, $"http://localhost/students/subjects/by-grade/*")
            .Respond(HttpStatusCode.OK, "application/json", "[]");
        _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{dto.Id}")
            .Respond(HttpStatusCode.OK, "application/json", JsonSerializer.Serialize(dto, _apiJsonOptions));
        return dto;
    }

    /// <summary>An empty authoring child read (no persisted questions, attachments or
    /// resources) — the baseline the existing Edit/View tests run against.</summary>
    private static AssignmentAuthoringChildrenDto MakeChildren(
        IReadOnlyList<AssignmentQuestionReadDto>? questions = null,
        IReadOnlyList<AssignmentAttachmentReadDto>? attachments = null,
        IReadOnlyList<ResourceDto>? resources = null,
        IReadOnlyList<AssignmentTargetDto>? targets = null) =>
        new(Guid.NewGuid(), questions ?? [], attachments ?? [], resources ?? [], targets ?? []);

    private IRenderedComponent<Authoring> RenderAuthoring(
        AssignmentAuthoringMode mode,
        AssignmentSummaryDto? dto = null,
        AssignmentAuthoringChildrenDto? children = null,
        Guid[]? linkedGroupIds = null,
        bool childrenReadFails = false,
        TimeProvider? timeProvider = null)
    {
        if (dto is not null)
            SetupAssignment(dto, children, linkedGroupIds, childrenReadFails);
        return Render<Authoring>(parameters =>
        {
            parameters.Add(p => p.Mode, mode);
            if (dto is not null) parameters.Add(p => p.Id, dto.Id);
            if (timeProvider is not null) parameters.Add(p => p.TimeProvider, timeProvider);
        });
    }

    private static IReadOnlyList<string> CompartmentTitles(IRenderedComponent<Authoring> cut) =>
        cut.FindAll("h3.authoring-compartment-title").Select(h => h.TextContent.Trim()).ToList();

    private static readonly string[] ExpectedCompartments =
    [
        "Basics",
        "Audience & Targets",
        "Delivery & Publishing",
        "Submission & Sign-off",
        "Content & Resources",
        "Questions & AI"
    ];

    private static bool ScoringFieldDisabled(IRenderedComponent<Authoring> cut, string id) =>
        cut.Find($"#{id}").HasAttribute("disabled");

    private static FluentSelect<Authoring.PickerOption> Picker(IRenderedComponent<Authoring> cut, string id) =>
        cut.FindComponents<FluentSelect<Authoring.PickerOption>>().Single(s => s.Instance.Id == id).Instance;

    /// <summary>Drives a picker's real <c>SelectedOptionChanged</c> callback — never a raw DOM
    /// event (the AssignmentCreateBunitTests / AssignmentPolicyFieldEditDialogTests pattern).</summary>
    private static Task SelectAsync(IRenderedComponent<Authoring> cut, string pickerId, string value, string label) =>
        cut.InvokeAsync(() => Picker(cut, pickerId).SelectedOptionChanged.InvokeAsync(
            new Authoring.PickerOption(value, label)));

    /// <summary>Drives the multi-select activity-group picker's real callback with the option
    /// objects its own item list holds (the picker matches a selection against its items).</summary>
    /// <summary>R2 (TGT-2): flips the compartment's "Everyone" toggle — the Create-mode route to a
    /// valid target set, since TGT-13 refuses a save with no targets.</summary>
    private static Task SelectEveryoneAsync(IRenderedComponent<Authoring> cut, bool everyone = true) =>
        cut.InvokeAsync(() => cut.FindComponents<FluentCheckbox>()
            .Single(c => c.Instance.Id == "authoring-audience-everyone").Instance.ValueChanged.InvokeAsync(everyone));

    /// <summary>Drives the subject picker with the option INSTANCE its own Items hold (a foreign
    /// instance is not accepted as the selection by the FluentUI control).</summary>
    private static Task SelectSubjectAsync(IRenderedComponent<Authoring> cut, Guid topicId) =>
        cut.InvokeAsync(() =>
        {
            var picker = Picker(cut, "authoring-basics-subject");
            var option = picker.Items!.First(o => o.Value == topicId.ToString());
            return picker.SelectedOptionChanged.InvokeAsync(option);
        });

    /// <summary>Drives the grade-target multi-select's real callback with its own option objects.</summary>
    private static Task SelectGradeTargetAsync(IRenderedComponent<Authoring> cut, Guid gradeId, string name) =>
        SelectGradeTargetsAsync(cut, (gradeId, name));

    /// <summary>Drives the grade-target multi-select's real callback with a whole option set — the
    /// picker reports the complete selection of its own kind on every change.</summary>
    private static Task SelectGradeTargetsAsync(
        IRenderedComponent<Authoring> cut, params (Guid Id, string Name)[] grades) =>
        cut.InvokeAsync(() => Picker(cut, "authoring-audience-grades").SelectedOptionsChanged.InvokeAsync(
            grades.Select(g => new Authoring.PickerOption(g.Id.ToString(), g.Name)).ToArray()));

    /// <summary>The chip list's labels, in DisplayOrder.</summary>
    private static IReadOnlyList<string> ChipLabels(IRenderedComponent<Authoring> cut) =>
        cut.FindAll(".authoring-target-chip fluent-badge").Select(b => b.TextContent.Trim()).ToList();

    private static Task SelectGroupsAsync(IRenderedComponent<Authoring> cut, params ActivityGroupDto[] groups) =>
        cut.InvokeAsync(() => Picker(cut, "authoring-audience-groups").SelectedOptionsChanged.InvokeAsync(
            groups.Select(g => new Authoring.PickerOption(g.Id.ToString(), g.Name)).ToArray()));

    private static ActivityGroupDto Group(Guid id, string name) =>
        new(id, name, null, null, null, true, "span", null, null, false, [], 0,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    /// <summary>Fills the Basics title through the bound text field's own callback — the save
    /// guards require a non-empty title (the AssignmentCreateBunitTests precedent).</summary>
    private static Task SetTitleAsync(IRenderedComponent<Authoring> cut, string title) =>
        cut.InvokeAsync(() => cut.FindComponents<FluentTextField>()
            .Single(f => f.Instance.Id == "authoring-basics-title").Instance.ValueChanged.InvokeAsync(title));

    // ── Criterion 3: six compartments in all three modes ────────────────────

    [TestMethod]
    public void Create_RendersAllSixCompartments()
    {
        var cut = RenderAuthoring(AssignmentAuthoringMode.Create);

        cut.WaitForAssertion(() =>
        {
            CompartmentTitles(cut).Should().Equal(ExpectedCompartments,
                "UX-1/UX-3: the six compartments render stacked in one scroll, in canonical order");
        });
    }

    [TestMethod]
    public void Edit_Draft_RendersAllSixCompartments()
    {
        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(AssignmentStatusDto.Draft));

        cut.WaitForAssertion(() =>
            CompartmentTitles(cut).Should().Equal(ExpectedCompartments));
    }

    [TestMethod]
    public void View_Published_RendersAllSixCompartments()
    {
        var cut = RenderAuthoring(AssignmentAuthoringMode.View, MakeDto(AssignmentStatusDto.Published));

        cut.WaitForAssertion(() =>
            CompartmentTitles(cut).Should().Equal(ExpectedCompartments));
    }

    [TestMethod]
    public void EveryMode_RendersTheJumpNavForAllSixCompartments()
    {
        foreach (var mode in Enum.GetValues<AssignmentAuthoringMode>())
        {
            var cut = mode == AssignmentAuthoringMode.Create
                ? RenderAuthoring(mode)
                : RenderAuthoring(mode, MakeDto(AssignmentStatusDto.Draft));

            cut.WaitForAssertion(() =>
            {
                // FluentAnchor renders a <fluent-anchor> custom element (the repo convention),
                // not a plain <a> — the WardAssignmentPlayerBunitTests selector precedent.
                cut.FindAll("nav.authoring-jumpnav fluent-anchor")
                    .Select(a => a.TextContent.Trim())
                    .Should().Equal(ExpectedCompartments,
                        "UX-4: the sticky jump-nav mirrors the compartment list in every mode");
            });
        }
    }

    [TestMethod]
    public void Create_InstructionsFieldIsRenderedInBasics()
    {
        var cut = RenderAuthoring(AssignmentAuthoringMode.Create);

        cut.WaitForAssertion(() =>
        {
            cut.FindComponents<FluentTextArea>()
                .Should().Contain(t => t.Instance.Id == "authoring-basics-instructions",
                    "INS-1: Instructions is the student-facing field in compartment 1");
        });
    }

    // ── Criterion 4: Edit parity, View for later statuses ──────────────────

    [TestMethod]
    public void Edit_Draft_SurfacesTheQuestionResourceAndScoringEditors()
    {
        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(AssignmentStatusDto.Draft));

        cut.WaitForAssertion(() =>
        {
            cut.FindComponents<QuestionEditorSection>().Should().NotBeEmpty(
                "D5: Edit reaches parity with Create — the manual question editor is present");
            cut.FindComponents<QuestionGenerationSection>().Should().NotBeEmpty(
                "D5: the AI generation section is present");
            cut.FindComponents<ResourcesSection>().Should().NotBeEmpty(
                "D5: the attachments / URL-resource editor is present");
            cut.FindComponents<ScoringFieldsSection>().Should().NotBeEmpty(
                "D5: the scoring section is present");
        });
    }

    [TestMethod]
    public void Edit_Scheduled_SurfacesTheEditors()
    {
        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(AssignmentStatusDto.Scheduled));

        cut.WaitForAssertion(() =>
            cut.FindComponents<QuestionEditorSection>().Should().NotBeEmpty(
                "UX-9: Scheduled is still editable"));
    }

    [TestMethod]
    [DataRow(AssignmentStatusDto.Published)]
    [DataRow(AssignmentStatusDto.Closed)]
    [DataRow(AssignmentStatusDto.Archived)]
    public void Edit_LaterStatus_DegradesToReadOnlyView(AssignmentStatusDto status)
    {
        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(status));

        cut.WaitForAssertion(() =>
        {
            cut.FindComponents<QuestionEditorSection>().Should().BeEmpty(
                "UX-9/UX-11: Published/Closed/Archived render View mode — no editors");
            cut.FindComponents<ResourcesSection>().Should().BeEmpty();
            cut.FindComponents<FluentTextField>()
                .Single(f => f.Instance.Id == "authoring-basics-title").Instance.ReadOnly
                .Should().BeTrue("View mode renders the always-visible spine read-only (UX-12)");
        });
    }

    [TestMethod]
    public void View_LaterStatus_RendersAllCompartmentsReadOnly()
    {
        var cut = RenderAuthoring(AssignmentAuthoringMode.View, MakeDto(AssignmentStatusDto.Closed));

        cut.WaitForAssertion(() =>
        {
            CompartmentTitles(cut).Should().Equal(ExpectedCompartments);
            cut.FindComponents<QuestionEditorSection>().Should().BeEmpty();
            cut.FindComponents<FluentTextArea>()
                .Single(t => t.Instance.Id == "authoring-basics-instructions").Instance.ReadOnly
                .Should().BeTrue();
        });
    }

    // ── Criterion 5: the §11 action matrix ─────────────────────────────────

    [TestMethod]
    [DataRow(AssignmentStatusDto.Draft, "Publish")]
    [DataRow(AssignmentStatusDto.Scheduled, "Unpublish")]
    [DataRow(AssignmentStatusDto.Published, "Unpublish")]
    [DataRow(AssignmentStatusDto.Closed, "Archive")]
    public void ActionBar_PrimaryAction_FollowsTheStatusMatrix(
        AssignmentStatusDto status, string expectedPrimary)
    {
        var cut = RenderAuthoring(AssignmentAuthoringMode.View, MakeDto(status));

        cut.WaitForAssertion(() =>
        {
            cut.Find("#authoring-primary-action").TextContent.Trim().Should().Be(expectedPrimary);
        });
    }

    [TestMethod]
    public void ActionBar_Archived_HasNoPrimaryAction()
    {
        var cut = RenderAuthoring(AssignmentAuthoringMode.View, MakeDto(AssignmentStatusDto.Archived));

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("#authoring-primary-action").Should().BeEmpty(
                "§11: Archived is terminal — no primary action");
        });
    }

    [TestMethod]
    public void ActionBar_Create_PrimaryActionSavesADraft()
    {
        var cut = RenderAuthoring(AssignmentAuthoringMode.Create);

        cut.WaitForAssertion(() =>
            cut.Find("#authoring-primary-action").TextContent.Trim().Should().Be("Save as Draft",
                "§5: Create's Save creates a Draft"));
    }

    [TestMethod]
    public void ActionBar_Draft_KebabHostsTheAuthoringActions()
    {
        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(AssignmentStatusDto.Draft));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW"));
        cut.Find("fluent-button[title=\"More assignment actions\"]").Click();

        var items = cut.FindAll("fluent-menu-item").Select(i => i.TextContent.Trim()).ToList();
        items.Should().Contain("Save draft");
        items.Should().Contain("Generate questions", "AI-1: generation is an explicit kebab action");
        items.Should().Contain("Schedule");
    }

    [TestMethod]
    public void ActionBar_Published_KebabHostsClose()
    {
        var cut = RenderAuthoring(AssignmentAuthoringMode.View, MakeDto(AssignmentStatusDto.Published));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW"));
        cut.Find("fluent-button[title=\"More assignment actions\"]").Click();

        cut.FindAll("fluent-menu-item").Select(i => i.TextContent.Trim())
            .Should().Contain("Close");
    }

    [TestMethod]
    public void ActionBar_Archived_HasNoKebab()
    {
        var cut = RenderAuthoring(AssignmentAuthoringMode.View, MakeDto(AssignmentStatusDto.Archived));

        cut.WaitForAssertion(() =>
            cut.FindAll("fluent-button[title=\"More assignment actions\"]").Should().BeEmpty(
                "§11: an Archived assignment offers no secondary actions"));
    }

    // ── Criterion 6: disabled-with-reason, never hidden ────────────────────

    [TestMethod]
    public void TeacherGraded_ScoringFields_RenderDisabledWithReason()
    {
        var cut = RenderAuthoring(
            AssignmentAuthoringMode.Edit,
            MakeDto(AssignmentStatusDto.Draft, grading: GradingFormatDto.TeacherGraded));

        cut.WaitForAssertion(() =>
        {
            ScoringFieldDisabled(cut, "scoringFieldsPassScore").Should().BeTrue(
                "UX-17: an inapplicable field is disabled, never hidden");
            ScoringFieldDisabled(cut, "scoringFieldsMaxAttempts").Should().BeTrue();
            cut.Markup.Should().Contain(ScoringFieldsSection.ScoringInapplicableReason,
                "the inline reason explains why the fields cannot be edited");
        });
    }

    [TestMethod]
    public void ChangingGradingFormat_TogglesEnablement_NotPresence()
    {
        var cut = RenderAuthoring(
            AssignmentAuthoringMode.Edit,
            MakeDto(AssignmentStatusDto.Draft, grading: GradingFormatDto.TeacherGraded));

        cut.WaitForAssertion(() => ScoringFieldDisabled(cut, "scoringFieldsPassScore").Should().BeTrue());

        var gradingSelect = cut
            .FindComponents<FluentSelect<Authoring.PickerOption>>()
            .Single(s => s.Instance.Id == "authoring-basics-grading");

        cut.InvokeAsync(() => gradingSelect.Instance.SelectedOptionChanged.InvokeAsync(
            new Authoring.PickerOption(((int)GradingFormatDto.AutoGraded).ToString(), "Auto Scored")));

        cut.WaitForAssertion(() =>
        {
            ScoringFieldDisabled(cut, "scoringFieldsPassScore").Should().BeFalse(
                "AutoGraded makes the pass score applicable again");
            cut.FindAll("#scoringFieldsPassScore").Should().ContainSingle(
                "the field is still rendered — gating enables/disables, it never reflows the page");
            cut.Markup.Should().NotContain(ScoringFieldsSection.ScoringInapplicableReason);
        });
    }

    /// <summary>R2 (D-2/TGT-11): a SINGLE grade target auto-derives the primary grade, so the grade
    /// picker renders disabled WITH its reason (never hidden) and the value follows the target.</summary>
    [TestMethod]
    public async Task SingleGradeTarget_AutoDerivesThePrimaryGrade_AndDisablesThePicker()
    {
        var gradeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        _gradeLevels = [new GradeLevelDto(gradeId, Guid.NewGuid(), 5, "Grade 5", 0, 0, 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)];
        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(AssignmentStatusDto.Draft));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW"));

        await SelectGradeTargetAsync(cut, gradeId, "Grade 5");

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Set automatically from the single targeted grade level.");
            cut.FindComponents<FluentSelect<Authoring.PickerOption>>()
                .Single(s => s.Instance.Id == "authoring-basics-grade").Instance.Disabled
                .Should().BeTrue("the primary grade is derived from the single target, not authored");
        });
    }

    /// <summary>R2 (TGT-2/UX-17): with Everyone selected every constraint picker renders disabled
    /// WITH the inline reason (disabled-with-reason, never hidden).</summary>
    [TestMethod]
    public async Task EveryoneTarget_DisablesTheConstraintPickersWithReason()
    {
        // The Create-mode load re-mirrors the (still empty) target set as it lands, which would
        // wipe a toggle driven before it finished — so the load's own preview read (its last step)
        // is what the test waits for first.
        var previewRequests = 0;
        _mockHttp.When(HttpMethod.Get, "http://localhost/assignments/recipient-preview*")
            .Respond(_ =>
            {
                previewRequests++;
                return PreviewResponse(
                    "{\"studentsMatched\":0,\"primaryContacts\":0,\"otherContacts\":0,\"previewDegraded\":false}");
            });

        var cut = RenderAuthoring(AssignmentAuthoringMode.Create);

        cut.WaitForAssertion(() => previewRequests.Should().Be(1), TimeSpan.FromSeconds(5));

        await SelectEveryoneAsync(cut);

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("#authoring-audience-constraint-reason").Should().ContainSingle(
                "UX-17: inapplicable controls explain themselves inline");
            Picker(cut, "authoring-audience-groups").Disabled.Should().BeTrue(
                "TGT-2: AllStudents is mutually exclusive with every other kind");
        }, TimeSpan.FromSeconds(5));
    }

    /// <summary>R2 (TGT-16/EC-7): the chips are a pure projection of the loaded target rows —
    /// <c>TargetChips =&gt; _model.Targets…</c> — so Edit mode pins the persisted DisplayOrder (and
    /// each chip's label) with no picker interaction at all.</summary>
    [TestMethod]
    public void Edit_TargetChips_RenderInDisplayOrder()
    {
        var gradeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        _gradeLevels =
        [
            new GradeLevelDto(gradeId, Guid.NewGuid(), 5, "Grade 5", 0, 0, 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        ];
        _activityGroups = [Group(GroupAId, "Alpha")];

        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(AssignmentStatusDto.Draft),
            MakeChildren(targets:
            [
                new AssignmentTargetDto(TargetKindDto.ActivityGroup, GroupAId, 0),
                new AssignmentTargetDto(TargetKindDto.GradeLevel, gradeId, 1)
            ]));

        cut.WaitForAssertion(() =>
        {
            var chips = cut.FindAll(".authoring-target-chip");
            chips.Should().HaveCount(2, "each persisted target row renders exactly one chip");
            chips.Select(c => c.GetAttribute("data-order")).Should().Equal(new[] { "0", "1" },
                "the chips carry the persisted DisplayOrder");
            chips.Select(c => c.QuerySelector("fluent-badge")!.TextContent.Trim())
                .Should().Equal(new[] { "Alpha", "Grade 5" },
                    "each chip labels its target through the loaded picker options");
        });
    }

    /// <summary>R2 (TGT-16/D-4): the preview renders the server-resolved counts once the trailing
    /// debounce fires — driven by the injected fake clock, so the assertion never races real time.
    /// </summary>
    [TestMethod]
    public async Task RecipientPreview_RendersTheCountsAfterTheDebounce()
    {
        // The load-time read answers "nobody matched"; only the read the settled change issues
        // carries the counts, so the assertion below cannot pass on the load's own read.
        var previewRequests = 0;
        _mockHttp.When(HttpMethod.Get, "http://localhost/assignments/recipient-preview*")
            .Respond(_ => PreviewResponse(++previewRequests == 1
                ? "{\"studentsMatched\":0,\"primaryContacts\":0,\"otherContacts\":0,\"previewDegraded\":false}"
                : "{\"studentsMatched\":7,\"primaryContacts\":5,\"otherContacts\":2,\"previewDegraded\":false}"));

        var clock = new FakeTimeProvider();
        var cut = RenderAuthoring(AssignmentAuthoringMode.Create, timeProvider: clock);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Everyone"));

        // Settle the load's own debounce first, so the change below is the only pending schedule.
        await AdvanceUntilAsync(clock, () => previewRequests >= 1);

        // Awaited only under a bound (below): the callback's task completes once the debounce it
        // arms has fired.
        var changing = SelectEveryoneAsync(cut);

        // The event dispatch is queued through the renderer, so this no-op rides behind it — when
        // it runs the change has armed its trailing debounce, which is still unfired.
        await cut.InvokeAsync(() => { });
        previewRequests.Should().Be(1, "the trailing debounce is armed, not elapsed");

        clock.Advance(FakeTimeProvider.PreviewDebounce);
        await changing.WaitAsync(TimeSpan.FromSeconds(5));

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Students matched: 7");
            cut.Markup.Should().Contain("Contacts reachable: 7");
        }, TimeSpan.FromSeconds(2));

        previewRequests.Should().Be(2, "one preview read per settled debounce");
    }

    /// <summary>R2 (D-4) coalescing: a burst of constraint changes leaves exactly ONE trailing
    /// debounce alive, so the audience is read once per settle rather than once per change. The
    /// fake clock holds every timer, so a read can only happen when the test advances it — the
    /// cancellation is what the count measures.</summary>
    [TestMethod]
    public async Task RecipientPreview_RapidConstraintChanges_CoalesceIntoASingleRead()
    {
        var previewRequests = 0;
        _mockHttp.When(HttpMethod.Get, "http://localhost/assignments/recipient-preview*")
            .Respond(_ =>
            {
                previewRequests++;
                return PreviewResponse(
                    "{\"studentsMatched\":7,\"primaryContacts\":5,\"otherContacts\":2,\"previewDegraded\":false}");
            });

        var clock = new FakeTimeProvider();
        var cut = RenderAuthoring(AssignmentAuthoringMode.Create, timeProvider: clock);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Everyone"));

        // The load's own read settles first, so the burst below is the only debounce left.
        await AdvanceUntilAsync(clock, () => previewRequests >= 1);
        previewRequests.Should().Be(1);

        // Three constraint changes in quick succession (Everyone on → off → on). Each one
        // re-schedules the trailing debounce and cancels its predecessor.
        var burst = new[]
        {
            SelectEveryoneAsync(cut),
            SelectEveryoneAsync(cut, everyone: false),
            SelectEveryoneAsync(cut)
        };

        // A no-op queued behind all three dispatches: when it runs, every change has cancelled the
        // debounce before it and nothing has fired.
        await cut.InvokeAsync(() => { });
        previewRequests.Should().Be(1, "a held debounce reads nothing before the clock is advanced");
        clock.PendingTimers.Should().Be(1, "the burst left exactly ONE trailing debounce armed");

        clock.Advance(FakeTimeProvider.PreviewDebounce);
        await Task.WhenAll(burst).WaitAsync(TimeSpan.FromSeconds(5));

        cut.WaitForAssertion(() => previewRequests.Should().Be(2), TimeSpan.FromSeconds(2));
        previewRequests.Should().Be(2,
            "D-4 coalescing: three rapid constraint changes produce ONE preview read, not one per change");
        clock.PendingTimers.Should().Be(0, "nothing is left armed to fire a second read");
    }

    [TestMethod]
    public void OfflineType_RendersQuestionsCompartmentDisabledWithReason()
    {
        var cut = RenderAuthoring(
            AssignmentAuthoringMode.Edit,
            MakeDto(AssignmentStatusDto.Draft, type: AssignmentTypeDto.Manual, grading: GradingFormatDto.TeacherGraded));

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("#authoring-questions").Should().ContainSingle(
                "UX-19: the Questions & AI compartment never disappears");
            cut.Markup.Should().Contain(QuestionGenerationGate.DisabledHint,
                "the compartment explains why generation is unavailable");
        });
    }

    [TestMethod]
    public void ActivityGroupsFlagOff_DoesNotOfferTheGroupTarget()
    {
        _flags.ActivityGroupsEnabled = false;
        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(AssignmentStatusDto.Draft));

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().NotContain("By Group",
                "the group target must not be offered when FEATURE:EnableActivityGroups is off");
            cut.FindAll("#authoring-groups-reason").Should().ContainSingle(
                "the picker renders disabled-with-reason instead of disappearing");
        });
    }

    /// <summary>R2-7 (P2-b): when the preview read degrades, the compartment renders the
    /// advisory reason inline — save is never blocked.</summary>
    [TestMethod]
    public async Task RecipientPreview_Degraded_RendersTheUnavailableReason()
    {
        var previewRequests = 0;
        _mockHttp.When(HttpMethod.Get, "http://localhost/assignments/recipient-preview*")
            .Respond(_ =>
            {
                previewRequests++;
                return PreviewResponse(
                    "{\"studentsMatched\":0,\"primaryContacts\":0,\"otherContacts\":0,\"previewDegraded\":true}");
            });

        var clock = new FakeTimeProvider();
        var cut = RenderAuthoring(AssignmentAuthoringMode.Create, timeProvider: clock);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Everyone"));

        // The load's own read settles first (its debounce is armed from a posted continuation),
        // so the change below issues the only remaining read.
        await AdvanceUntilAsync(clock, () => previewRequests >= 1);

        var changing = SelectEveryoneAsync(cut);
        await cut.InvokeAsync(() => { });
        clock.Advance(FakeTimeProvider.PreviewDebounce);
        await changing.WaitAsync(TimeSpan.FromSeconds(5));

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain(Authoring.PreviewUnavailableReason,
                "D-4: a degraded preview shows the inline advisory note");
            cut.Markup.Should().NotContain("Students matched",
                "F10: a degraded read answers all-zero counts — they are never rendered as if trustworthy");
            cut.FindAll("#authoring-audience-preview").Should().BeEmpty(
                "F10: the reason replaces the counts line, it is never shown beside it");
        }, TimeSpan.FromSeconds(2));

        previewRequests.Should().Be(2, "the change's own settled debounce issued the degraded read");
    }

    /// <summary>R2-7 (P2-b / UX-21): an editable Edit surface whose persisted targets could
    /// not be loaded renders the audience pickers disabled-with-reason rather than live-empty.</summary>
    [TestMethod]
    public void Edit_TargetsLoadFails_DisablesAudiencePickersWithReason()
    {
        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(AssignmentStatusDto.Draft),
            childrenReadFails: true);

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain(Authoring.ChildrenUnavailableReason,
                "the audience editor shares the fail-closed reason with the children editors");
            cut.Find("#authoring-audience-everyone").HasAttribute("disabled").Should().BeTrue(
                "the Everyone toggle is disabled until the persisted targets are known");
            Picker(cut, "authoring-audience-grades").Disabled.Should().BeTrue(
                "the grade target picker is disabled until the persisted targets are known");
            Picker(cut, "authoring-audience-streams").Disabled.Should().BeTrue(
                "the stream target picker is disabled until the persisted targets are known");
        });
    }

    /// <summary>R2-9 (F1, P1 data-loss path): the persisted-targets read and the group-links read are
    /// SEPARATE calls, so the first can fail while the second succeeds. The group picker used to gate
    /// on <c>GroupPickerReason</c> alone — which does not carry the UX-21 "targets did not load"
    /// reason — so it stayed LIVE over an empty in-memory target set and a group selection then
    /// full-replaced the persisted rows on save. It now gates on the same reason the other three
    /// constraint pickers do.</summary>
    [TestMethod]
    public void Edit_TargetsLoadFails_GroupLinksLoadSucceeds_DisablesTheGroupPicker()
    {
        _activityGroups = [Group(GroupAId, "Alpha")];

        // Targets read 500; group-links read 200 WITH a link — the exact asymmetry of the defect.
        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(AssignmentStatusDto.Draft),
            childrenReadFails: true, linkedGroupIds: [GroupAId]);

        cut.WaitForAssertion(() =>
        {
            // Vacuity guard: the options DID load, so the disabled state below is the gate and not
            // an empty picker.
            cut.FindAll("#authoring-audience-groups fluent-option").Should().ContainSingle(
                "the group options loaded — the links read succeeded");

            Picker(cut, "authoring-audience-groups").Disabled.Should().BeTrue(
                "F1: the group picker is disabled whenever the other three constraint pickers are — " +
                "a group selection here would full-replace the unread persisted target rows on save");

            cut.FindAll("#authoring-audience-groups[disabled]").Should().ContainSingle(
                "the rendered control is inert, so no group target can be set at all");

            cut.FindAll("#authoring-audience-constraint-reason").Should().ContainSingle(
                "F1: the UX-21 case is explained by the AUDIENCE reason")
                .Which.TextContent.Should().Contain(Authoring.ChildrenUnavailableReason);

            cut.FindAll("#authoring-groups-reason").Should().BeEmpty(
                "F1: no misleading group-specific reason is invented for the UX-21 case");
        }, TimeSpan.FromSeconds(5));
    }

    /// <summary>R2-10 (P1, NFR-1): the group picker's <c>aria-describedby</c> names the reason
    /// paragraph that actually RENDERS. The attribute used to be the hard-coded
    /// <c>authoring-groups-reason</c>, which is absent in the UX-21 case (the picker is disabled by
    /// the constraint gate while <c>GroupPickerReason</c> is null), so the idref dangled and the
    /// disabled picker exposed no reason at all.</summary>
    [TestMethod]
    public void Edit_TargetsLoadFails_GroupLinksLoadSucceeds_GroupPickerNamesTheRenderedReason()
    {
        _activityGroups = [Group(GroupAId, "Alpha")];

        // Targets read 500; group-links read 200 — the exact UX-21 asymmetry.
        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(AssignmentStatusDto.Draft),
            childrenReadFails: true, linkedGroupIds: [GroupAId]);

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("#authoring-groups-reason").Should().BeEmpty(
                "vacuity guard: the idref the attribute must not use is genuinely absent from the DOM");

            cut.FindAll("#authoring-audience-groups[disabled]").Should().ContainSingle(
                "the picker really is disabled — NFR-1 binds exactly on the disabled control");

            cut.Find("#authoring-audience-groups").GetAttribute("aria-describedby")
                .Should().Be("authoring-audience-constraint-reason",
                    "NFR-1/P1: the disabled picker names the reason paragraph that renders");
        }, TimeSpan.FromSeconds(5));
    }

    /// <summary>R2-9 (F4): Everyone is mutually exclusive with the constraints, so switching it ON
    /// necessarily replaces them — switching it back OFF must restore what it replaced instead of
    /// leaving an empty set (the prior constraints were discarded without a word).</summary>
    [TestMethod]
    public async Task EveryoneOff_RestoresTheConstraintsEveryoneReplaced()
    {
        var gradeA = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var gradeB = Guid.Parse("99999999-9999-9999-9999-999999999999");
        _gradeLevels =
        [
            new GradeLevelDto(gradeA, Guid.NewGuid(), 5, "Grade 5", 0, 0, 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            new GradeLevelDto(gradeB, Guid.NewGuid(), 6, "Grade 6", 0, 0, 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        ];

        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(AssignmentStatusDto.Draft));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW"));

        await SelectGradeTargetsAsync(cut, (gradeA, "Grade 5"), (gradeB, "Grade 6"));
        cut.WaitForAssertion(() => ChipLabels(cut).Should().Equal("Grade 5", "Grade 6"),
            TimeSpan.FromSeconds(5));

        await SelectEveryoneAsync(cut);
        cut.WaitForAssertion(() => ChipLabels(cut).Should().Equal("Everyone"),
            TimeSpan.FromSeconds(5));

        await SelectEveryoneAsync(cut, everyone: false);

        cut.WaitForAssertion(() =>
        {
            ChipLabels(cut).Should().Equal(new[] { "Grade 5", "Grade 6" },
                "F4: Everyone OFF restores the constraints it replaced — the authored work is kept");
            Picker(cut, "authoring-audience-grades").SelectedOptions!.Select(o => o.Value)
                .Should().BeEquivalentTo([gradeA.ToString(), gradeB.ToString()],
                    "the restored set is mirrored back onto the grade picker, not just onto the chips");
        }, TimeSpan.FromSeconds(5));
    }

    /// <summary>R2-9 (F6/F8 + F9/F14, NFR-1): each control the Audience compartment disables names
    /// the reason paragraph that explains it, and the constraint rows are top-aligned (the students
    /// typeahead is a tall composite beside single-line siblings).</summary>
    [TestMethod]
    public async Task AudienceCompartment_DisabledControlsNameTheirReason_AndConstraintRowsAlignTop()
    {
        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(AssignmentStatusDto.Draft));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW"));

        cut.FindAll(".form-row--align-top").Should().HaveCount(4,
            "F9/F14: the four constraint rows (grades, streams, students, activity groups) are top-aligned");

        await SelectEveryoneAsync(cut);

        cut.WaitForAssertion(() =>
        {
            foreach (var id in new[]
                     {
                         "authoring-audience-grades",
                         "authoring-audience-streams",
                         "authoring-audience-groups"
                     })
            {
                cut.Find($"#{id}").GetAttribute("aria-describedby").Should().NotBeNull(
                    $"NFR-1: the disabled {id} names the reason paragraph that explains it");
            }

            cut.Find("#authoring-audience-groups").GetAttribute("aria-describedby")
                .Should().Be("authoring-groups-reason",
                    "NFR-1: the group picker points at its own reason paragraph");
            cut.Find("#authoring-audience-everyone").GetAttribute("aria-describedby")
                .Should().Be("authoring-audience-constraint-reason");
        }, TimeSpan.FromSeconds(5));

        // NFR-1 (F6): the chip Remove button carries the chip it removes in its accessible name.
        cut.FindAll(".authoring-target-chip fluent-button")
            .Select(b => b.GetAttribute("aria-label"))
            .Should().Contain("Remove Everyone",
                "the visible \"Remove\" text alone is ambiguous across the chip list");
    }

    /// <summary>R2-9 (F11): the preview resolves on an editable surface only, so a read-only View
    /// renders a stable author-only note instead of a "Preview pending…" that can never settle.</summary>
    [TestMethod]
    public void ViewMode_Preview_RendersTheAuthorOnlyNoteInsteadOfAPermanentPending()
    {
        var cut = RenderAuthoring(AssignmentAuthoringMode.View, MakeDto(AssignmentStatusDto.Published));

        cut.WaitForAssertion(() =>
        {
            cut.Find("#authoring-audience-preview").TextContent.Trim()
                .Should().Be(Authoring.PreviewAuthorOnlyNote);
            cut.Markup.Should().NotContain("Preview pending…");
            cut.FindAll("#authoring-audience-preview-note").Should().BeEmpty();
        });
    }

    // ── Criterion 7: policy values are read-only with the inherited badge ───

    [TestMethod]
    public void PolicyDerivedFields_RenderReadOnlyWithTheInheritedBadge()
    {
        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(AssignmentStatusDto.Draft));

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll(".authoring-policy-row");
            rows.Should().HaveCount(3, "approval, notification and signature policy rows (UX-13/UX-15/UX-16)");
            cut.Markup.Should().Contain(Authoring.InheritedPolicyBadgeText);

            foreach (var row in rows)
            {
                row.QuerySelectorAll("input, fluent-text-field, fluent-number-field, fluent-select, fluent-checkbox, fluent-date-picker, fluent-listbox, fluent-text-area")
                    .Should().BeEmpty("UX-13: a policy-derived value is never an editable control");
            }
        });
    }

    [TestMethod]
    public void PolicySignatureRow_ReflectsTheResolvedRequirement()
    {
        _signatureDefaultBody = "{\"requiresSignature\":true,\"signatureMode\":\"Mandatory\"}";

        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(AssignmentStatusDto.Draft));

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("A guardian signature is required after completion");
            cut.FindComponents<FluentCheckbox>()
                .Single(c => c.Instance.Id == "authoring-submission-requires-signature").Instance.Disabled
                .Should().BeTrue("a Mandatory policy locks the per-assignment override");
            cut.Markup.Should().Contain(Authoring.SignatureMandatoryReason,
                "the disabled checkbox explains why it cannot be unticked");
        });
    }

    // ── P1 rework: Edit loads the persisted children (the load half of parity) ─────

    private static AssignmentQuestionReadDto LoadedQuestion() =>
        new(Guid.NewGuid(), "Loaded question?", QuestionTypeDto.MultipleChoice, DisplayOrder: 0,
            ModelAnswer: null,
            Options:
            [
                new AssignmentQuestionOptionReadDto(Guid.NewGuid(), "A", true),
                new AssignmentQuestionOptionReadDto(Guid.NewGuid(), "B", false)
            ]);

    private static AssignmentAttachmentReadDto LoadedAttachment() =>
        new(Guid.NewGuid(), "syllabus.pdf", "application/pdf", 2048, "tenants/t/staging/syllabus.pdf");

    private static ResourceDto LoadedUrlResource(Guid assignmentId) =>
        new(Guid.NewGuid(), assignmentId, ResourceKindDto.Url, "https://example.com/ref", null, "Ref", true);

    [TestMethod]
    public void Edit_LoadsThePersistedChildrenIntoTheEditors()
    {
        var dto = MakeDto(AssignmentStatusDto.Draft);
        var children = MakeChildren(
            questions: [LoadedQuestion()],
            attachments: [LoadedAttachment()],
            resources: [LoadedUrlResource(dto.Id)]);

        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, dto, children: children);

        cut.WaitForAssertion(() =>
        {
            var model = cut.FindComponents<QuestionEditorSection>().Single().Instance.Model;
            model.Questions.Should().ContainSingle("the persisted question is loaded before the editor renders");
            model.Questions[0].QuestionText.Should().Be("Loaded question?");
            model.Questions[0].CorrectOptionIndex.Should().Be(0);
            model.Attachments.Should().ContainSingle();
            model.Attachments[0].FileName.Should().Be("syllabus.pdf");
            model.ResourceUrls.Should().ContainSingle();
            model.ResourceUrls[0].Url.Should().Be("https://example.com/ref");

            cut.Markup.Should().Contain("Loaded question?", "the loaded question renders in the editor");
            cut.Markup.Should().Contain("syllabus.pdf");
            cut.Markup.Should().Contain("https://example.com/ref");
        });
    }

    /// <summary>
    /// The P1 data-loss shape under test end-to-end: adding ONE question in Edit must send the
    /// loaded set plus the new row, never only the new one (the update handler full-replaces
    /// any non-null question collection).
    /// </summary>
    [TestMethod]
    public async Task Edit_AddingAQuestion_SendsTheLoadedSetPlusTheNewOne()
    {
        var dto = MakeDto(AssignmentStatusDto.Draft);
        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, dto,
            children: MakeChildren(questions: [LoadedQuestion()], attachments: [LoadedAttachment()],
                // TGT-13: at least one target is required before the save gate opens.
                targets: [new AssignmentTargetDto(TargetKindDto.Stream, Guid.NewGuid(), 0)]));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Loaded question?"));

        // Add one VALID question through the editor section's own seam (a blank row would be
        // rejected by the submit gate before any request is sent).
        var row = new QuestionEditorRow { QuestionText = "Added question?", Type = QuestionTypeDto.ShortAnswer, ModelAnswer = "42" };
        var section = cut.FindComponents<QuestionEditorSection>().Single();
        await cut.InvokeAsync(() => section.Instance.Model.AddQuestion(row));

        string? updateBody = null;
        _mockHttp.Expect(HttpMethod.Put, $"http://localhost/assignments/{dto.Id}")
            .With(req =>
            {
                updateBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return true;
            })
            .Respond(HttpStatusCode.NoContent);

        SaveDraft(cut);

        cut.WaitForAssertion(() => updateBody.Should().NotBeNull(), TimeSpan.FromSeconds(5));

        updateBody.Should().Contain("Loaded question?",
            "the update request carries the loaded question — not only the newly added one");
        updateBody.Should().Contain("Added question?");
        updateBody.Should().Contain("syllabus.pdf", "the loaded attachment is re-projected, not dropped");
    }

    [TestMethod]
    public void Edit_ChildrenReadFails_RendersTheEditorsDisabledWithReason()
    {
        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(AssignmentStatusDto.Draft),
            childrenReadFails: true);

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("#authoring-content-reason").Should().ContainSingle(
                "fail-closed: the Content & Resources editors never render live-empty");
            cut.FindAll("#authoring-questions-reason").Should().ContainSingle();
            cut.Markup.Should().Contain(Authoring.ChildrenUnavailableReason);
            cut.FindComponents<QuestionEditorSection>().Should().BeEmpty(
                "an add against a blank editor is what deleted the persisted children");
            cut.FindComponents<ResourcesSection>().Should().BeEmpty();
            cut.Markup.Should().NotContain("No questions yet", "no empty-state editor is exposed either");
        });
    }

    [TestMethod]
    public void Create_ChildrenReadIsNotAttempted_AndTheEditorsStayLive()
    {
        var cut = RenderAuthoring(AssignmentAuthoringMode.Create);

        cut.WaitForAssertion(() => cut.FindComponents<QuestionEditorSection>().Should().NotBeEmpty(
            "Create has no persisted children to lose, so its editors stay live"));
        cut.FindAll("#authoring-content-reason").Should().BeEmpty();
    }

    // ── P1 rework (supervisor decision (a)): the group picker's binding is real again ──

    private static Guid GroupAId { get; } = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111");

    private static Guid GroupBId { get; } = Guid.Parse("bbbbbbbb-2222-2222-2222-222222222222");

    /// <summary>Stubs the FR-58 subject union the group picker's selection resolves against.</summary>
    private void SetupGroupSubjects(params Guid[] topicIds)
    {
        _mockHttp.When(HttpMethod.Get, "http://localhost/students/subjects/by-group/*")
            .Respond(HttpStatusCode.OK, "application/json", JsonSerializer.Serialize(
                topicIds.Select(id => new { id, name = "Mathematics", displayOrder = 0 }), _apiJsonOptions));
    }

    /// <summary>Clicks the Edit action bar's "Save draft" kebab item (the update path).</summary>
    private static void SaveDraft(IRenderedComponent<Authoring> cut)
    {
        cut.Find("fluent-button[title=\"More assignment actions\"]").Click();
        cut.FindAll("fluent-menu-item").Single(i => i.TextContent.Trim() == "Save draft").Click();
    }

    /// <summary>
    /// Regression (supervisor decision (a)): the group picker bound
    /// <c>@bind-SelectedValues</c>, which does not exist on the FluentUI list components —
    /// Blazor dropped it into the catch-all <c>AdditionalAttributes</c> and the element
    /// rendered a stray <c>selectedvalues</c> attribute while never reporting a selection.
    /// </summary>
    [TestMethod]
    public void GroupPicker_BindsTheSupportedApi_AndRendersNoStrayAttribute()
    {
        // Both modes the picker renders in: Create (empty selection) and Edit (a loaded link).
        foreach (var mode in new[] { AssignmentAuthoringMode.Create, AssignmentAuthoringMode.Edit })
        {
            _activityGroups = [Group(GroupAId, "Alpha")];
            var cut = mode == AssignmentAuthoringMode.Create
                ? RenderAuthoring(mode)
                : RenderAuthoring(mode,
                    MakeDto(AssignmentStatusDto.Draft, audience: TargetAudienceTypeDto.SelectedGroups),
                    MakeChildren(targets: [new AssignmentTargetDto(TargetKindDto.ActivityGroup, GroupAId, 0)]),
                    linkedGroupIds: [GroupAId]);

            cut.WaitForAssertion(() =>
            {
                cut.Markup.Should().NotContain("selectedvalues",
                    "the dead @bind-SelectedValues attribute must not leak into the DOM");
                cut.Markup.Should().NotContain("fluent-listbox",
                    "the supported control is the multi-select FluentSelect (the skills' multi-select route)");
                cut.Find("#authoring-audience-groups").HasAttribute("multiple").Should().BeTrue();
                cut.FindAll("#authoring-audience-groups fluent-option").Should().ContainSingle(
                    "the groups are offered by the multi-select picker");
            });
        }
    }

    /// <summary>Requirement 4(i): Create must ACCEPT a group selection and submit it — with the
    /// dead binding the author could never satisfy the By-Group audience.</summary>
    [TestMethod]
    public async Task Create_SelectingGroups_SubmitsTheAudienceAndLinksTheGroups()
    {
        var groupA = Group(GroupAId, "Alpha");
        _activityGroups = [groupA];
        var newAssignmentId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var topicId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        SetupGroupSubjects(topicId);

        var cut = RenderAuthoring(AssignmentAuthoringMode.Create);
        cut.WaitForAssertion(() => cut.FindAll("#authoring-audience-groups fluent-option").Should().ContainSingle());

        await SetTitleAsync(cut, "Group-targeted HW");
        await SelectGroupsAsync(cut, groupA);
        await SelectAsync(cut, "authoring-basics-subject", topicId.ToString(), "Mathematics");

        string? createBody = null;
        _mockHttp.Expect(HttpMethod.Post, "http://localhost/assignments")
            .With(req =>
            {
                createBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return true;
            })
            .Respond(HttpStatusCode.OK, "application/json", $"\"{newAssignmentId}\"");

        string? linkBody = null;
        _mockHttp.Expect(HttpMethod.Put, $"http://localhost/assignments/{newAssignmentId}/groups")
            .With(req =>
            {
                linkBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return true;
            })
            .Respond(HttpStatusCode.NoContent);

        cut.Find("#authoring-primary-action").Click();

        cut.WaitForAssertion(() =>
        {
            createBody.Should().NotBeNull("ValidateForSave must accept the group selection and create the assignment");
            linkBody.Should().NotBeNull("the selected groups are linked after the create");
        }, TimeSpan.FromSeconds(5));

        createBody.Should().Contain("\"targetAudienceType\":\"SelectedGroups\"",
            "R2 (D-1): the compat audience is DERIVED from the authored group target");
        createBody.Should().Contain(GroupAId.ToString(),
            "the authored target rows ride the create payload (TGT-1)");
        linkBody.Should().Contain(GroupAId.ToString(), "the picker's selection reaches the link route");
        cut.Markup.Should().NotContain("Select at least one activity group.",
            "ValidateForSave accepts the selection the repaired picker reports");
    }

    /// <summary>Requirement 4(ii): the loaded links render as SELECTED (the load half of P2-1).</summary>
    [TestMethod]
    public void Edit_LoadedLinks_RenderAsSelectedInThePicker()
    {
        _activityGroups = [Group(GroupAId, "Alpha"), Group(GroupBId, "Beta")];
        var dto = MakeDto(AssignmentStatusDto.Draft, audience: TargetAudienceTypeDto.SelectedGroups);

        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, dto,
            MakeChildren(targets: [new AssignmentTargetDto(TargetKindDto.ActivityGroup, GroupAId, 0)]),
            linkedGroupIds: [GroupAId]);

        cut.WaitForAssertion(() =>
        {
            Picker(cut, "authoring-audience-groups").SelectedOptions
                .Should().ContainSingle(o => o.Value == GroupAId.ToString(),
                    "the persisted TARGET row is loaded into the picker instead of an empty selection");
            cut.FindAll("#authoring-audience-groups fluent-option[aria-selected=\"true\"]")
                .Should().ContainSingle(o => o.TextContent.Contains("Alpha"),
                    "the loaded link is marked selected in the rendered picker");
        });
    }

    /// <summary>Requirement 4(iii), the fail-safe half: an untouched picker never issues the
    /// replace-set call (which deletes every link missing from its payload).</summary>
    [TestMethod]
    public async Task Edit_ReassertingTheSameGroups_DoesNotRewriteTheLinks()
    {
        var groupA = Group(GroupAId, "Alpha");
        _activityGroups = [groupA];
        var dto = MakeDto(AssignmentStatusDto.Draft, audience: TargetAudienceTypeDto.SelectedGroups);
        SetupGroupSubjects(dto.TopicId);

        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, dto,
            MakeChildren(targets: [new AssignmentTargetDto(TargetKindDto.ActivityGroup, GroupAId, 0)]),
            linkedGroupIds: [GroupAId]);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW"));

        // The picker reports the SAME set it loaded (a click that changes nothing): the
        // subject union reloads but the link set is unchanged.

        await SelectSubjectAsync(cut, dto.TopicId);

        string? updateBody = null;
        string? linkBody = null;
        CaptureRequest(HttpMethod.Put, $"http://localhost/assignments/{dto.Id}/groups", body => linkBody = body);
        CaptureRequest(HttpMethod.Put, $"http://localhost/assignments/{dto.Id}", body => updateBody = body);

        SaveDraft(cut);

        cut.WaitForAssertion(() => updateBody.Should().NotBeNull(), TimeSpan.FromSeconds(5));
        linkBody.Should().BeNull(
            "an unchanged picker never issues the replace-set call — the route deletes every link missing from its payload");
    }

    /// <summary>Requirement 4(iii), the write half: an ACTUAL edit of the picker is persisted.</summary>
    [TestMethod]
    public async Task Edit_ChangingTheGroups_PersistsTheReplaceSet()
    {
        var groupA = Group(GroupAId, "Alpha");
        var groupB = Group(GroupBId, "Beta");
        _activityGroups = [groupA, groupB];
        var dto = MakeDto(AssignmentStatusDto.Draft, audience: TargetAudienceTypeDto.SelectedGroups);
        SetupGroupSubjects(dto.TopicId);

        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, dto,
            MakeChildren(targets: [new AssignmentTargetDto(TargetKindDto.ActivityGroup, GroupAId, 0)]),
            linkedGroupIds: [GroupAId]);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW"));

        await SelectGroupsAsync(cut, groupA, groupB);
        await SelectSubjectAsync(cut, dto.TopicId);

        string? linkBody = null;
        CaptureRequest(HttpMethod.Put, $"http://localhost/assignments/{dto.Id}/groups", body => linkBody = body);
        CaptureRequest(HttpMethod.Put, $"http://localhost/assignments/{dto.Id}", _ => { });

        SaveDraft(cut);

        cut.WaitForAssertion(() => linkBody.Should().NotBeNull(
            "an edited picker is what makes the links non-stale (the reviewer's P2-1)"), TimeSpan.FromSeconds(5));
        linkBody.Should().Contain(GroupAId.ToString()).And.Contain(GroupBId.ToString());
    }

    /// <summary>A fresh recipient-preview response per read (the mock re-sends it on every
    /// call, so a test can count the reads a settle produced).</summary>
    private static HttpResponseMessage PreviewResponse(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    /// <summary>Registers an asserting HTTP route that hands the request body to the test
    /// (the AssignmentCreateBunitTests capture pattern, in a reusable shape). Specific routes
    /// are registered before any generic one — MockHttp v6 matches in registration order.</summary>
    private void CaptureRequest(HttpMethod method, string url, Action<string?> capture)
    {
        _mockHttp.When(method, url)
            .With(req =>
            {
                capture(req.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                return true;
            })
            .Respond(HttpStatusCode.NoContent);
    }

    /// <summary>Requirement 4(iii), the fail-closed half: an unknown link set is never written
    /// over (the picker disables with a reason and the save path skips the replace-set).</summary>
    [TestMethod]
    public void Edit_LinkedGroupsReadFails_DisablesThePickerWithReason()
    {
        // The link read is stubbed to 500 for this dto only; every other route the Edit surface
        // touches is registered here (this test registers its own routes instead of calling
        // SetupAssignment, whose /groups route returns 200).
        _activityGroups = [Group(GroupAId, "Alpha")];
        var dto = MakeDto(AssignmentStatusDto.Draft, audience: TargetAudienceTypeDto.SelectedGroups);
        _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{dto.Id}/authoring")
            .Respond(HttpStatusCode.OK, "application/json", JsonSerializer.Serialize(MakeChildren(), _apiJsonOptions));
        _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{dto.Id}/groups")
            .Respond(HttpStatusCode.InternalServerError);
        _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{dto.Id}/questions-draft")
            .Respond(HttpStatusCode.OK, "application/json", "[]");
        _mockHttp.When(HttpMethod.Get, "http://localhost/students/subjects/by-grade/*")
            .Respond(HttpStatusCode.OK, "application/json", "[]");
        _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{dto.Id}")
            .Respond(HttpStatusCode.OK, "application/json", JsonSerializer.Serialize(dto, _apiJsonOptions));

        var cut = Render<Authoring>(parameters =>
        {
            parameters.Add(p => p.Mode, AssignmentAuthoringMode.Edit);
            parameters.Add(p => p.Id, dto.Id);
        });

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain(Authoring.GroupLinksUnavailableReason);
            Picker(cut, "authoring-audience-groups").Disabled.Should().BeTrue(
                "an unknown link set must not be collectable — saving would replace the persisted links");
        });
    }

    /// <summary>Requirement 4(i)-adjacent: the empty-string Title path is unchanged; this asserts
    /// the group audience still needs a selection once the picker reports one — i.e. the
    /// validation gate is satisfied by the picker rather than bypassed.</summary>
    [TestMethod]
    public async Task Create_GroupAudienceWithoutGroupSubjects_BlocksTheSave()
    {
        var groupA = Group(GroupAId, "Alpha");
        _activityGroups = [groupA];
        SetupGroupSubjects(); // the group resolves NO subject

        var cut = RenderAuthoring(AssignmentAuthoringMode.Create);
        cut.WaitForAssertion(() => cut.FindAll("#authoring-audience-groups fluent-option").Should().ContainSingle());

        await SetTitleAsync(cut, "Group-targeted HW");
        await SelectGroupsAsync(cut, groupA);

        string? createBody = null;
        CaptureRequest(HttpMethod.Post, "http://localhost/assignments", body => createBody = body);

        cut.Find("#authoring-primary-action").Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Select a subject."),
            TimeSpan.FromSeconds(5));
        createBody.Should().BeNull("the FR-58 union gate blocks the save before any request is sent");
    }

    // ── UX-7 (D2/D3): the unsaved-changes guard ──────────────────────────────

    /// <summary>
    /// Registers a mocked <see cref="IDialogService"/> whose confirmation outcome is fixed
    /// (<c>true</c> = the discard was confirmed) and which records every confirmation the page asks
    /// for (the <c>PeriodEditPageTests</c> / <c>ContactsEditorTests</c> pattern; the mocked service
    /// must be registered BEFORE rendering, because the page resolves it at construction). The
    /// <see cref="ConfirmDialogContent"/> is what <c>ShowConfirmDialogAsync</c> hands FluentUI, so
    /// recording it pins the wording and the button roles the author actually reads.
    /// </summary>
    private void RegisterConfirmationDialog(bool confirm, List<ConfirmDialogContent> captured)
    {
        var dialogRef = new Mock<IDialogReference>();
        dialogRef.SetupGet(r => r.Result).Returns(Task.FromResult(
            confirm ? DialogResult.Ok<object>(null!) : DialogResult.Cancel()));

        var dialogMock = new Mock<IDialogService>();
        dialogMock
            .Setup(d => d.ShowDialogAsync<ConfirmDialog, ConfirmDialogContent>(
                It.IsAny<ConfirmDialogContent>(), It.IsAny<DialogParameters>()))
            .Callback<ConfirmDialogContent, DialogParameters>((content, _) => captured.Add(content))
            .ReturnsAsync(dialogRef.Object);

        Services.AddSingleton(dialogMock.Object);
    }

    /// <summary>
    /// Mocks the PublishDialog shell open so the primary action's publish round trip completes:
    /// <c>ShowShellDialogAsync&lt;PublishDialog, PublishFormModel, PublishResult&gt;</c> resolves to
    /// <c>ShowDialogAsync&lt;PublishDialog, DialogShellData&lt;PublishFormModel&gt;&gt;</c>, and the
    /// dialog Result carries the <see cref="DialogShellResult{TResult}"/> the extension unwraps into
    /// the typed result (the <c>AssignmentDetailBunitTests</c> schedule-dialog pattern).
    /// </summary>
    private void RegisterPublishDialog()
    {
        var dialogRef = new Mock<IDialogReference>();
        var payload = new DialogShellResult<PublishResult>(new PublishResult(null));
        dialogRef.SetupGet(r => r.Result).Returns(Task.FromResult(DialogResult.Ok<object?>(payload)));

        var dialogMock = new Mock<IDialogService>();
        dialogMock
            .Setup(d => d.ShowDialogAsync<PublishDialog, DialogShellData<PublishFormModel>>(
                It.IsAny<DialogShellData<PublishFormModel>>(), It.IsAny<DialogParameters>()))
            .ReturnsAsync(dialogRef.Object);

        Services.AddSingleton(dialogMock.Object);
    }

    /// <summary>The rendered fragment's navigation target, i.e. what an in-app navigation would
    /// commit to if no location-changing handler cancelled it.</summary>
    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    /// <summary>UX-7 criterion 1 (D2 in-app half): a navigation away from a form with unsaved work is
    /// cancelled and confirmed through the shared FluentUI dialog. The target here is ANOTHER
    /// assignment's edit URL — the exact reuse case F17 covers — so the guard is what stands between
    /// the author and their unsaved work being replaced.
    /// <para>Discriminating: against `66c2676a` the page registered no handler, so the fake
    /// navigation committed and no confirmation was ever asked for.</para></summary>
    [TestMethod]
    public async Task UnsavedChanges_InAppNavigation_IsCancelledAndConfirmed()
    {
        var captured = new List<ConfirmDialogContent>();
        RegisterConfirmationDialog(confirm: false, captured);

        var dto = MakeDto(AssignmentStatusDto.Draft);
        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, dto);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW"));

        await SetTitleAsync(cut, "Edited title");

        var before = Navigation.Uri;
        Navigation.NavigateTo($"/assignments/{Guid.NewGuid()}/edit");

        cut.WaitForAssertion(() => captured.Should().HaveCount(1,
            "UX-7: leaving a form with unsaved changes raises the confirmation"));
        captured[0].Message.Should().Be(Authoring.UnsavedChangesMessage);
        captured[0].PrimaryText.Should().Be(Authoring.UnsavedChangesDiscardAction);
        captured[0].SecondaryText.Should().Be(Authoring.UnsavedChangesKeepEditingAction);

        Navigation.Uri.Should().Be(before,
            "the navigation was cancelled — a guard that prompts but navigates anyway has lost the work " +
            "it was asked to protect");
        cut.Markup.Should().Contain("Edited title", "the authored value is still on screen");
    }

    /// <summary>UX-7 criterion 1 (the fail-safe half): a form with NOTHING unsaved navigates through
    /// untouched — a guard that always prompts is as broken as none at all, and asking here would
    /// train the author to dismiss the one prompt that matters.
    /// <para>Not itself discriminating (both trees navigate); it is the non-vacuity anchor for the
    /// interception above.</para></summary>
    [TestMethod]
    public void UnchangedForm_InAppNavigation_ProceedsWithoutAConfirmation()
    {
        var captured = new List<ConfirmDialogContent>();
        RegisterConfirmationDialog(confirm: false, captured);

        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(AssignmentStatusDto.Draft));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW"));

        Navigation.NavigateTo("/assignments");

        cut.WaitForAssertion(() => Navigation.Uri.Should().EndWith("/assignments"));
        captured.Should().BeEmpty("a clean form is never questioned");
    }

    /// <summary>UX-7 (D2): confirming the discard lets the navigation through — the guard cancels
    /// only on a non-discard outcome. Discriminating: against `66c2676a` no confirmation was raised
    /// at all (the second assertion), so the interception's contract is not vacuous.
    /// <para>The confirmation is asked for as part of the navigation; the navigation then proceeds on
    /// the same call, so the "asked" and "proceeded" halves are asserted together.</para></summary>
    [TestMethod]
    public async Task UnsavedChanges_DiscardConfirmed_NavigationProceeds()
    {
        var captured = new List<ConfirmDialogContent>();
        RegisterConfirmationDialog(confirm: true, captured);

        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(AssignmentStatusDto.Draft));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW"));
        await SetTitleAsync(cut, "Edited title");

        Navigation.NavigateTo("/assignments");

        cut.WaitForAssertion(() => Navigation.Uri.Should().EndWith("/assignments"));
        captured.Should().HaveCount(1, "the discard was confirmed only after the prompt was raised");
    }

    /// <summary>UX-7 criterion 2 (D3, the baseline comparison): a value changed and then changed back
    /// to what was loaded is NOT dirty, so no guard remains — neither the browser-unload listener nor
    /// an in-app prompt. This is the test that proves the guard compares against a baseline rather
    /// than carrying an edit flag: with a flag the revert would leave a phantom prompt behind.
    /// <para>Discriminating: against `66c2676a` the collocated module was never invoked at all, so the
    /// register/unregister pair the assertions hinge on does not exist.</para></summary>
    [TestMethod]
    public async Task ChangedThenRevertedValue_IsNotDirty_SoNoGuardRemains()
    {
        var captured = new List<ConfirmDialogContent>();
        RegisterConfirmationDialog(confirm: false, captured);
        var module = JSInterop.SetupModule(Authoring.BeforeUnloadModulePath);

        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(AssignmentStatusDto.Draft));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW"));

        await SetTitleAsync(cut, "Edited title");
        cut.WaitForAssertion(() => module.Invocations.Should()
            .Contain(i => i.Identifier == "registerBeforeUnload",
                "a real edit arms the browser-unload guard"));

        await SetTitleAsync(cut, "Math HW");
        cut.WaitForAssertion(() => module.Invocations.Should()
            .Contain(i => i.Identifier == "unregisterBeforeUnload",
                "D3: back at the loaded value there is nothing to lose, so the guard is detached"));

        Navigation.NavigateTo("/assignments");

        // The guard is genuinely gone rather than merely quiet: the navigation commits with no prompt.
        cut.WaitForAssertion(() => Navigation.Uri.Should().EndWith("/assignments"));
        captured.Should().BeEmpty("no phantom prompt for an edit that was undone");
    }

    /// <summary>UX-7 criterion 3 (D2 browser-unload half): the collocated module is imported and told
    /// to register exactly while the form is dirty, and detached again on disposal. The browser's own
    /// prompt cannot be asserted from here (the platform owns it, and the handler is not allowed to
    /// show anything else) — what IS assertable, and is what this test pins, is the dirty-state
    /// contract across the boundary and the listener's lifetime.
    /// <para>Discriminating: against `66c2676a` the module was never imported, so both invocations the
    /// test waits for are absent.</para></summary>
    [TestMethod]
    public async Task UnsavedChanges_UnloadGuard_IsAttachedWhileDirty_AndDetachedOnDispose()
    {
        var module = JSInterop.SetupModule(Authoring.BeforeUnloadModulePath);

        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(AssignmentStatusDto.Draft));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW"));

        module.Invocations.Should().BeEmpty(
            "a form with nothing unsaved does not even import the module — there is no listener to own");

        await SetTitleAsync(cut, "Edited title");
        cut.WaitForAssertion(() => module.Invocations.Should()
            .ContainSingle(i => i.Identifier == "registerBeforeUnload"));

        await cut.Instance.DisposeAsync();

        module.Invocations.Should().Contain(i => i.Identifier == "unregisterBeforeUnload",
            "the listener is detached with the component, so it cannot outlive the page it guards");
    }

    /// <summary>UX-7 criterion 3 (P1 rework — the dirty→read-only transition): a lifecycle action
    /// that ends the author's participation in the form (Publish here; Unpublish/Close/Archive are
    /// the same transition) flips <c>EffectiveMode</c> to View. The listener was attached while the
    /// form was dirty and the form is now clean, so the guard MUST detach — a beforeunload prompt on
    /// a page with nothing unsaved is exactly the false alarm the criterion forbids, and it would
    /// train the author to dismiss the one prompt that matters.
    /// <para>The transition is driven through the real primary action and its API round trip (the
    /// reloaded detail read reports Published), not by mutating state: that is the path on which the
    /// listener was left armed.</para>
    /// <para>Discriminating: the P1 pre-fix code returned early on <c>IsReadOnly</c> — before the
    /// <c>dirty == _beforeUnloadRegistered</c> check and the unregister call — so the only
    /// <c>unregisterBeforeUnload</c> left is the disposal one, which this test does not reach
    /// (verified failing against that revision before the fix was applied).</para></summary>
    [TestMethod]
    public async Task ReadOnlyTransition_UnregistersTheUnloadGuard_SoACleanPageDoesNotPrompt()
    {
        var module = JSInterop.SetupModule(Authoring.BeforeUnloadModulePath);
        var dto = MakeDto(AssignmentStatusDto.Draft);

        // The detail read answers from a variable so the publish POST can move the assignment to
        // Published: the reload the lifecycle action performs then lands the surface in View mode.
        // Registered BEFORE RenderAuthoring's SetupAssignment because MockHttp v6 matches in
        // registration order.
        _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{dto.Id}")
            .Respond(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(dto, _apiJsonOptions), Encoding.UTF8, "application/json")
            });
        _mockHttp.When(HttpMethod.Post, $"http://localhost/assignments/{dto.Id}/publish")
            .Respond(_ =>
            {
                dto = dto with { Status = AssignmentStatusDto.Published };
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            });
        _mockHttp.When(HttpMethod.Get, "http://localhost/contacts/subscribed*")
            .Respond(HttpStatusCode.OK, "application/json", "[]");
        RegisterPublishDialog();

        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, dto);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW"));

        await SetTitleAsync(cut, "Edited title");
        cut.WaitForAssertion(() => module.Invocations.Should()
            .ContainSingle(i => i.Identifier == "registerBeforeUnload",
                "the unsaved edit armed the browser-unload guard"));

        cut.Find("#authoring-primary-action").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Unpublish",
                "the publish landed and the surface is now the read-only View");
            module.Invocations.Should().Contain(i => i.Identifier == "unregisterBeforeUnload",
                "P1: the dirty→read-only transition leaves nothing to lose, so the listener must be " +
                "detached rather than left to prompt on a clean page");
        }, TimeSpan.FromSeconds(15));
    }

    // ── F17: a swapped Id/Mode reloads the reused instance ────────────────

    /// <summary>F17: the host page renders <c>&lt;AssignmentAuthoring Mode="Edit" Id="Id" /&gt;</c>, and
    /// <c>/assignments/{a}/edit</c> → <c>/assignments/{b}/edit</c> is the same component type at the
    /// same render-tree position, so Blazor only updates <c>Id</c> — leaving A's loaded state on
    /// screen under B's URL (a stale-write path). The reused instance must reload the assignment the
    /// route now names.
    /// <para>Discriminating: against `66c2676a` the component had no <c>OnParametersSet</c>, so B's
    /// title never appears and A's stays.</para></summary>
    [TestMethod]
    public void IdParameterChanged_ReloadsTheAssignmentTheRouteNowNames()
    {
        var dtoA = MakeDto(AssignmentStatusDto.Draft);
        var dtoB = dtoA with { Id = Guid.NewGuid(), Title = "Science HW" };

        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, dtoA);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW"));

        SetupAssignment(dtoB);
        cut.Render(p => p
            .Add(x => x.Mode, AssignmentAuthoringMode.Edit)
            .Add(x => x.Id, dtoB.Id));

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Science HW",
                "F17: the reused instance reloads the assignment the route now names");
            cut.Markup.Should().NotContain("Math HW",
                "F17: and A's state is gone rather than merely left behind the new URL");
        }, TimeSpan.FromSeconds(5));
    }

    /// <summary>F17 (the loop guard): a parameter set that repeats the SAME <c>Id</c>/<c>Mode</c> must
    /// not re-enter the load — <c>OnParametersSetAsync</c> runs on every parameter set, so reloading
    /// unconditionally would be an endless re-read (and would make every render a fresh load).
    /// <para>Discriminating only in combination with the reload above: against `66c2676a` this passes
    /// vacuously (nothing ever reloads), which is why the reload test is the discriminating one.</para></summary>
    [TestMethod]
    public void SameParametersReset_DoesNotReloadTheAssignment()
    {
        var dto = MakeDto(AssignmentStatusDto.Draft);
        var detailReads = 0;

        // Registered BEFORE SetupAssignment: MockHttp matches in registration order, and
        // SetupAssignment registers its own detail read (this one therefore wins) — the counter would
        // never move with the registrations the other way round.
        _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{dto.Id}")
            .Respond(_ =>
            {
                detailReads++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(dto, _apiJsonOptions), Encoding.UTF8, "application/json")
                };
            });
        SetupAssignment(dto);

        var cut = Render<Authoring>(parameters => parameters
            .Add(p => p.Mode, AssignmentAuthoringMode.Edit)
            .Add(p => p.Id, dto.Id));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW"));
        var afterFirstLoad = detailReads;
        afterFirstLoad.Should().BeGreaterThan(0, "vacuity guard: the first load DID read the assignment");

        cut.Render(p => p
            .Add(x => x.Mode, AssignmentAuthoringMode.Edit)
            .Add(x => x.Id, dto.Id));

        detailReads.Should().Be(afterFirstLoad,
            "F17: re-setting the same Id/Mode is not a change — reloading on every parameter set is the " +
            "infinite loop the guard exists to prevent");
        cut.Markup.Should().Contain("Math HW");
    }

    // ── F12: the empty-audience preview wording ───────────────────────────────

    /// <summary>F12: with no audience target authored, the preview area does not render
    /// "Students matched: 0" — an empty audience resolves to zero, so the bare count reads as a
    /// settled audience of nobody instead of as work not yet done. It points at the missing step
    /// instead. The mocked read really did answer zero (the vacuity guard), so the hint is the
    /// rendering choice and not an unresolved state.
    /// <para>Discriminating: against `66c2676a` the area rendered the zero count.</para></summary>
    [TestMethod]
    public async Task NoTargets_PreviewRendersAnActionableHint_NotAZeroCount()
    {
        var previewRequests = 0;
        _mockHttp.When(HttpMethod.Get, "http://localhost/assignments/recipient-preview*")
            .Respond(_ =>
            {
                previewRequests++;
                return PreviewResponse(
                    "{\"studentsMatched\":0,\"primaryContacts\":0,\"otherContacts\":0,\"previewDegraded\":false}");
            });

        var clock = new FakeTimeProvider();
        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(AssignmentStatusDto.Draft),
            timeProvider: clock);

        // The read settles (and answered zero-matched) before the wording is judged.
        await AdvanceUntilAsync(clock, () => previewRequests >= 1);

        cut.WaitForAssertion(() =>
        {
            cut.Find("#authoring-audience-preview").TextContent.Trim()
                .Should().Be(Authoring.PreviewNoTargetsHint,
                    "F12: nothing is authored to preview, so the area asks for the missing step");
            cut.Markup.Should().NotContain("Students matched",
                "F12: the server's zero for an empty audience is never rendered as a count");
        });
    }

    /// <summary>F12 (non-regression): once a target IS authored the server-resolved counts render
    /// exactly as before. Not itself discriminating — it is the anchor that keeps the new hint from
    /// swallowing the normal case.</summary>
    [TestMethod]
    public async Task WithTargets_PreviewStillRendersTheServerResolvedCounts()
    {
        var previewRequests = 0;
        _mockHttp.When(HttpMethod.Get, "http://localhost/assignments/recipient-preview*")
            .Respond(_ =>
            {
                previewRequests++;
                return PreviewResponse(
                    "{\"studentsMatched\":7,\"primaryContacts\":5,\"otherContacts\":2,\"previewDegraded\":false}");
            });

        var clock = new FakeTimeProvider();
        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(AssignmentStatusDto.Draft),
            MakeChildren(targets: [new AssignmentTargetDto(TargetKindDto.Stream, Guid.NewGuid(), 0)]),
            timeProvider: clock);

        await AdvanceUntilAsync(clock, () => previewRequests >= 1);

        cut.WaitForAssertion(() => cut.Find("#authoring-audience-preview").TextContent.Trim()
            .Should().Be("Students matched: 7 \u00b7 Contacts reachable: 7 (5 primary / 2 other)"),
            TimeSpan.FromSeconds(2));
    }

    // ── F13: the initial load does not wait for the preview debounce ────────

    /// <summary>F13 (D6): the initial load must not await the advisory preview's 500 ms trailing
    /// debounce — an already-loaded form sat behind a progress ring for the length of a debounce that
    /// only the preview cares about. The form renders with the fake clock NEVER advanced (which is
    /// only reachable if the load does not await the debounce), the read is still pending, and it
    /// settles once the clock is advanced.
    /// <para>Discriminating: against `66c2676a` the load awaited the debounce, so with a clock that
    /// never advances the ring stays up forever and the first assertion times out.</para></summary>
    [TestMethod]
    public async Task InitialLoad_RendersWithoutWaitingForThePreviewDebounce()
    {
        var previewRequests = 0;
        _mockHttp.When(HttpMethod.Get, "http://localhost/assignments/recipient-preview*")
            .Respond(_ =>
            {
                previewRequests++;
                return PreviewResponse(
                    "{\"studentsMatched\":7,\"primaryContacts\":5,\"otherContacts\":2,\"previewDegraded\":false}");
            });

        var clock = new FakeTimeProvider();
        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(AssignmentStatusDto.Draft),
            MakeChildren(targets: [new AssignmentTargetDto(TargetKindDto.Stream, Guid.NewGuid(), 0)]),
            timeProvider: clock);

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Math HW", "F13: the loaded form is on screen");
            cut.FindComponents<FluentProgressRing>().Should().BeEmpty(
                "F13: and the loading ring is gone without the clock having moved");
        }, TimeSpan.FromSeconds(5));

        previewRequests.Should().Be(0, "the preview read is scheduled behind the debounce, not awaited in front of the form");

        await AdvanceUntilAsync(clock, () => previewRequests >= 1);
        cut.WaitForAssertion(() => cut.Find("#authoring-audience-preview").TextContent.Trim()
            .Should().Contain("Students matched: 7", "the scheduled read still settles once the debounce elapses"));
    }

    // ── F15: the constraint reason sits with the pickers it explains ───────

    /// <summary>F15: the paragraph that explains WHY the four constraint pickers are unavailable must be
    /// reachable from the group it governs — positioned with the picker rows rather than after the last
    /// one — and every control it disables must name a paragraph that actually renders in that state
    /// (NFR-1; the R2-10 derived-idref fix, re-checked through the new placement).
    /// <para>Discriminating: against `66c2676a` the paragraph rendered AFTER the group row, so the
    /// placement assertions fail.</para></summary>
    [TestMethod]
    public async Task ConstraintReason_SitsWithThePickerRows_AndEveryDisabledControlNamesARenderedReason()
    {
        var everyoneState = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(AssignmentStatusDto.Draft));
        everyoneState.WaitForAssertion(() => everyoneState.Markup.Should().Contain("Math HW"));

        await SelectEveryoneAsync(everyoneState);

        everyoneState.WaitForAssertion(() =>
        {
            var markup = everyoneState.Markup;
            var toggle = markup.IndexOf("id=\"authoring-audience-everyone\"", StringComparison.Ordinal);
            var reason = markup.IndexOf("id=\"authoring-audience-constraint-reason\"", StringComparison.Ordinal);
            var firstPicker = markup.IndexOf("id=\"authoring-audience-grades\"", StringComparison.Ordinal);
            var lastPicker = markup.IndexOf("id=\"authoring-audience-groups\"", StringComparison.Ordinal);

            toggle.Should().BeGreaterThanOrEqualTo(0).And.BeLessThan(reason,
                "F15: the reason follows the toggle that explains the constraint gate");
            reason.Should().BeLessThan(firstPicker,
                "F15: and stands with the picker rows it governs, above the first of them");
            reason.Should().BeLessThan(lastPicker, "F15: never after the last picker in the group");

            AssertEveryDisabledConstraintControlNamesARenderedReason(everyoneState);
        }, TimeSpan.FromSeconds(5));

        // The same contract in the UX-21 case (persisted targets did not load): the placement moved, so
        // the R2-10 derived-idref guarantee is re-checked against the new position.
        var ux21 = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(AssignmentStatusDto.Draft),
            childrenReadFails: true);

        ux21.WaitForAssertion(() =>
        {
            ux21.FindAll("#authoring-audience-constraint-reason").Should().ContainSingle();
            AssertEveryDisabledConstraintControlNamesARenderedReason(ux21);
        }, TimeSpan.FromSeconds(5));
    }

    /// <summary>NFR-1: every control the Audience compartment disables names a reason paragraph that
    /// really renders in the state under test — a dangling idref is worse than none, because it
    /// silently tells assistive tech nothing.</summary>
    private static void AssertEveryDisabledConstraintControlNamesARenderedReason(IRenderedComponent<Authoring> cut)
    {
        var describedBy = new[]
        {
            "authoring-audience-everyone",
            "authoring-audience-grades",
            "authoring-audience-streams",
            "authoring-audience-groups"
        };

        foreach (var id in describedBy)
        {
            var control = cut.Find($"#{id}");
            var idref = control.GetAttribute("aria-describedby");
            idref.Should().NotBeNullOrEmpty($"NFR-1: the disabled {id} explains itself");
            cut.FindAll($"#{idref}").Should().ContainSingle(
                $"NFR-1: {id}'s idref must resolve to a paragraph that actually renders in this state");
        }

        // The students picker is the one constraint control whose declaration FluentUI SWALLOWS:
        // FluentAutocomplete puts the Id on its inner <fluent-text-field> and owns that element's aria
        // surface (combobox role, aria-label, aria-expanded, aria-controls), so the aria-describedby
        // the page declares is dropped rather than forwarded. Pinned instead of skipped, so the
        // limitation is visible to the next reader and a FluentUI version that starts forwarding the
        // attribute fails HERE — extend the loop above when that happens. Recorded in the spec's
        // "Deferred / known gaps".
        cut.Find("#authoring-audience-students").GetAttribute("aria-describedby").Should().BeNull(
            "documented limitation (FluentUI 4.14.2): FluentAutocomplete drops the declared " +
            "aria-describedby, so this picker has no idref to resolve — unlike the four in the loop above");
    }

    // ── F7: a disabled action explains itself (the authoring call site) ────

    /// <summary>F7 at its call site: the kebab's "Generate questions" is disabled when the type/grading
    /// gate is closed, and it now carries the reason the compartment states (AI-4). The shared
    /// component's own rendering contract is asserted in
    /// <c>SchoolCollab.Admin.Tests.Unit.RowActionsMenuTests</c>.
    /// <para>Discriminating: against `66c2676a` the item carried no description at all.</para></summary>
    [TestMethod]
    public void AuthoringKebab_DisabledGenerateAction_CarriesTheReason()
    {
        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit,
            MakeDto(AssignmentStatusDto.Draft, type: AssignmentTypeDto.Manual, grading: GradingFormatDto.TeacherGraded));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW"));

        cut.Find("fluent-button[title=\"More assignment actions\"]").Click();

        cut.WaitForAssertion(() =>
        {
            var item = cut.FindAll("fluent-menu-item")
                .Single(i => i.TextContent.Trim() == "Generate questions");
            item.GetAttribute("title").Should().Be(QuestionGenerationGate.DisabledHint,
                "F7: a greyed-out action names the reason instead of being a dead end");
        });
    }

    /// <summary>Drives the authoring page to the state <paramref name="settled"/> describes while
    /// the injected fake clock keeps advancing past the trailing preview debounce. The page arms
    /// that debounce from a POSTED continuation (Blazor dispatches the event through the renderer,
    /// and the load's reads hop the same way), so the advance is retried rather than guessed at —
    /// bounded, and only the fake clock ever fires a timer, never real time.</summary>
    private static async Task AdvanceUntilAsync(FakeTimeProvider clock, Func<bool> settled)
    {
        for (var attempt = 0; attempt < 400; attempt++)
        {
            clock.Advance(FakeTimeProvider.PreviewDebounce);
            await Task.Delay(5);
            if (settled())
            {
                return;
            }
        }

        settled().Should().BeTrue("the authoring page settled within the bounded fake-clock advance");
    }
}
