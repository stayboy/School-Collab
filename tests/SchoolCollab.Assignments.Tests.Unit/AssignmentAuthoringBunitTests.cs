using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.FluentUI.AspNetCore.Components;
using Moq;
using RichardSzalay.MockHttp;
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
            .Respond(HttpStatusCode.OK, "application/json", "[]");
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
        IReadOnlyList<ResourceDto>? resources = null) =>
        new(Guid.NewGuid(), questions ?? [], attachments ?? [], resources ?? []);

    private IRenderedComponent<Authoring> RenderAuthoring(
        AssignmentAuthoringMode mode,
        AssignmentSummaryDto? dto = null,
        AssignmentAuthoringChildrenDto? children = null,
        Guid[]? linkedGroupIds = null,
        bool childrenReadFails = false)
    {
        if (dto is not null)
            SetupAssignment(dto, children, linkedGroupIds, childrenReadFails);
        return Render<Authoring>(parameters =>
        {
            parameters.Add(p => p.Mode, mode);
            if (dto is not null) parameters.Add(p => p.Id, dto.Id);
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

    [TestMethod]
    public void GroupTarget_DisablesTheGradePickerWithReason()
    {
        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(AssignmentStatusDto.Draft));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW"));

        var audienceSelect = cut
            .FindComponents<FluentSelect<Authoring.PickerOption>>()
            .Single(s => s.Instance.Id == "authoring-audience-target");

        cut.InvokeAsync(() => audienceSelect.Instance.SelectedOptionChanged.InvokeAsync(
            new Authoring.PickerOption(((int)TargetAudienceTypeDto.SelectedGroups).ToString(), "By Group")));

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Not applicable when the audience is By Group.");
            cut.FindComponents<FluentSelect<Authoring.PickerOption>>()
                .Single(s => s.Instance.Id == "authoring-basics-grade").Instance.Disabled
                .Should().BeTrue("the primary grade picker is inapplicable, not hidden");
        });
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
            children: MakeChildren(questions: [LoadedQuestion()], attachments: [LoadedAttachment()]));

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
        await SelectAsync(cut, "authoring-audience-target",
            ((int)TargetAudienceTypeDto.SelectedGroups).ToString(), "By Group");
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
            "the By-Group audience is what the author selected (enum names serialize as-is)");
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

        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, dto, linkedGroupIds: [GroupAId]);

        cut.WaitForAssertion(() =>
        {
            Picker(cut, "authoring-audience-groups").SelectedOptions
                .Should().ContainSingle(o => o.Value == GroupAId.ToString(),
                    "the persisted link is loaded into the picker instead of an empty selection");
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

        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, dto, linkedGroupIds: [GroupAId]);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW"));

        // The picker reports the SAME set it loaded (a click that changes nothing): the
        // subject union reloads but the link set is unchanged.
        await SelectGroupsAsync(cut, groupA);
        await SelectAsync(cut, "authoring-basics-subject", dto.TopicId.ToString(), "Mathematics");

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

        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, dto, linkedGroupIds: [GroupAId]);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW"));

        await SelectGroupsAsync(cut, groupA, groupB);
        await SelectAsync(cut, "authoring-basics-subject", dto.TopicId.ToString(), "Mathematics");

        string? linkBody = null;
        CaptureRequest(HttpMethod.Put, $"http://localhost/assignments/{dto.Id}/groups", body => linkBody = body);
        CaptureRequest(HttpMethod.Put, $"http://localhost/assignments/{dto.Id}", _ => { });

        SaveDraft(cut);

        cut.WaitForAssertion(() => linkBody.Should().NotBeNull(
            "an edited picker is what makes the links non-stale (the reviewer's P2-1)"), TimeSpan.FromSeconds(5));
        linkBody.Should().Contain(GroupAId.ToString()).And.Contain(GroupBId.ToString());
    }

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
        await SelectAsync(cut, "authoring-audience-target",
            ((int)TargetAudienceTypeDto.SelectedGroups).ToString(), "By Group");
        await SelectGroupsAsync(cut, groupA);

        string? createBody = null;
        CaptureRequest(HttpMethod.Post, "http://localhost/assignments", body => createBody = body);

        cut.Find("#authoring-primary-action").Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Select a subject."),
            TimeSpan.FromSeconds(5));
        createBody.Should().BeNull("the FR-58 union gate blocks the save before any request is sent");
    }
}
