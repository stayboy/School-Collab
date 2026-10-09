using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AngleSharp.Dom;
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
using SchoolCollab.Core.AssignmentPolicies;
using SchoolCollab.Core.Features;
using SchoolCollab.Students.Application.Services;
using Authoring = SchoolCollab.Assignments.Application.Components.Pages.Assignments.AssignmentAuthoring;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// R1 acceptance (documents/specs/assignment-authoring-compartments.md §16; round
/// <c>round-assignment-authoring-r1</c> criteria 3–7) — the shared
/// <see cref="AssignmentAuthoring"/> component is the ONE authoring surface (D5/UX-9):
/// five compartments in Create / Edit / View, Edit parity with Create while Draft|Scheduled,
/// the §11 status-driven action bar, disabled-with-reason gating (UX-17/UX-19) and the
/// read-only policy values with the inherited badge (UX-13).
/// </summary>
[TestClass]
public class AssignmentAuthoringBunitTests : BunitContext
{
    private readonly MockHttpMessageHandler _mockHttp;
    private readonly JsonSerializerOptions _apiJsonOptions;
    private readonly StubFlagService _flags = new();

    /// <summary>The resolved effective-policy body the authoring page's
    /// <c>/assignments/effective-policy</c> read returns. Mutable so a test can switch the resolved
    /// policy (signature requirement, guardian review, archive window) before rendering.</summary>
    private string _effectivePolicyBody = EffectivePolicyBody();

    /// <summary>Every <c>/effective-policy</c> request's query string, in call order — the
    /// observable for "which grade (if any) did the page resolve its effective policy for"
    /// (round <c>drop-primary-grade</c>: the page DERIVES that grade from its grade targets).</summary>
    private readonly List<string> _effectivePolicyQueries = [];

    /// <summary>D2/OD4: the effective-policy body a GRADE-scoped read returns. The single matcher
    /// below dispatches on the query string because MockHttp's path matchers ignore it, so a
    /// grade-scoped stub cannot simply be registered first.</summary>
    private readonly Dictionary<Guid, string> _gradeEffectivePolicies = [];

    /// <summary>D2/OD4: the effective-policy body, produced by the REAL resolver so the stubbed body
    /// can never disagree with the server's own derivation (the D4 implication included).</summary>
    private static string EffectivePolicyBody(
        SignatureRequirementMode signature = SignatureRequirementMode.Disabled,
        bool? mandatoryReview = null,
        int? archiveGraceDays = null) =>
        JsonSerializer.Serialize(new EffectiveAssignmentPolicyResolver().Resolve(
            tenantDefault: new AssignmentPolicyFields
            {
                SignatureRequirement = signature,
                MandatoryReview = mandatoryReview,
                ArchiveGraceDays = archiveGraceDays,
            },
            gradeOverride: null));

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
        _mockHttp.When(HttpMethod.Get, "http://localhost/assignments/effective-policy")
            .Respond(request =>
            {
                var query = request.RequestUri!.Query;
                _effectivePolicyQueries.Add(query);
                var body = _effectivePolicyBody;
                foreach (var (gradeId, gradeBody) in _gradeEffectivePolicies)
                {
                    if (query.Contains(gradeId.ToString(), StringComparison.OrdinalIgnoreCase))
                    {
                        body = gradeBody;
                        break;
                    }
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
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

    /// <param name="enterEditFields">assignment-create-edit-redesign D1: Edit/View routes now open
    /// on the SUMMARY surface, so a test that inspects the compartment form must flip the pencil.
    /// Default true (the historical behaviour of this helper: hand back the FORM); pass
    /// <see langword="false"/> to assert the summary itself.</param>
    private IRenderedComponent<Authoring> RenderAuthoring(
        AssignmentAuthoringMode mode,
        AssignmentSummaryDto? dto = null,
        AssignmentAuthoringChildrenDto? children = null,
        Guid[]? linkedGroupIds = null,
        bool childrenReadFails = false,
        TimeProvider? timeProvider = null,
        bool enterEditFields = true)
    {
        if (dto is not null)
            SetupAssignment(dto, children, linkedGroupIds, childrenReadFails);
        var cut = Render<Authoring>(parameters =>
        {
            parameters.Add(p => p.Mode, mode);
            if (dto is not null) parameters.Add(p => p.Id, dto.Id);
            if (timeProvider is not null) parameters.Add(p => p.TimeProvider, timeProvider);
        });

        if (enterEditFields)
        {
            TryEnterEditFields(cut);
        }

        return cut;
    }

    /// <summary>D1: flips the Edit route from its default summary into the edit-fields view via the
    /// summary card's pencil. A NO-OP when no pencil renders — Create has no summary at all and a
    /// degraded read-only View (Published/Closed/Archived) has no pencil (D3).</summary>
    private static void TryEnterEditFields(IRenderedComponent<Authoring> cut)
    {
        // Wait until the surface settled: either the summary card or the form itself is on screen.
        cut.WaitForAssertion(() =>
        {
            (cut.FindAll("#authoring-summary").Count > 0 || cut.FindAll("#authoring-basics").Count > 0)
                .Should().BeTrue("the surface must have settled before the pencil decision");
        }, TimeSpan.FromSeconds(5));

        var pencil = cut.FindAll("#authoring-summary-edit");
        if (pencil.Count == 0)
        {
            return;
        }

        pencil.Single().Click();
        cut.WaitForAssertion(() =>
            cut.FindAll("#authoring-basics").Count.Should().BeGreaterThan(0,
                "the pencil flips the page into the edit-fields view"), TimeSpan.FromSeconds(5));
    }

    /// <summary>D12 (owner, 2026-10-08): the compartment JUMP-NAV is removed, so the compartment
    /// contract is now read straight off the DOM — the <c>section.authoring-compartment</c> order.
    /// Rules is NOT a section (it is a SectionCard inside the Targets column), so the expected lists
    /// below are the SECTION order; the Rules card's own anchor is pinned separately.</summary>
    private static IReadOnlyList<string> CompartmentSectionIds(IRenderedComponent<Authoring> cut) =>
        cut.FindAll("section.authoring-compartment").Select(section => section.Id).ToList();

    private static readonly string[] ExpectedFormSections =
    [
        "authoring-basics",
        "authoring-targets",
        "authoring-content",
        "authoring-questions",
        "authoring-instructions"
    ];

    /// <summary>D10 (verification round 2026-10-08, extended by the owner): Create renders the
    /// DETAILS sections only — neither Content &amp; Resources nor Questions &amp; AI (both live on
    /// the draft-edit surface).</summary>
    private static readonly string[] ExpectedCreateSections =
    [
        "authoring-basics",
        "authoring-targets",
        "authoring-instructions"
    ];

    private static bool ScoringFieldDisabled(IRenderedComponent<Authoring> cut, string id) =>
        cut.Find($"#{id}").HasAttribute("disabled");

    private static FluentSelect<Authoring.PickerOption> Picker(IRenderedComponent<Authoring> cut, string id) =>
        cut.FindComponents<FluentSelect<Authoring.PickerOption>>().Single(s => s.Instance.Id == id).Instance;

    /// <summary>The five Rules readout ids, in card order (round <c>authoring-compact-fields</c>: the
    /// readouts became card items, and their ids are the seam the surface has always exposed).</summary>
    private static readonly string[] PolicyReadoutIds =
    [
        "authoring-policy-approval",
        "authoring-policy-notification",
        "authoring-policy-archive",
        "authoring-policy-signature",
        "authoring-policy-review"
    ];

    /// <summary>The <c>FormRow</c> that owns the control with this id. A row's root element carries the
    /// <c>form-row</c> class, so the walk goes up from the control's own cell — which is what makes
    /// "the two controls share ONE row" assertable at all.</summary>
    private static IElement OwningFormRow(IElement scope, string id)
    {
        var element = scope.QuerySelector($"#{id}");
        while (element is not null && !element.ClassList.Contains("form-row"))
        {
            element = element.ParentElement;
        }

        return element ?? throw new InvalidOperationException($"#{id} renders outside any FormRow");
    }

    // ── The "Targets & audience" builder: add/remove helpers ────────────────

    /// <summary>The add dialog's queued outcomes plus the models the page handed it, so a test can
    /// assert the options the page offered as well as the entries it appended. One queued outcome is
    /// one submission — and a submission carries EVERY option the multi-select picked.
    /// </summary>
    private sealed class TargetsDialogStub
    {
        private readonly Queue<TargetsAndAudienceDialogResult> _queued = new();

        public List<TargetsAndAudienceDialogModel> Models { get; } = [];

        /// <summary>Queues ONE dialog submission carrying these entries (the multi-select result).</summary>
        public TargetsDialogStub Enqueue(params TargetsAndAudienceEntry[] entries)
        {
            _queued.Enqueue(new TargetsAndAudienceDialogResult(entries));
            return this;
        }

        internal bool TryDequeue(out TargetsAndAudienceDialogResult result)
        {
            if (_queued.Count == 0)
            {
                result = null!;
                return false;
            }

            result = _queued.Dequeue();
            return true;
        }
    }

    /// <summary>
    /// Mocks the "Add target &amp; audience" shell dialog: every open records the model the page
    /// built (its option sources and student search) and resolves to the next queued submission — or to a
    /// cancel when none remains. bUnit renders the page without a FluentUI dialog provider, so the
    /// dialog component cannot be driven in-tree; the seam under test is what the page does with the
    /// entries the dialog returns.
    /// </summary>
    private TargetsDialogStub RegisterTargetsDialog()
    {
        var stub = new TargetsDialogStub();
        var dialogMock = new Mock<IDialogService>();
        dialogMock
            .Setup(d => d.ShowDialogAsync<TargetsAndAudienceDialog, DialogShellData<TargetsAndAudienceDialogModel>>(
                It.IsAny<DialogShellData<TargetsAndAudienceDialogModel>>(), It.IsAny<DialogParameters>()))
            .Callback<DialogShellData<TargetsAndAudienceDialogModel>, DialogParameters>(
                (data, _) => stub.Models.Add(data.Model))
            .Returns(() =>
            {
                var dialogRef = new Mock<IDialogReference>();
                dialogRef.SetupGet(r => r.Result).Returns(Task.FromResult(stub.TryDequeue(out var result)
                    ? DialogResult.Ok<object?>(new DialogShellResult<TargetsAndAudienceDialogResult>(result))
                    : DialogResult.Cancel()));
                return Task.FromResult(dialogRef.Object);
            });
        Services.AddSingleton(dialogMock.Object);
        return stub;
    }

    /// <summary>Clicks the builder's Add action — the card's native header Add (grill Q4), or its
    /// disabled-with-reason replacement (P1-3), both named "Add target".</summary>
    private static void OpenAddTargetDialog(IRenderedComponent<Authoring> cut) =>
        cut.Find("fluent-button[title='Add target']").Click();

    /// <summary>The builder's rendered entries — (category label, value label) in authored order.</summary>
    private static IReadOnlyList<(string Category, string Value)> BuilderEntries(IRenderedComponent<Authoring> cut) =>
        cut.FindAll("#authoring-targets .targets-audience-item")
            .Select(item => (
                item.QuerySelector(".targets-audience-item__label")!.TextContent.Trim(),
                item.QuerySelector(".targets-audience-item__value")!.TextContent.Trim()))
            .ToList();

    /// <summary>Clicks the nth builder entry's remove action (P1-1: it lives INSIDE the ItemTemplate).</summary>
    private static void RemoveBuilderEntry(IRenderedComponent<Authoring> cut, int index) =>
        cut.FindAll("#authoring-targets fluent-button[title='Remove']")[index].Click();

    /// <summary>Drives a picker's real <c>SelectedOptionChanged</c> callback — never a raw DOM
    /// event (the AssignmentCreateBunitTests / AssignmentPolicyFieldEditDialogTests pattern).</summary>
    /// <summary>Drives a FluentUI picker to a value and does not return until the pick is
    /// OBSERVABLY applied — the shared seam every picker-driving test inherits (skill
    /// <c>fix-flaky-bunit-fluentui-after-cascade</c>). Two races this closes: (1) the picker's
    /// <c>Items</c> is rebuilt as a FRESH array when a prerequisite target/interaction settles, and
    /// a pick made before that rebuild lands is dropped — FluentUI matches the selection by
    /// INSTANCE — so we wait for the value to appear in <c>Items</c> and then pick that very
    /// instance, never a synthetic one; (2) the pick's own <c>SelectedOptionChanged</c> cascade is
    /// one render pass late, so a Submit in the next statement could read the pre-pick state — we
    /// assert the landed selection before returning.</summary>
    private static async Task SelectAsync(IRenderedComponent<Authoring> cut, string pickerId, string value)
    {
        cut.WaitForAssertion(() =>
        {
            var items = Picker(cut, pickerId).Items;
            items.Should().NotBeNull("the picker's options are loaded before any pick");
            items!.Should().Contain(o => o.Value == value,
                "the prerequisite reload settles before the pick, so the value is present in the CURRENT Items array");
        }, TimeSpan.FromSeconds(5));

        await cut.InvokeAsync(() =>
        {
            var picker = Picker(cut, pickerId);
            var option = picker.Items!.First(o => o.Value == value);
            return picker.SelectedOptionChanged.InvokeAsync(option);
        });

        cut.WaitForAssertion(() => Picker(cut, pickerId).SelectedOption?.Value.Should().Be(value,
            "the pick must be applied before the next interaction: the FluentUI cascade is one pass late"),
            TimeSpan.FromSeconds(5));
    }

    /// <summary>Drives the subject picker with the option INSTANCE its own Items hold (a foreign
    /// instance is not accepted as the selection by the FluentUI control).</summary>
    private static Task SelectSubjectAsync(IRenderedComponent<Authoring> cut, Guid topicId) =>
        cut.InvokeAsync(() =>
        {
            var picker = Picker(cut, "authoring-basics-subject");
            var option = picker.Items!.First(o => o.Value == topicId.ToString());
            return picker.SelectedOptionChanged.InvokeAsync(option);
        });

    private static ActivityGroupDto Group(Guid id, string name, bool isActive = true) =>
        new(id, name, null, null, null, isActive, "span", null, null, false, [], 0,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    /// <summary>Fills the Basics title through the bound text field's own callback — the save
    /// guards require a non-empty title (the AssignmentCreateBunitTests precedent).</summary>
    private static Task SetTitleAsync(IRenderedComponent<Authoring> cut, string title) =>
        cut.InvokeAsync(() => cut.FindComponents<FluentTextField>()
            .Single(f => f.Instance.Id == "authoring-basics-title").Instance.ValueChanged.InvokeAsync(title));

    // ── Criterion 3: six compartments in all editable modes (D1) + assignment-create-edit-redesign D5/D7 ──

    [TestMethod]
    public void Create_RendersItsCompartmentSections_NoContentNoQuestions()
    {
        var cut = RenderAuthoring(AssignmentAuthoringMode.Create);

        cut.WaitForAssertion(() =>
        {
            CompartmentSectionIds(cut).Should().Equal(ExpectedCreateSections,
                "UX-1/UX-3 (D1) + D5 + D10: Create renders the three DETAILS sections — Content & Resources and Questions & AI live on the draft-edit surface only");
            cut.Find("#authoring-rules").Should().NotBeNull(
                "the Rules card (not a section) still renders in the Targets column");
        });
    }

    /// <summary>D10 (extended by the owner 2026-10-08): Create renders NEITHER Questions &amp; AI
    /// NOR Content &amp; Resources — no sections, no editors, no kebab action (its anchor would be
    /// dead), no jump-nav entries. "Move questions and AI to draft edit when assignment is
    /// created"; Content &amp; Resources follows the same rule.</summary>
    [TestMethod]
    public void Create_DoesNotRenderContentOrQuestions()
    {
        var cut = RenderAuthoring(AssignmentAuthoringMode.Create);

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("#authoring-questions").Should().BeEmpty(
                "D10: the Questions & AI section starts at draft-edit time");
            cut.FindAll("#authoring-content").Should().BeEmpty(
                "D10: Content & Resources starts at draft-edit time too (owner confirmation)");
            cut.FindComponents<QuestionGenerationSection>().Should().BeEmpty();
            cut.FindComponents<QuestionEditorSection>().Should().BeEmpty();
            cut.FindComponents<ResourcesSection>().Should().BeEmpty();
            cut.FindAll("fluent-button[title=\"More assignment actions\"]").Should().BeEmpty(
                "D10: Create's only kebab entry was Generate questions, whose anchor is gone");
            cut.FindAll("nav.authoring-jumpnav").Should().BeEmpty(
                "D12: the compartment jump-nav is removed from the surface entirely");
        });
    }

    [TestMethod]
    public void Edit_Draft_RendersTheFormSections()
    {
        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(AssignmentStatusDto.Draft));

        cut.WaitForAssertion(() =>
        {
            CompartmentSectionIds(cut).Should().Equal(ExpectedFormSections,
                "the pencil flipped the default summary into the edit-fields view, which carries the form sections in canonical order");
            cut.Find("#authoring-rules").Should().NotBeNull("the Rules card renders in the Targets column");
        });
    }

    /// <summary>D2/D3 (assignment-create-edit-redesign): a read-only View opens on the SUMMARY — no
    /// compartment form, no jump-nav, no pencil — with Questions &amp; AI and the Targets accordion
    /// still on screen.</summary>
    [TestMethod]
    public void View_Published_RendersTheSummary_NotTheCompartmentForm()
    {
        var cut = RenderAuthoring(AssignmentAuthoringMode.View, MakeDto(AssignmentStatusDto.Published),
            enterEditFields: false);

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("#authoring-summary").Count.Should().Be(1,
                "D3: a later status opens on the summary surface");
            cut.FindAll("#authoring-summary-edit").Should().BeEmpty("D3: no pencil without structural edits");
            cut.FindAll("#authoring-basics").Should().BeEmpty("the form is not rendered on the summary");
            cut.FindAll("nav.authoring-jumpnav").Should().BeEmpty(
                "D12: the compartment jump-nav is removed from every surface");
            cut.FindAll("#authoring-questions").Count.Should().Be(1,
                "Questions & AI always renders");
            cut.FindAll("#authoring-targets").Count.Should().Be(1,
                "the Targets accordion always renders");
        });
    }

    /// <summary>D12 (owner, 2026-10-08): the compartment jump-nav is gone from EVERY surface, while
    /// each mode keeps exactly the compartment sections it always had.</summary>
    [TestMethod]
    public void EverySurface_RendersNoJumpNav_AndTheFormKeepsItsSections()
    {
        foreach (var mode in new[] { AssignmentAuthoringMode.Create, AssignmentAuthoringMode.Edit })
        {
            var cut = mode == AssignmentAuthoringMode.Create
                ? RenderAuthoring(mode)
                : RenderAuthoring(mode, MakeDto(AssignmentStatusDto.Draft));

            var expected = mode == AssignmentAuthoringMode.Create
                ? ExpectedCreateSections
                : ExpectedFormSections;

            cut.WaitForAssertion(() =>
            {
                cut.FindAll("nav.authoring-jumpnav").Should().BeEmpty(
                    "D12: the jump-nav — the submenu list that used to sit under the page title — is removed");
                CompartmentSectionIds(cut).Should().Equal(expected,
                    "D1/D5/D10: the section set per mode is unchanged by D12");
            });
        }

        var viewCut = RenderAuthoring(AssignmentAuthoringMode.View, MakeDto(AssignmentStatusDto.Draft),
            enterEditFields: false);
        viewCut.WaitForAssertion(() =>
            viewCut.FindAll("nav.authoring-jumpnav").Should().BeEmpty("D12: no jump-nav on the summary either"));
    }

    [TestMethod]
    public void Create_InstructionsFieldIsRenderedInBasics()
    {
        var cut = RenderAuthoring(AssignmentAuthoringMode.Create);

        cut.WaitForAssertion(() =>
        {
            cut.FindComponents<FluentTextArea>()
                .Should().Contain(t => t.Instance.Id == "authoring-basics-instructions",
                    "INS-1 + D5: Instructions is the student-facing field, now in the bottom Instructions compartment");
        });
    }

    /// <summary>The right-hand column's cards each carry their OWN header as their only title (the
    /// column renders no <c>h3</c>). Round <c>authoring-compact-fields</c> added the Rules card to that
    /// column, so the column now stacks exactly two cards in a fixed order — the assertion is re-pointed
    /// from "exactly one title" to "exactly these two, in this order" rather than weakened: the
    /// single-title intent is preserved per CARD, and the stacking order is now pinned too.
    /// Round <c>drop-primary-grade</c>: the assignment carries no primary grade, so no grade control
    /// exists anywhere on the surface — the grade half of the audience is the grade TARGET rows inside
    /// the card (AC-9).</summary>
    [TestMethod]
    public void TargetsCompartment_CardHeaderIsTheOnlyTitle_AndNoPrimaryGradeFieldRenders()
    {
        foreach (var mode in new[] { AssignmentAuthoringMode.Create, AssignmentAuthoringMode.Edit })
        {
            var cut = mode == AssignmentAuthoringMode.Create
                ? RenderAuthoring(mode)
                : RenderAuthoring(mode, MakeDto(AssignmentStatusDto.Draft));

            cut.WaitForAssertion(() =>
            {
                cut.FindAll("#authoring-targets h3.authoring-compartment-title").Should().BeEmpty(
                    "the column renders no heading of its own — each card's header is its title");
                cut.FindAll("#authoring-targets .section-card__title")
                    .Select(title => title.TextContent.Trim())
                    .Should().Equal(["Targets & audience", "Rules"],
                        "the SectionCard headers hold both titles, Targets first");
                cut.FindAll("#authoring-basics-grade").Should().BeEmpty(
                    "round drop-primary-grade: the Primary grade control is gone from every mode (AC-9)");
                cut.Markup.Should().NotContain("Primary grade",
                    "no compartment offers a separately-authored primary grade any more");
            });
        }

        // assignment-create-edit-redesign D3: the View route renders the SUMMARY — the Targets
        // block is the accordion whose HEADING is its single title (the card title inside is
        // deliberately empty), and the same no-primary-grade rule holds.
        var viewCut = RenderAuthoring(AssignmentAuthoringMode.View, MakeDto(AssignmentStatusDto.Draft),
            enterEditFields: false);
        viewCut.WaitForAssertion(() =>
        {
            viewCut.FindAll("#authoring-targets h3.authoring-compartment-title").Should().BeEmpty(
                "the summary's Targets block carries no h3 either — the accordion heading is its title");
            viewCut.Find("#authoring-targets").TextContent.Should().Contain("Targets & audience",
                "the accordion heading names the block");
            viewCut.FindAll("#authoring-basics-grade").Should().BeEmpty();
            viewCut.Markup.Should().NotContain("Primary grade");
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

    /// <summary>D3 (assignment-create-edit-redesign): a degraded status renders the SUMMARY —
    /// no pencil, no compartment form, no editors — while Questions &amp; AI and the Targets
    /// accordion stay on screen.</summary>
    [TestMethod]
    [DataRow(AssignmentStatusDto.Published)]
    [DataRow(AssignmentStatusDto.Closed)]
    [DataRow(AssignmentStatusDto.Archived)]
    public void Edit_LaterStatus_DegradesToReadOnlySummary(AssignmentStatusDto status)
    {
        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(status));

        cut.WaitForAssertion(() =>
        {
            cut.FindComponents<QuestionEditorSection>().Should().BeEmpty(
                "UX-9/UX-11 + D3: Published/Closed/Archived render the summary — no editors");
            cut.FindComponents<ResourcesSection>().Should().BeEmpty();
            cut.FindAll("#authoring-summary").Count.Should().Be(1,
                "the same summary every later status opens on");
            cut.FindAll("#authoring-summary-edit").Should().BeEmpty("D3: no pencil without structural edits");
            cut.FindAll("#authoring-basics").Should().BeEmpty("D3: no input fields unless requested");
            cut.FindAll("#authoring-questions").Count.Should().Be(1);
            cut.FindAll("#authoring-targets").Count.Should().Be(1);
        });
    }

    /// <summary>D2/D3: View opens on the summary — facts present, questions present as a hint,
    /// no form, no editors.</summary>
    [TestMethod]
    public void View_LaterStatus_RendersTheSummaryReadOnly()
    {
        var cut = RenderAuthoring(AssignmentAuthoringMode.View, MakeDto(AssignmentStatusDto.Closed),
            enterEditFields: false);

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("#authoring-summary").Count.Should().Be(1);
            cut.FindAll("#authoring-fact-instructions").Count.Should().Be(1,
                "the facts grid carries the long-form texts");
            cut.FindComponents<QuestionEditorSection>().Should().BeEmpty();
            cut.FindComponents<FluentTextArea>().Should().BeEmpty(
                "D3: no input fields render on the summary at all");
            cut.FindAll("#authoring-summary-edit").Should().BeEmpty();
            cut.Find("#authoring-breadcrumb").TextContent.Should().Contain("Overview",
                "D8: a degraded surface names its crumb Overview, not Edit");
        });
    }

    // ── assignment-create-edit-redesign: summary-first draft edit (D1/D2/D9) ────

    /// <summary>D1/D2: Edit opens on the SUMMARY — facts card with the pencil as the only entry to
    /// the fields, no input controls anywhere on it, Questions &amp; AI rendered and the Targets
    /// accordion below it.</summary>
    [TestMethod]
    public void Edit_Draft_OpensOnTheSummary_NoInputFields_PencilPresent()
    {
        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit,
            MakeDto(AssignmentStatusDto.Draft, instructions: "Do the thing"),
            enterEditFields: false);

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("#authoring-summary").Count.Should().Be(1);
            cut.Find("#authoring-summary-edit").Should().NotBeNull(
                "the pencil (top right) is the only entry to the input fields");
            cut.Find("#authoring-summary").QuerySelectorAll(
                    "input, textarea, fluent-select, fluent-text-field, fluent-date-picker, fluent-number-field")
                .Should().BeEmpty("the owner's rule: no input fields on the summary unless requested");
            cut.Find("#authoring-fact-status").TextContent.Trim().Should().Be("Draft");
            cut.Find("#authoring-fact-instructions").TextContent.Trim().Should().Be("Do the thing");
            cut.FindComponents<QuestionEditorSection>().Should().NotBeEmpty(
                "Questions & AI always renders, ready for edit or question add");
            cut.FindAll("#authoring-targets fluent-accordion").Should().NotBeEmpty(
                "the Targets & audience accordion sits below Questions & AI");
            cut.FindAll("nav.authoring-jumpnav").Should().BeEmpty("D12: no jump-nav on the summary");
        }, TimeSpan.FromSeconds(5));
    }

    /// <summary>D9: the pencil / back-to-summary flip is PURE presentation state — with a dirty
    /// form it raises no confirmation (nothing is discarded) and the unsaved value survives both
    /// flips. The guard's own contract (navigation only) is pinned by recording that no
    /// confirmation was ever asked for.</summary>
    [TestMethod]
    public async Task Pencil_FlipsToTheFormAndBack_KeepingUnsavedEdits_WithoutAGuardPrompt()
    {
        var captured = new List<ConfirmDialogContent>();
        RegisterConfirmationDialog(confirm: false, captured);

        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(AssignmentStatusDto.Draft),
            enterEditFields: false);
        cut.WaitForAssertion(() => cut.FindAll("#authoring-summary").Count.Should().Be(1));

        cut.Find("#authoring-summary-edit").Click();
        cut.WaitForAssertion(() => cut.FindAll("#authoring-basics").Count.Should().BeGreaterThan(0));

        await SetTitleAsync(cut, "Dirty title");

        cut.Find("#authoring-summary-back").Click();
        cut.WaitForAssertion(() => cut.FindAll("#authoring-summary").Count.Should().Be(1));

        cut.Find("#authoring-summary-edit").Click();
        cut.WaitForAssertion(() =>
            cut.FindComponents<FluentTextField>()
                .Single(f => f.Instance.Id == "authoring-basics-title").Instance.Value
                .Should().Be("Dirty title", "D9: the model survives the flip in both directions"));

        captured.Should().BeEmpty("D9: the toggle is not navigation, so the UX-7 guard stays silent");
    }

    /// <summary>D2: the Targets accordion is collapsed by default, carries the count badge in its
    /// heading, and holds the same SectionCard entry list the form renders (expanding reveals it).</summary>
    [TestMethod]
    public void TargetsAccordion_CollapsedByDefault_HoldsTheEntryListAndCount()
    {
        var dto = MakeDto(AssignmentStatusDto.Draft);
        var children = MakeChildren(
            targets: [new AssignmentTargetDto(TargetKindDto.GradeLevel, Guid.NewGuid(), 0)]);

        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, dto, children: children, enterEditFields: false);

        cut.WaitForAssertion(() =>
        {
            var item = cut.Find("fluent-accordion-item");
            item.HasAttribute("expanded").Should().BeFalse("collapsed by default (D2)");
            cut.Find(".authoring-targets-count").TextContent.Trim().Should().Be("1",
                "the heading carries the count badge");
            cut.FindAll("#authoring-targets .targets-audience-item").Should().ContainSingle(
                "the entry list is the card's, ready behind the collapsed header");
        }, TimeSpan.FromSeconds(5));

        // bUnit never runs the web component's JS, so the element's own 'onaccordionchange' event
        // cannot fire here — invoke the SAME public callback the handler would invoke (the bound
        // ExpandedChanged), which is what actually flips the page's _targetsExpanded state.
        var itemComponent = cut.FindComponent<FluentAccordionItem>();
        cut.InvokeAsync(() => itemComponent.Instance.ExpandedChanged.InvokeAsync(true));
        cut.WaitForAssertion(() =>
            cut.Find("fluent-accordion-item").HasAttribute("expanded").Should().BeTrue(
                "the bound state expands the panel"));
    }

    /// <summary>§9.6: the summary's Questions block keeps the disabled-with-reason contract — an
    /// offline type explains itself (UX-19) instead of disappearing, beside the disabled
    /// generation section.</summary>
    [TestMethod]
    public void Summary_OfflineType_QuestionsBlockRendersDisabledWithReason()
    {
        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit,
            MakeDto(AssignmentStatusDto.Draft, type: AssignmentTypeDto.Manual,
                grading: GradingFormatDto.TeacherGraded),
            enterEditFields: false);

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("#authoring-questions").Should().ContainSingle();
            cut.FindAll("#authoring-questions-reason").Should().ContainSingle()
                .Which.TextContent.Trim().Should().Be(QuestionGenerationGate.DisabledHint);
            cut.FindComponents<QuestionEditorSection>().Should().NotBeEmpty(
                "the manual editor stays live beside the disabled generation");
        }, TimeSpan.FromSeconds(5));
    }

    /// <summary>§9.6: a summary whose children read failed keeps the P1 fail-closed contract — the
    /// Questions block renders disabled-with-reason, never a live-empty editor that a first add
    /// would turn into a full-replacement payload.</summary>
    [TestMethod]
    public void Summary_ChildrenReadFails_QuestionsBlockRendersDisabledWithReason()
    {
        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(AssignmentStatusDto.Draft),
            childrenReadFails: true, enterEditFields: false);

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("#authoring-questions-reason").Should().ContainSingle()
                .Which.TextContent.Trim().Should().Be(Authoring.ChildrenUnavailableReason);
            cut.FindComponents<QuestionEditorSection>().Should().BeEmpty();
        }, TimeSpan.FromSeconds(5));
    }

    /// <summary>§9.6: the kebab's "Generate questions" resolves FROM the summary — the target
    /// section is one of its three always-on blocks.</summary>
    [TestMethod]
    public void Summary_KebabGenerateQuestionsAction_ResolvesItsAnchor()
    {
        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(AssignmentStatusDto.Draft),
            enterEditFields: false);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW"), TimeSpan.FromSeconds(5));

        cut.Find("fluent-button[title=\"More assignment actions\"]").Click();

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("fluent-menu-item").Select(i => i.TextContent.Trim())
                .Should().Contain("Generate questions");
            cut.FindAll("#authoring-questions").Should().ContainSingle(
                "the anchor target exists on the summary");
        }, TimeSpan.FromSeconds(5));
    }

    /// <summary>D8: the breadcrumb trail on each route — three crumbs on Edit (the title crumb
    /// links to the assignment's Detail page), two on Create.</summary>
    [TestMethod]
    public void Breadcrumb_RendersTheTrailOnEveryRoute()
    {
        var dto = MakeDto(AssignmentStatusDto.Draft);
        var editCut = RenderAuthoring(AssignmentAuthoringMode.Edit, dto, enterEditFields: false);
        editCut.WaitForAssertion(() =>
        {
            var crumbs = editCut.FindComponents<FluentBreadcrumbItem>();
            crumbs.Should().HaveCount(3, "D8: Assignments › {title} › Edit");
            crumbs[0].Instance.Href.Should().Be("/assignments", "the root crumb links to the list");
            crumbs[1].Instance.Href.Should().Be($"/assignments/{dto.Id}",
                "the title crumb links to the assignment's Detail page");
            crumbs[1].Instance.ChildContent.Should().NotBeNull();
            crumbs[2].Instance.Href.Should().BeNull("the current crumb is not a link");
            editCut.Find("#authoring-breadcrumb").TextContent.Should().Contain("Math HW");
        }, TimeSpan.FromSeconds(5));

        var createCut = RenderAuthoring(AssignmentAuthoringMode.Create);
        createCut.WaitForAssertion(() =>
        {
            var crumbs = createCut.FindComponents<FluentBreadcrumbItem>();
            crumbs.Should().HaveCount(2, "D8: Assignments › New assignment");
            crumbs[1].Instance.Href.Should().BeNull();
            createCut.Find("#authoring-breadcrumb").TextContent.Should().Contain("New assignment");
        });
    }

    /// <summary>D6 + D14 (owner follow-up, 2026-10-09): Status &amp; available from LEADS the form,
    /// Subject + its strands/lessons row sit right after Title — BEFORE the Assignment type &amp;
    /// grading row (the subject is vital, D14) — and the Due date sits directly after the
    /// type &amp; grading row.</summary>
    [TestMethod]
    public void Basics_LeadsWithStatus_AndSubjectPrecedesTheTypeAndGradingRow()
    {
        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(AssignmentStatusDto.Draft));

        cut.WaitForAssertion(() =>
        {
            var basics = cut.Find("#authoring-basics");
            var order = basics.QuerySelectorAll(
                    "#authoring-basics-status, #authoring-basics-title, #authoring-basics-subject, " +
                    "#authoring-add-strand, #authoring-basics-type, #authoring-basics-due")
                .Select(e => e.Id).ToList();
            string[] expected =
            [
                "authoring-basics-status", "authoring-basics-title", "authoring-basics-subject",
                "authoring-add-strand", "authoring-basics-type", "authoring-basics-due"
            ];
            order.Should().ContainInOrder(expected,
                "D14: subject + its picks row lead the authoring decisions (before type/grading); D6: due date right after the type/grading row");
        });
    }

    /// <summary>D5/D6: Description + Instructions move out of Basics into their own bottom
    /// compartment — two inline cells whose rows put each label BENEATH its textarea
    /// (FormRow's RowLabelPosition.Below).</summary>
    [TestMethod]
    public void Instructions_OwnBottomCompartment_SideBySide_WithLabelsBelow()
    {
        var cut = RenderAuthoring(AssignmentAuthoringMode.Create);

        cut.WaitForAssertion(() =>
        {
            var section = cut.Find("#authoring-instructions");
            section.QuerySelector("h3.authoring-compartment-title").TextContent.Trim().Should().Be("Instructions",
                "the compartment title is the owner's label");

            cut.FindAll(".authoring-instructions-cell").Should().HaveCount(2,
                "D6: the two textareas sit side by side (inline)");
            cut.FindComponents<FluentTextArea>()
                .Where(t => t.Instance.Id is "authoring-basics-description" or "authoring-basics-instructions")
                .Select(t => t.Instance.Rows)
                .Should().OnlyContain(rows => rows == 4,
                    "D11: both textareas render at the same height");
            cut.FindAll(".authoring-instructions-cell .form-row")
                .Should().OnlyContain(r => r.ClassList.Contains("form-row--label-below"),
                    "each row's own label renders beneath its textarea");

            cut.Find("#authoring-basics").QuerySelectorAll("#authoring-basics-description, #authoring-basics-instructions")
                .Should().BeEmpty("D5: they are no longer part of Basics");

            cut.FindAll("section.authoring-compartment").Last().Id.Should().Be("authoring-instructions",
                "the compartment renders last on the page");
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
                "UX-17: an inapplicable control is disabled, never hidden");
            var passScore = cut.Find("#scoringFieldsPassScore");
            passScore.TagName.ToLowerInvariant().Should().Be("fluent-checkbox",
                "D11: the inapplicable state is a checkbox whose LABEL explains the disabled state");
            passScore.TextContent.Should().Contain(ScoringFieldsSection.ScoringInapplicableReason,
                "the label IS the explanation (FluentCheckbox renders its Label as child content)");
            cut.FindAll(".scoring-fields-reason").Should().BeEmpty(
                "D11: the standalone reason paragraph is gone");
            ScoringFieldDisabled(cut, "scoringFieldsMaxAttempts").Should().BeTrue();
            cut.Markup.Should().Contain(ScoringFieldsSection.ScoringInapplicableReason,
                "the label carries the reason the fields cannot be edited");
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
            cut.Find("#scoringFieldsPassScore").TagName.ToLowerInvariant().Should().Be("fluent-number-field",
                "D11: the applicable state renders the number field again");
            cut.FindAll("#scoringFieldsPassScore").Should().ContainSingle(
                "the field is still rendered — gating swaps the control, it never reflows the page");
            cut.Markup.Should().NotContain(ScoringFieldsSection.ScoringInapplicableReason);
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

    // ── Criterion 7 (D2/OD3/OD4): the Rules readouts ────────────────────────

    [TestMethod]
    public void Rules_HoldsTheFivePolicyReadoutsAndNoAuthorEditableInput()
    {
        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(AssignmentStatusDto.Draft));

        cut.WaitForAssertion(() =>
        {
            var rules = cut.Find("#authoring-rules");
            var rows = rules.QuerySelectorAll(".authoring-policy-row");
            rows.Should().HaveCount(5,
                "D2: approval, notification, archive window, signature requirement and guardian review");
            cut.FindAll("#authoring-rules fluent-badge").Should().BeEmpty(
                "D13: the per-row 'Inherited from Grade/Tenant policy' badge is removed — five rows no longer repeat one sentence");
            cut.FindAll("#authoring-rules [title]").Select(e => e.GetAttribute("title"))
                .Should().Contain(Authoring.PolicyInheritedHelpText,
                    "D13: the note is still stated — it rides the padlock tooltip");

            foreach (var row in rows)
            {
                row.QuerySelectorAll("input, fluent-text-field, fluent-number-field, fluent-select, fluent-checkbox, fluent-date-picker, fluent-listbox, fluent-text-area")
                    .Should().BeEmpty("UX-13/D2: a policy-derived value is never an editable control");
            }

            rules.QuerySelectorAll("fluent-number-field, fluent-date-picker, fluent-checkbox, fluent-text-field")
                .Should().BeEmpty("D2/AC8: no author-editable input remains inside Rules");
        });
    }

    /// <summary>D13 (owner, 2026-10-08) + D14 (owner follow-up, 2026-10-09): the inherited
    /// Grade/Tenant-policy note is a padlock tooltip whose appearance is CONDITIONAL — inherited
    /// from policy <b>and</b> not author-overridable. Four readouts (approval, notification, archive
    /// window, signature) are policy-owned end to end, so they always carry it; guardian review
    /// carries it only once the policy sets it (unset = the author chooses in Basics, so no lock).
    /// D14: the card header carries NO padlock of its own — only the rows state the lock.</summary>
    [TestMethod]
    public void Rules_PadlockHintAppearsOnlyForNonOverridablePolicyValues()
    {
        // Policy leaves guardian review unset → the author owns that choice, so its row is unlocked
        // while the four policy-owned readouts stay locked.
        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(AssignmentStatusDto.Draft));

        cut.WaitForAssertion(() =>
        {
            var rules = cut.Find("#authoring-rules");

            rules.QuerySelectorAll("fluent-badge").Should().BeEmpty(
                "D13: the inherited text badge is gone from every subitem");

            rules.QuerySelectorAll(".section-card__help").Should().BeEmpty(
                "D14: no card-level padlock beside the header text — the owner dropped it");

            var rowHelp = rules.QuerySelectorAll(".authoring-policy-help");
            rowHelp.Should().HaveCount(4,
                "D13: the four policy-owned readouts carry the padlock; guardian review does NOT — the author chooses it in Basics");
            rowHelp.Select(h => h.GetAttribute("title")).Should().AllBe(Authoring.PolicyInheritedHelpText,
                "D13: every locked subitem's padlock states the same inherited-policy note");
            rules.QuerySelectorAll("#authoring-policy-review .authoring-policy-help").Should().BeEmpty(
                "D13: an overridable value never claims a lock");
        });

        // Policy SETS guardian review → the Basics toggle locks (OD3) and the row gains the padlock.
        _effectivePolicyBody = EffectivePolicyBody(mandatoryReview: true);
        var locked = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(AssignmentStatusDto.Draft));

        locked.WaitForAssertion(() =>
        {
            var rules = locked.Find("#authoring-rules");
            rules.QuerySelectorAll(".authoring-policy-help").Should().HaveCount(5,
                "D13: a policy-set guardian review is a locked value like the other four");
            rules.QuerySelectorAll("#authoring-policy-review .authoring-policy-help").Should().ContainSingle(
                "D13: the padlock appears exactly on the row that just became non-overridable");
            rules.QuerySelectorAll(".section-card__help").Should().BeEmpty(
                "D14: the header stays padlock-free on every policy shape");
        });
    }

    /// <summary>D2/OD4: the archive-window and guardian-review readouts render the RESOLVED policy the
    /// page fetched — the page does not recompute the D4 implication, and an unset window states the
    /// built-in retention floor the write seam falls back to.</summary>
    [TestMethod]
    public void Rules_SignatureArchiveAndReviewReadouts_ReflectTheResolvedPolicy()
    {
        // Signature Mandatory, policy review TRUE (so the implication is observable both ways),
        // archive window 45.
        _effectivePolicyBody = EffectivePolicyBody(
            SignatureRequirementMode.Mandatory, mandatoryReview: true, archiveGraceDays: 45);

        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(AssignmentStatusDto.Draft));

        cut.WaitForAssertion(() =>
        {
            cut.Find("#authoring-policy-signature").TextContent
                .Should().Contain("A guardian signature is required after completion");
            cut.Find("#authoring-policy-archive").TextContent
                .Should().Contain("Archived 45 days after the due date");
            cut.Find("#authoring-policy-review").TextContent
                .Should().Contain("Guardian review is required before a student can submit");
        });
    }

    /// <summary>OD2: an unset archive window reads as the built-in retention floor rather than as a
    /// blank readout — the write seam stores exactly that value.</summary>
    [TestMethod]
    public void Rules_UnsetArchiveWindow_StatesTheBuiltInRetentionFloor()
    {
        _effectivePolicyBody = EffectivePolicyBody();

        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(AssignmentStatusDto.Draft));

        cut.WaitForAssertion(() => cut.Find("#authoring-policy-archive").TextContent
            .Should().Contain("Archived 30 days after the due date (built-in default)"));
    }

    // ── Round authoring-compact-fields (AC1–AC5): the Rules card + the paired short fields ──

    /// <summary>AC1: Rules renders through the shared <c>SectionCard</c>, inside the right-hand column
    /// and beneath the Targets &amp; audience card — it is no longer a full-width spine section, and the
    /// spine is left with the other compartments. (Form views only: a summary has no Rules card — D2
    /// keeps Rules behind the pencil.)</summary>
    [TestMethod]
    public void Rules_RendersThroughSectionCard_InTheRightColumnBeneathTheTargetsCard()
    {
        foreach (var mode in new[] { AssignmentAuthoringMode.Create, AssignmentAuthoringMode.Edit })
        {
            var cut = mode == AssignmentAuthoringMode.Create
                ? RenderAuthoring(mode)
                : RenderAuthoring(mode, MakeDto(AssignmentStatusDto.Draft));

            cut.WaitForAssertion(() =>
            {
                cut.Find("#authoring-targets").ClassList.Should().Contain("authoring-compartment--right",
                    "the right-hand column is the element the Rules card stacks in");
                cut.Find("#authoring-targets > #authoring-rules").Should().NotBeNull(
                    "the Rules container is a child of that column, so it stacks beneath the Targets card");

                var rules = cut.Find("#authoring-rules");
                rules.QuerySelectorAll("h3").Should().BeEmpty(
                    "the card header IS the compartment's single title — no hand-rolled h3");
                rules.QuerySelector(".section-card__body .authoring-policy-row").Should().NotBeNull(
                    "the readouts render as card ITEMS, not as bare children of a section");
                rules.NextElementSibling.Should().BeNull(
                    "the Rules card closes the column: Targets card → its audience readouts → Rules card");

                string[] expectedSections = mode == AssignmentAuthoringMode.Create
                    // D10: Create renders neither Content nor Questions.
                    ? ["authoring-basics", "authoring-targets", "authoring-instructions"]
                    : ["authoring-basics", "authoring-targets", "authoring-content",
                       "authoring-questions", "authoring-instructions"];
                cut.FindAll("section.authoring-compartment").Select(section => section.Id)
                    .Should().Equal(expectedSections,
                        "only the spine compartments are sections any more — Rules is a card, D5 adds Instructions, and D10 drops content+questions on Create");
            });
        }
    }

    /// <summary>AC2: the Rules card renders NO Add button and all five readouts, whose long-standing ids
    /// survive the move from hand-written rows to card items.</summary>
    [TestMethod]
    public void Rules_Card_RendersTheFiveReadoutsAsItems_AndNoAddButton()
    {
        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(AssignmentStatusDto.Draft));

        cut.WaitForAssertion(() =>
        {
            var rules = cut.Find("#authoring-rules");

            rules.QuerySelectorAll("fluent-button").Should().BeEmpty(
                "ShowAddButton=\"false\": a readouts-only card offers no Add");
            rules.QuerySelectorAll(".section-card__title").Should().ContainSingle()
                .Which.TextContent.Trim().Should().Be("Rules", "the card header carries the title");

            foreach (var id in PolicyReadoutIds)
            {
                rules.QuerySelectorAll($".section-card__body .authoring-policy-row#{id}").Should().ContainSingle(
                    $"{id} still renders — now as a card item");
            }

            rules.QuerySelectorAll(".authoring-policy-row").Should().HaveCount(5,
                "the five readouts and nothing else are the card's items");
        });
    }

    /// <summary>AC5 + D12: the jump-nav is gone, but every compartment ANCHOR it used to link to
    /// still resolves — the Rules card's container on Create, and the Questions section the kebab's
    /// <c>Generate questions</c> action targets on the edit surfaces.</summary>
    [TestMethod]
    public void CompartmentAnchors_StillResolve_WithoutTheJumpNav()
    {
        var create = RenderAuthoring(AssignmentAuthoringMode.Create);

        create.WaitForAssertion(() =>
        {
            create.FindAll("nav.authoring-jumpnav").Should().BeEmpty(
                "D12: the jump-nav is removed from the Create page");
            create.Find("#authoring-rules .section-card__title").TextContent.Trim().Should().Be("Rules",
                "the Rules container keeps its id — the anchor still resolves");
        });

        var edit = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(AssignmentStatusDto.Draft));

        edit.WaitForAssertion(() =>
        {
            edit.FindAll("nav.authoring-jumpnav").Should().BeEmpty(
                "D12: no jump-nav in the edit-fields view either");
            edit.Find("#authoring-questions").Should().NotBeNull(
                "the kebab's #authoring-questions anchor still resolves");
            edit.Find("#authoring-rules .section-card__title").TextContent.Trim().Should().Be("Rules");
        });
    }

    /// <summary>AC3: each short pair renders as exactly ONE multi-input <c>FormRow</c>. The two controls
    /// share one input cell and that cell is top-aligned (<c>AlignTop</c> — the repo's multi-input row
    /// pattern, which KEEPS the 180px label gutter so the pair lines up with every single-field row),
    /// instead of each field owning
    /// its own full-width row.</summary>
    [TestMethod]
    public void Basics_PairsTheShortFields_OneRowEach()
    {
        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit,
            MakeDto(AssignmentStatusDto.Draft, grading: GradingFormatDto.AutoGraded));

        cut.WaitForAssertion(() =>
        {
            var basics = cut.Find("#authoring-basics");

            var pairs = new (string Name, string[] Ids)[]
            {
                ("type & grading format", ["authoring-basics-type", "authoring-basics-grading"]),
                ("max score & max attempts", ["authoring-basics-max-score", "scoringFieldsMaxAttempts"]),
                ("status & available from", ["authoring-basics-status", "authoring-basics-available-from"])
            };

            foreach (var (name, ids) in pairs)
            {
                var rows = ids.Select(id => OwningFormRow(basics, id)).Distinct().ToList();
                rows.Should().ContainSingle($"the {name} pair shares ONE FormRow");

                var row = rows[0];
                row.ClassList.Should().Contain("form-row--horizontal",
                    $"the {name} pair KEEPS the 180px label gutter, so its controls line up with every single-field row");
                row.ClassList.Should().NotContain("form-row--label-below",
                    $"LabelPosition=Below drops the gutter and renders the pair single label under its FIRST control — the regression this pair must never reintroduce");
                row.ClassList.Should().Contain("form-row--align-top",
                    $"AlignTop top-aligns the {name} pair's cell (the flex-row-input-alignment pattern)");

                // The pair shares ONE input cell, and that cell holds exactly the two controls —
                // which is what makes them render side by side instead of stacked. (The cell's
                // element children also carry FluentUI's own <style> elements, so the assertion
                // counts the CONTROLS, never the cell's children.)
                var cell = row.QuerySelector(".form-row-input")!;
                cell.QuerySelectorAll("fluent-select, fluent-number-field, span.authoring-readonly-value")
                    .Should().HaveCount(2, $"both {name} controls sit in the ONE input cell, so they sit inline");
                ids.Should().OnlyContain(id => cell.QuerySelector($"#{id}") != null,
                    $"both {name} controls belong to that one row");

                foreach (var id in ids)
                {
                    cut.FindAll($"#{id}").Should().ContainSingle($"{id} renders exactly once");
                }
            }
        });
    }

    /// <summary>AC4 (regression guard): the pairing changed LAYOUT only. Every paired control still
    /// writes the same model member, the six ids are the ones the surface always exposed, and OD1's
    /// move is asserted where it is observable — the attempt cap renders in Basics and is no longer in
    /// <c>ScoringFieldsSection</c>'s own markup.</summary>
    [TestMethod]
    public void PairedFields_KeepTheirBindingsAndIds()
    {
        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit,
            MakeDto(AssignmentStatusDto.Draft, grading: GradingFormatDto.AutoGraded));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW",
            "Edit loads the assignment before the form is compared — a write before that load "
            + "would be overwritten by it"));

        cut.Find("#authoring-basics-max-score").Change("100");
        cut.Find("#scoringFieldsMaxAttempts").Change("3");
        cut.Find("#scoringFieldsPassScore").Change("70");

        var model = cut.FindComponents<ScoringFieldsSection>().Single().Instance.Model;
        model.MaxScore.Should().Be(100m, "the paired max score still binds _model.MaxScore");
        model.MaxAttempts.Should().Be(3, "the paired attempt cap still binds _model.MaxAttempts");
        model.PassScore.Should().Be(70m, "the pass score still binds _model.PassScore");

        cut.FindComponents<ScoringFieldsSection>().Single().Markup
            .Should().NotContain("scoringFieldsMaxAttempts",
                "OD1: the attempt cap left that section for the Basics pair");

        foreach (var id in new[]
                 {
                     "authoring-basics-type", "authoring-basics-grading", "authoring-basics-max-score",
                     "scoringFieldsMaxAttempts", "authoring-basics-status", "authoring-basics-available-from"
                 })
        {
            cut.FindAll($"#{id}").Should().ContainSingle($"{id} is unchanged by the pairing");
        }
    }

    // ── OD3/AC10: the guardian-review toggle lives in Basics ────────────────

    /// <summary>OD3: the author toggle is free (unlocked, author-controlled) exactly while the
    /// effective policy leaves review unset.</summary>
    [TestMethod]
    public void GuardianReviewToggle_UnsetPolicy_IsAuthorEditableInBasics()
    {
        _effectivePolicyBody = EffectivePolicyBody(SignatureRequirementMode.Disabled, mandatoryReview: null);

        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(AssignmentStatusDto.Draft));

        cut.WaitForAssertion(() =>
        {
            var toggle = cut.FindComponents<FluentCheckbox>()
                .Single(c => c.Instance.Id == "authoring-basics-mandatory-review").Instance;
            toggle.Disabled.Should().BeFalse("an unset policy leaves the review choice to the author");
            cut.FindAll("#authoring-basics #authoring-basics-mandatory-review").Should().ContainSingle(
                "OD3: the toggle lives in Basics, not in the readouts-only Rules section");
        });
    }

    /// <summary>OD3/AC10: a policy-set review value locks the toggle and explains why; Rules still
    /// carries the policy readout row.</summary>
    [TestMethod]
    public void GuardianReviewToggle_PolicySetReview_IsLockedWithAReason()
    {
        _effectivePolicyBody = EffectivePolicyBody(mandatoryReview: false);

        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(AssignmentStatusDto.Draft));

        cut.WaitForAssertion(() =>
        {
            var toggle = cut.FindComponents<FluentCheckbox>()
                .Single(c => c.Instance.Id == "authoring-basics-mandatory-review").Instance;
            toggle.Disabled.Should().BeTrue("D3: a policy-set value is not author-editable");
            toggle.Value.Should().BeFalse("the locked toggle mirrors the policy's value");
            cut.Markup.Should().Contain(Authoring.PolicyReviewLockedReason);
            cut.Find("#authoring-policy-review").TextContent
                .Should().Contain("Guardian review is not required");
        });
    }

    /// <summary>AC10 (D10): the author-facing guardian-signature checkbox is retired — nothing on the
    /// page binds a <c>RequiresSignature</c> control any more; the policy readout states the outcome.</summary>
    [TestMethod]
    public void GuardianSignatureCheckbox_IsRetired_AndThePolicyRowStatesTheOutcome()
    {
        _effectivePolicyBody = EffectivePolicyBody(SignatureRequirementMode.Mandatory);

        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(AssignmentStatusDto.Draft));

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().NotContain("authoring-submission-requires-signature");
            cut.Markup.Should().NotContain("Require guardian signature after completion");
            cut.Find("#authoring-policy-signature").TextContent
                .Should().Contain("A guardian signature is required after completion");
        });
    }

    /// <summary>AC9 (D9): the author-fed scoring fields moved into Basics, directly under the Grading
    /// format control that drives them.</summary>
    [TestMethod]
    public void ScoringFields_RenderInsideBasics_UnderTheGradingFormat()
    {
        _effectivePolicyBody = EffectivePolicyBody();

        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit,
            MakeDto(AssignmentStatusDto.Draft, grading: GradingFormatDto.AutoGraded));

        cut.WaitForAssertion(() =>
        {
            var basics = cut.Find("#authoring-basics");
            basics.QuerySelector("#authoring-basics-max-score").Should().NotBeNull();
            basics.InnerHtml.Should().Contain("Feedback mode:",
                "D11: the feedback-mode narrative rides the (i) help icon on the grading row's label");
            basics.InnerHtml.Should().Contain(ScoringFieldsSection.PassScoreHint,
                "D11: the pass-threshold hint rides the Pass Score row's (i) help icon");
            basics.InnerHtml.Should().Contain(
                QuestionGenerationGate.HintText(AssignmentTypeDto.Digital, GradingFormatDto.AutoGraded),
                "D11: the AI-availability text rides the same merged icon on the paired row");
            basics.QuerySelectorAll(".form-row-help").Should().HaveCount(2,
                "D11: exactly the type/grading row and the Pass Score row carry help icons "
                + "(Guardian review's is conditional and its policy is unset here)");

            // D14f: the FIRST help icon is the type/grading row's — its tooltip is three
            // labelled lines (purpose / AI availability / feedback mode), and the purpose
            // sentence is the documented causal chain (§3.3 immediate-vs-held; FR-220 gate).
            var gradingRowHelp = basics.QuerySelectorAll(".form-row-help")[0]
                .GetAttribute("title") ?? string.Empty;
            gradingRowHelp.Should().Contain(Authoring.TypeGradingPurposeHint,
                "D14f: the row's (i) icon explains what type & grading DRIVE, not just what they are");
            gradingRowHelp.Split('\n').Should().HaveCount(3,
                "D14f/Q1a: three labelled lines — purpose, AI availability, feedback mode");
            basics.QuerySelector("#scoringFieldsPassScore").Should().NotBeNull();
            cut.FindAll("#authoring-submission").Should().BeEmpty(
                "D1: the Submission & Sign-off compartment is retired");

            // "Directly after" the Grading format field: the scoring controls follow it in Basics.
            // D14 moved the Subject picker ABOVE the type/grading row — its new position is pinned
            // by Basics_LeadsWithStatus_AndSubjectPrecedesTheTypeAndGradingRow.
            var order = basics.QuerySelectorAll(
                "#authoring-basics-grading, #authoring-basics-max-score, #scoringFieldsPassScore")
                .Select(e => e.Id).ToList();
            order.Should().ContainInOrder(
                "authoring-basics-grading", "authoring-basics-max-score", "scoringFieldsPassScore");
        });
    }

    /// <summary>AC11 (OD4): the page reads the whole effective policy from the new route and never the
    /// retired <c>/signature-default</c> one. The legacy matcher is registered (and counted) so the
    /// assertion is about the PAGE's traffic, not about the route's absence from the API.</summary>
    [TestMethod]
    public void EffectivePolicy_IsFetchedFromTheNewRoute_AndTheLegacyRouteIsNeverCalled()
    {
        var legacyRoute = _mockHttp.When(HttpMethod.Get, "http://localhost/assignments/signature-default")
            .Respond(HttpStatusCode.OK, "application/json", "{}");

        var cut = RenderAuthoring(AssignmentAuthoringMode.Create);

        cut.WaitForAssertion(() =>
        {
            _effectivePolicyQueries.Should().NotBeEmpty(
                "the Rules readouts and the review lock are resolved from /assignments/effective-policy");
            _effectivePolicyQueries.Should().OnlyContain(q => !q.Contains("gradeLevelId=", StringComparison.Ordinal),
                "a create with no grade target resolves the tenant-global default");
        });
        _mockHttp.GetMatchCount(legacyRoute).Should().Be(0,
            "OD4: the page's only policy read is the effective-policy route");
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

    /// <summary>P1's Create half + D10: Create never attempts the children read (no fail-closed
    /// reason anywhere), and — because both authoring-children sections live on the draft-edit
    /// surface now — it renders NO question or resource editor at all.</summary>
    [TestMethod]
    public void Create_ChildrenReadIsNotAttempted_AndNoEditorsRender()
    {
        var cut = RenderAuthoring(AssignmentAuthoringMode.Create);

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("#authoring-content-reason").Should().BeEmpty(
                "Create has no persisted children to lose, so no fail-closed reason is raised");
            cut.FindComponents<ResourcesSection>().Should().BeEmpty("D10: Content & Resources starts at draft-edit time");
            cut.FindComponents<QuestionEditorSection>().Should().BeEmpty(
                "D10: question authoring starts at draft-edit time");
            cut.FindComponents<QuestionGenerationSection>().Should().BeEmpty("D10");
        });
    }

    // ── Shared fixtures for the builder's group/FR-58 coverage ──────────────

    private static Guid GroupAId { get; } = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111");

    private static Guid GroupBId { get; } = Guid.Parse("bbbbbbbb-2222-2222-2222-222222222222");

    /// <summary>Stubs the FR-58 subject union an activity-group target resolves against.</summary>
    private void SetupGroupSubjects(params Guid[] topicIds)
    {
        _mockHttp.When(HttpMethod.Get, "http://localhost/students/subjects/by-group/*")
            .Respond(HttpStatusCode.OK, "application/json", JsonSerializer.Serialize(
                topicIds.Select(id => new { id, name = "Mathematics", displayOrder = 0 }), _apiJsonOptions));
    }

    /// <summary>
    /// Stubs the advisory recipient-preview read and returns the number of reads issued so far. The
    /// authoring page's load ends by SCHEDULING that read, so a non-zero count is the deterministic
    /// "the load has settled" signal the builder tests need before they author entries — the load's own
    /// <c>RebuildTargetsAndAudienceFromModel</c> would otherwise clear entries added mid-load.
    /// </summary>
    private Func<int> SetupRecipientPreviewRead(
        string body = "{\"studentsMatched\":0,\"primaryContacts\":0,\"otherContacts\":0,\"previewDegraded\":false}")
    {
        var reads = 0;
        _mockHttp.When(HttpMethod.Get, "http://localhost/assignments/recipient-preview*")
            .Respond(_ =>
            {
                reads++;
                return PreviewResponse(body);
            });

        return () => reads;
    }

    /// <summary>Waits for the page's load to settle behind its own preview debounce.</summary>
    private static void WaitForLoadSettled(IRenderedComponent<Authoring> cut, Func<int> previewReads) =>
        cut.WaitForAssertion(() => previewReads().Should().BeGreaterThan(0,
            "the load settles behind its own preview debounce"), TimeSpan.FromSeconds(5));

    /// <summary>Clicks the Edit action bar's "Save draft" kebab item (the update path).</summary>
    private static void SaveDraft(IRenderedComponent<Authoring> cut)
    {
        cut.Find("fluent-button[title=\"More assignment actions\"]").Click();
        cut.WaitForAssertion(() => cut.FindAll("fluent-menu-item")
            .Should().Contain(i => i.TextContent.Trim() == "Save draft",
                "the Draft's kebab hosts the save action; rendered items were: ["
                + string.Join(", ", cut.FindAll("fluent-menu-item").Select(i => i.TextContent.Trim())) + "]"),
            TimeSpan.FromSeconds(5));
        cut.FindAll("fluent-menu-item").Single(i => i.TextContent.Trim() == "Save draft").Click();
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

    // ── §17 item 1: a lifecycle action re-reads the persisted set ─────────

    /// <summary>§17 (item 1, this round): <c>ReloadAsync</c> re-read only the scalar summary, so a lifecycle
    /// action that moved the assignment to a status whose surface edits again — Published → Unpublish →
    /// Draft is the visible one — landed the author on an editable form whose editors were still
    /// DISABLED-with-reason ("the saved questions could not be loaded"), with the audience chips empty.
    /// The re-read now runs the same persisted-set load the initial Edit load runs.
    /// <para>Discriminating: pre-fix the children/targets read is never issued on this path, so
    /// <c>#authoring-content-reason</c> still renders and the chip list is empty — neither assertion
    /// can be satisfied by the summary refresh alone.</para></summary>
    [TestMethod]
    public async Task Unpublish_IntoAnEditableDraft_ReadsThePersistedChildrenAndTargets()
    {
        var captured = new List<ConfirmDialogContent>();
        RegisterConfirmationDialog(confirm: true, captured);

        var gradeId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        _gradeLevels =
        [
            new GradeLevelDto(gradeId, Guid.NewGuid(), 5, "Grade 5", 0, 0, 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        ];

        var dto = MakeDto(AssignmentStatusDto.Published);
        var children = MakeChildren(
            questions: [LoadedQuestion()],
            targets: [new AssignmentTargetDto(TargetKindDto.GradeLevel, gradeId, 0)]);

        // The detail read answers the CURRENT status, so the reload the lifecycle action performs lands
        // in Draft; both registrations come BEFORE RenderAuthoring's own detail read (MockHttp v6
        // matches in registration order).
        _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{dto.Id}")
            .Respond(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(dto, _apiJsonOptions),
                    Encoding.UTF8, "application/json")
            });
        _mockHttp.When(HttpMethod.Post, $"http://localhost/assignments/{dto.Id}/unpublish")
            .Respond(_ =>
            {
                dto = dto with { Status = AssignmentStatusDto.Draft };
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            });

        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, dto, children);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW"));

        cut.Find("#authoring-primary-action").Click();

        cut.WaitForAssertion(() =>
        {
            captured.Should().ContainSingle("Unpublish asks before it acts");

            cut.Find("#authoring-primary-action").TextContent.Trim().Should().Be("Publish",
                "the unpublish landed and the surface is an editable Draft again");

            // Only the fresh read can produce these: a read-only View never reads the persisted set, so
            // the surface had none until the re-read ran.
            cut.FindAll("#authoring-content-reason").Should().BeEmpty(
                "§17 item 1: the persisted children were re-read, so the content editors are live");

            var questions = cut.FindComponents<QuestionEditorSection>().Single().Instance.Model.Questions;
            questions.Should().HaveCount(1);
            questions[0].QuestionText.Should().Be("Loaded question?",
                "... and they carry what the server holds");

            BuilderEntries(cut).Should().Equal(new[] { ("Grade Levels", "Grade 5") },
                "§17 item 1: the target rows came back with them");
        }, TimeSpan.FromSeconds(15));
    }

    /// <summary>§17 (item 1, this round) — the half the re-read must NOT break: Unpublish from a still-editable
    /// Scheduled assignment leaves the author on a form they can save, without having saved it, so the
    /// persisted rows must not be written over the ones they are still editing. (The scalar refresh is the
    /// reload's pre-existing behaviour and is untouched.)
    /// <para>Non-regression anchor by construction: pre-fix the child rows were never re-read at all, so this
    /// passes there too. It exists to keep the new re-read from becoming the data-loss path — removing the
    /// unsaved-edits gate fails it.</para></summary>
    [TestMethod]
    public async Task Unpublish_FromScheduled_WithUnsavedRows_KeepsTheAuthorsEdits()
    {
        var captured = new List<ConfirmDialogContent>();
        RegisterConfirmationDialog(confirm: true, captured);

        var dto = MakeDto(AssignmentStatusDto.Scheduled);
        _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{dto.Id}")
            .Respond(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(dto, _apiJsonOptions),
                    Encoding.UTF8, "application/json")
            });
        _mockHttp.When(HttpMethod.Post, $"http://localhost/assignments/{dto.Id}/unpublish")
            .Respond(_ =>
            {
                dto = dto with { Status = AssignmentStatusDto.Draft };
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            });

        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, dto, MakeChildren(questions: [LoadedQuestion()]));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Loaded question?"));

        // The author's unsaved edit, written exactly the way the editor's own binding writes it.
        var section = cut.FindComponents<QuestionEditorSection>().Single();
        await cut.InvokeAsync(() => section.Instance.Model.Questions[0].QuestionText = "Author's unsaved edit");

        cut.Find("#authoring-primary-action").Click();

        cut.WaitForAssertion(() =>
        {
            captured.Should().ContainSingle();
            cut.Find("#authoring-primary-action").TextContent.Trim().Should().Be("Publish",
                "the unpublish landed");
            cut.FindComponents<QuestionEditorSection>().Single().Instance.Model.Questions[0].QuestionText
                .Should().Be("Author's unsaved edit",
                    "§17 item 1: re-reading the persisted rows onto a form with unsaved work would discard " +
                    "the author's questions — the reload holds back instead");
        }, TimeSpan.FromSeconds(15));
    }

    // ── §17 item 5: the dirty snapshot is a fingerprint of the payload ─────

    private static readonly Guid SnapshotProbeTopicId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid SnapshotProbeGradeId = Guid.Parse("88888888-8888-8888-8888-888888888888");

    /// <summary>§17 (item 5): a form model carrying a value in every field the save payload projects, so one
    /// mutation of any of them can be exercised against the snapshot.</summary>
    private static AssignmentEditFormModel SnapshotProbeModel(
        Action<AssignmentEditFormModel>? configure = null,
        int questions = 1)
    {
        var model = new AssignmentEditFormModel
        {
            Title = "Math HW",
            Description = "internal note",
            Instructions = "Do the odd numbers",
            DueDate = new DateTime(2026, 5, 1, 9, 0, 0),
            MaxScore = 10m,
            PassScore = 5m,
            MaxAttempts = 2,
            AiPromptOverride = "be gentle",
            DifficultyEasyCount = 1,
            DifficultyMediumCount = 2,
            DifficultyHardCount = 3
        };

        for (var i = 0; i < questions; i++)
        {
            model.AddQuestion(new QuestionEditorRow
            {
                QuestionText = $"Question {i}",
                Type = QuestionTypeDto.MultipleChoice,
                ModelAnswer = "answer",
                Options = { new OptionEditorRow { OptionText = "A" }, new OptionEditorRow { OptionText = "B" } },
                CorrectOptionIndex = 0
            });
        }

        model.AddAttachment(new AttachmentEditorRow
        {
            FileName = "syllabus.pdf",
            ContentType = "application/pdf",
            FileSize = 2048,
            StoragePath = "tenants/t/staging/syllabus.pdf"
        });
        model.AddResourceUrl("https://example.test/ref");
        model.LoadTargets([new AssignmentTargetDto(TargetKindDto.GradeLevel, SnapshotProbeGradeId, 0)]);

        configure?.Invoke(model);
        return model;
    }

    /// <summary>§17 (item 5): both spellings of the save payload for one model state — the round's
    /// fingerprint, and the payload itself serialized exactly as the pre-fix snapshot serialized it.</summary>
    private static (string Snapshot, string Payload) CaptureProbe(
        AssignmentEditFormModel model, bool? mandatoryReview = true) =>
        (model.CaptureSaveSnapshot(
             AssignmentTypeDto.Manual, GradingFormatDto.TeacherGraded, TargetAudienceTypeDto.SelectedGrades,
             SnapshotProbeTopicId, mandatoryReview),
         JsonSerializer.Serialize(model.ToUpdateRequest(
             AssignmentTypeDto.Manual, GradingFormatDto.TeacherGraded, TargetAudienceTypeDto.SelectedGrades,
             SnapshotProbeTopicId, mandatoryReview,
             targets: model.ToTargetDtos())));

    /// <summary>§17 (item 5): asserts the snapshot's equality relation matches the payload's, in whichever
    /// direction the case runs — a payload-relevant change must move the digest, and a difference the
    /// payload normalizes away must not.</summary>
    private static void AssertSnapshotTracksPayload(string what, AssignmentEditFormModel baseline, AssignmentEditFormModel mutated)
    {
        var (beforeSnapshot, beforePayload) = CaptureProbe(baseline);
        var (afterSnapshot, afterPayload) = CaptureProbe(mutated);

        (afterSnapshot != beforeSnapshot).Should().Be(afterPayload != beforePayload,
            $"§17 item 5: the dirty snapshot must track the save payload exactly ({what})");
    }

    /// <summary>§17 (item 5, this round): the dirty guard reads the save snapshot on EVERY render, so the
    /// snapshot is a fixed-width fingerprint of the payload instead of the payload itself.
    /// <para>Discriminating: pre-fix <c>CaptureSaveSnapshot</c> returned <c>JsonSerializer.Serialize</c> of the
    /// whole update request — a JSON document whose length tracks the question list — so neither the digest
    /// assertion nor the fixed-width one can hold against it.</para></summary>
    [TestMethod]
    public void SaveSnapshot_IsAFixedWidthFingerprint_NotTheMaterializedPayload()
    {
        var small = CaptureProbe(SnapshotProbeModel(questions: 1));
        var large = CaptureProbe(SnapshotProbeModel(questions: 40));

        small.Snapshot.Should().MatchRegex("^[0-9a-f]{32}$",
            "§17 item 5: the per-render dirty comparison reads a digest of the save payload");
        large.Snapshot.Should().HaveLength(small.Snapshot.Length,
            "the snapshot's cost must not track the size of the form — materializing it per render is the residual");

        small.Payload.Should().NotBe(large.Payload,
            "vacuity guard: the two models really are two different payloads");
        small.Snapshot.Should().NotBe(large.Snapshot,
            "... and the digest still tells them apart");
    }

    /// <summary>§17 (item 5, this round): the semantic guard for the fingerprint. The dirty guard is correct
    /// only while "the digests differ" means exactly "the next save would write something different", so
    /// every payload-relevant field is exercised in BOTH directions: a state that changes the payload must
    /// change the digest, and a difference the payload normalizes away (a null Title and an empty one, a
    /// DateTime differing only in <c>Kind</c>, a blank option text) must not.
    /// <para>Non-regression anchor by construction: pre-fix the snapshot WAS the serialized payload, so the
    /// equivalence held trivially — it is here for the implementation that replaced it.</para></summary>
    [TestMethod]
    public void SaveSnapshot_MatchesTheSerializedPayload_OnEveryDirtyRelevantField()
    {
        // ── Scalars
        AssertSnapshotTracksPayload("Title", SnapshotProbeModel(), SnapshotProbeModel(m => m.Title = "Other"));
        AssertSnapshotTracksPayload("Title null→empty (payload-equal)",
            SnapshotProbeModel(m => m.Title = null), SnapshotProbeModel(m => m.Title = string.Empty));
        AssertSnapshotTracksPayload("Description", SnapshotProbeModel(), SnapshotProbeModel(m => m.Description = null));
        AssertSnapshotTracksPayload("Instructions", SnapshotProbeModel(), SnapshotProbeModel(m => m.Instructions = "Other"));
        AssertSnapshotTracksPayload("DueDate", SnapshotProbeModel(), SnapshotProbeModel(m => m.DueDate = m.DueDate!.Value.AddDays(1)));
        AssertSnapshotTracksPayload("DueDate Kind only (payload-equal)",
            SnapshotProbeModel(m => m.DueDate = DateTime.SpecifyKind(m.DueDate!.Value, DateTimeKind.Utc)),
            SnapshotProbeModel(m => m.DueDate = DateTime.SpecifyKind(m.DueDate!.Value, DateTimeKind.Unspecified)));
        AssertSnapshotTracksPayload("MaxScore", SnapshotProbeModel(), SnapshotProbeModel(m => m.MaxScore = 20m));
        AssertSnapshotTracksPayload("MaxScore null→zero", SnapshotProbeModel(m => m.MaxScore = null), SnapshotProbeModel(m => m.MaxScore = 0m));
        AssertSnapshotTracksPayload("PassScore", SnapshotProbeModel(), SnapshotProbeModel(m => m.PassScore = 6m));
        AssertSnapshotTracksPayload("MaxAttempts", SnapshotProbeModel(), SnapshotProbeModel(m => m.MaxAttempts = 3));
        AssertSnapshotTracksPayload("difficulty mix", SnapshotProbeModel(), SnapshotProbeModel(m => m.DifficultyHardCount = 4));
        AssertSnapshotTracksPayload("AiPromptOverride", SnapshotProbeModel(), SnapshotProbeModel(m => m.AiPromptOverride = null));

        // R4 (CP-5 checklist): the picks join the fingerprint. A payload field the fingerprint cannot
        // distinguish makes a real edit read as clean, and CP-10 gives the picks THREE wire states —
        // null (preserve) / empty (clear) / non-empty (replace). "Not loaded" is the preserve case;
        // "loaded and empty" is the clear — the two must not collide.
        var strandPick = Guid.NewGuid();
        var lessonPick = Guid.NewGuid();
        AssertSnapshotTracksPayload("context picks: not loaded → loaded-empty (preserve vs clear)",
            SnapshotProbeModel(), SnapshotProbeModel(m => m.LoadContextPicks([], [])));
        AssertSnapshotTracksPayload("context picks not loaded (payload-equal)",
            SnapshotProbeModel(), SnapshotProbeModel(m => m.ContextStrandIds.Add(strandPick)));
        AssertSnapshotTracksPayload("context strand picks",
            SnapshotProbeModel(m => m.LoadContextPicks([], [])),
            SnapshotProbeModel(m =>
            {
                m.LoadContextPicks([], []);
                m.ContextStrandIds.Add(strandPick);
            }));
        AssertSnapshotTracksPayload("context lesson picks",
            SnapshotProbeModel(m => m.LoadContextPicks([], [])),
            SnapshotProbeModel(m =>
            {
                m.LoadContextPicks([], []);
                m.ContextLessonIds.Add(lessonPick);
            }));

        // OD1/D3: guardian review is nullable on the wire now — "unset" (the author left it to the
        // policy) is a different payload from an explicit value, so the fingerprint must tell them apart.
        CaptureProbe(SnapshotProbeModel(), mandatoryReview: true).Snapshot.Should().NotBe(
            CaptureProbe(SnapshotProbeModel(), mandatoryReview: null).Snapshot,
            "a payload field the snapshot cannot distinguish makes a real edit read as clean");
        CaptureProbe(SnapshotProbeModel(), mandatoryReview: null).Snapshot.Should().NotBe(
            CaptureProbe(SnapshotProbeModel(), mandatoryReview: false).Snapshot,
            "null and false are two different payloads (OD1)");

        // ── Questions
        AssertSnapshotTracksPayload("question text", SnapshotProbeModel(), SnapshotProbeModel(m => m.Questions[0].QuestionText = "Other"));
        AssertSnapshotTracksPayload("question type", SnapshotProbeModel(), SnapshotProbeModel(m => m.Questions[0].Type = QuestionTypeDto.ShortAnswer));
        AssertSnapshotTracksPayload("model answer", SnapshotProbeModel(), SnapshotProbeModel(m => m.Questions[0].ModelAnswer = null));
        AssertSnapshotTracksPayload("option text", SnapshotProbeModel(), SnapshotProbeModel(m => m.Questions[0].Options[1].OptionText = "Other"));
        AssertSnapshotTracksPayload("option text null→empty (payload-equal)",
            SnapshotProbeModel(m => m.Questions[0].Options[1].OptionText = null),
            SnapshotProbeModel(m => m.Questions[0].Options[1].OptionText = string.Empty));
        AssertSnapshotTracksPayload("correct option", SnapshotProbeModel(), SnapshotProbeModel(m => m.Questions[0].CorrectOptionIndex = 1));
        AssertSnapshotTracksPayload("added question", SnapshotProbeModel(), SnapshotProbeModel(questions: 2));
        AssertSnapshotTracksPayload("removed question", SnapshotProbeModel(questions: 2), SnapshotProbeModel(questions: 1));
        AssertSnapshotTracksPayload("reordered questions", SnapshotProbeModel(questions: 2), SnapshotProbeModel(
            m =>
            {
                var first = m.Questions[0];
                m.Questions.RemoveAt(0);
                m.Questions.Add(first);
            },
            questions: 2));

        // ── Attachments
        AssertSnapshotTracksPayload("attachment file name", SnapshotProbeModel(), SnapshotProbeModel(m => m.Attachments[0].FileName = "other.pdf"));
        AssertSnapshotTracksPayload("attachment content type", SnapshotProbeModel(), SnapshotProbeModel(m => m.Attachments[0].ContentType = null));
        AssertSnapshotTracksPayload("attachment size", SnapshotProbeModel(), SnapshotProbeModel(m => m.Attachments[0].FileSize = 4096));
        AssertSnapshotTracksPayload("attachment storage path", SnapshotProbeModel(), SnapshotProbeModel(m => m.Attachments[0].StoragePath = null));
        AssertSnapshotTracksPayload("removed attachment", SnapshotProbeModel(), SnapshotProbeModel(m => m.Attachments.Clear()));

        // ── ContentModules: the payload field no editor owns yet — the matrix covers it anyway, so a
        // field that lands on the wire without a matching mix in the walk fails here instead of
        // turning a real edit into a silent "clean" (P2-1, this round's rework).
        static IReadOnlyList<NewContentModuleDto> Modules(
            string url = "https://example.test/guide", int threshold = 100) =>
            [new NewContentModuleDto(ModuleTypeDto.Guide, "Guide", url, null, 0, threshold)];

        AssertSnapshotTracksPayload("added content module", SnapshotProbeModel(),
            SnapshotProbeModel(m => m.ContentModules = Modules()));
        AssertSnapshotTracksPayload("removed content module",
            SnapshotProbeModel(m => m.ContentModules = Modules()), SnapshotProbeModel());
        AssertSnapshotTracksPayload("content module url",
            SnapshotProbeModel(m => m.ContentModules = Modules()),
            SnapshotProbeModel(m => m.ContentModules = Modules(url: "https://example.test/other")));
        AssertSnapshotTracksPayload("content module threshold",
            SnapshotProbeModel(m => m.ContentModules = Modules()),
            SnapshotProbeModel(m => m.ContentModules = Modules(threshold: 80)));
        AssertSnapshotTracksPayload("content modules empty→null (payload-equal)",
            SnapshotProbeModel(m => m.ContentModules = null),
            SnapshotProbeModel(m => m.ContentModules = []));
        CaptureProbe(SnapshotProbeModel(m => m.ContentModules = Modules())).Snapshot.Should().NotBe(
            CaptureProbe(SnapshotProbeModel()).Snapshot,
            "P2-1: ContentModules rides ToUpdateRequest, so the walk must mix it — a payload field the " +
            "snapshot omits makes a real edit read as clean and the navigation prompt never fires");

        // ── Resources
        AssertSnapshotTracksPayload("resource url", SnapshotProbeModel(),
            SnapshotProbeModel(m => m.ResourceUrls[0] = new ResourceUrlRow("https://example.test/other", null)));
        AssertSnapshotTracksPayload("resource display name", SnapshotProbeModel(),
            SnapshotProbeModel(m => m.ResourceUrls[0] = new ResourceUrlRow("https://example.test/ref", "Ref")));
        AssertSnapshotTracksPayload("removed url resource", SnapshotProbeModel(), SnapshotProbeModel(m => m.ResourceUrls.Clear()));
        AssertSnapshotTracksPayload("preserved resource", SnapshotProbeModel(),
            SnapshotProbeModel(m => m.PreservedResources.Add(
                new NewResourceDto(ResourceKindDto.File, null, "tenants/t/staging/x.pdf", "Syllabus", true))));

        // ── Targets
        AssertSnapshotTracksPayload("added target", SnapshotProbeModel(),
            SnapshotProbeModel(m => m.SetTargetsOfKind(TargetKindDto.Stream, [Guid.NewGuid()])));
        AssertSnapshotTracksPayload("removed target", SnapshotProbeModel(), SnapshotProbeModel(m => m.LoadTargets([])));
        AssertSnapshotTracksPayload("everyone target", SnapshotProbeModel(), SnapshotProbeModel(m => m.SetEveryoneTarget(true)));
        AssertSnapshotTracksPayload("reordered targets",
            SnapshotProbeModel(m => m.LoadTargets(
            [
                new AssignmentTargetDto(TargetKindDto.GradeLevel, SnapshotProbeGradeId, 0),
                new AssignmentTargetDto(TargetKindDto.Stream, SnapshotProbeTopicId, 1)
            ])),
            SnapshotProbeModel(m => m.LoadTargets(
            [
                new AssignmentTargetDto(TargetKindDto.Stream, SnapshotProbeTopicId, 0),
                new AssignmentTargetDto(TargetKindDto.GradeLevel, SnapshotProbeGradeId, 1)
            ])));
        AssertSnapshotTracksPayload("loaded-empty vs never loaded (payload-equal)",
            SnapshotProbeModel(m => m.LoadTargets(null)), SnapshotProbeModel(m => m.LoadTargets([])));

        // ── Non-vacuity: the matrix runs in both directions (a case that must move the payload, and one
        // that must not), so a helper that compared nothing would fail here rather than pass silently.
        CaptureProbe(SnapshotProbeModel()).Payload.Should().NotBe(
            CaptureProbe(SnapshotProbeModel(m => m.Title = "Other")).Payload,
            "a payload-relevant change really does move the reference payload");
        CaptureProbe(SnapshotProbeModel(m => m.Title = null)).Payload.Should().Be(
            CaptureProbe(SnapshotProbeModel(m => m.Title = string.Empty)).Payload,
            "... and a normalized-away difference really does not");
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

    // ── UX-7 / F17 defence-in-depth: the reload path's own dirty gate (this round) ──

    /// <summary>The notice the parameter-change reload renders when it discarded unsaved edits no
    /// confirmation consented to. Pinned here as a literal because the component keeps it
    /// <c>private</c> — this round adds no public API member (D3), so the element id plus this wording
    /// is what the suite holds the page to.</summary>
    private const string UnsavedEditsDiscardedNoticeText =
        "This form was reloaded for a different assignment or mode without a confirmation, so the unsaved " +
        "edits you had made were discarded.";

    /// <summary>The notice element, addressed by the component's own id.</summary>
    private const string UnsavedEditsDiscardedNoticeId = "#authoring-unsaved-edits-discarded";

    /// <summary>UX-7 / F17 defence-in-depth (this round, criterion 3): a host that swaps <c>Id</c>
    /// WITHOUT a <c>NavigationManager</c> navigation — no location-changing handler ran, so nothing
    /// asked the author — still reloads the assignment the route now names (D2: refusing the reload
    /// would keep A's state on screen under B's URL, the stale-write path F17 removed), and the discard
    /// is announced instead of silent.
    /// <para>Discriminating: before this round the reload carried no notice at all, so the second half
    /// fails while the first passes — the reload half is F17's, already in place.</para></summary>
    [TestMethod]
    public async Task UnsavedEdits_UnconsentedIdChange_ReloadsAndAnnouncesTheDiscard()
    {
        var dtoA = MakeDto(AssignmentStatusDto.Draft);
        var dtoB = dtoA with { Id = Guid.NewGuid(), Title = "Science HW" };

        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, dtoA);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW"));

        await SetTitleAsync(cut, "Edited title");
        cut.FindAll(UnsavedEditsDiscardedNoticeId).Should().BeEmpty(
            "vacuity anchor: a dirty form whose parameters did NOT change has nothing to announce");

        SetupAssignment(dtoB);
        cut.Render(p => p
            .Add(x => x.Mode, AssignmentAuthoringMode.Edit)
            .Add(x => x.Id, dtoB.Id));

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Science HW",
                "D2: the reload still happens — refusing it would keep A's state under B's URL");
            cut.Markup.Should().NotContain("Edited title", "A's unsaved edit left with A's state");
        });

        var notices = cut.FindAll(UnsavedEditsDiscardedNoticeId);
        notices.Should().ContainSingle(
            "exactly one notice: the discarded work is announced once, on the surface that replaced it");
        notices[0].TextContent.Should().Be(UnsavedEditsDiscardedNoticeText,
            "the notice has to say what actually happened — the discard — not make a claim about the new " +
            "assignment");
    }

    /// <summary>UX-7 / F17 defence-in-depth (this round, criterion 3, the fail-safe half): a CLEAN form
    /// that swaps <c>Id</c> reloads without a notice — nothing was discarded, so an announcement would
    /// be a lie, and a page that cries wolf teaches authors to read past the one that is true.
    /// <para>Not on its own discriminating (both trees reload silently); it is the non-vacuity anchor for
    /// the notice above, and it pins that the reload itself is still unguarded by dirtiness.</para></summary>
    [TestMethod]
    public void CleanForm_IdChange_ReloadsWithoutTheDiscardNotice()
    {
        var dtoA = MakeDto(AssignmentStatusDto.Draft);
        var dtoB = dtoA with { Id = Guid.NewGuid(), Title = "Science HW" };

        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, dtoA);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW"));

        SetupAssignment(dtoB);
        cut.Render(p => p
            .Add(x => x.Mode, AssignmentAuthoringMode.Edit)
            .Add(x => x.Id, dtoB.Id));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Science HW"));
        cut.FindAll(UnsavedEditsDiscardedNoticeId).Should().BeEmpty(
            "an untouched form loses nothing, so the reload stays silent");
    }

    /// <summary>UX-7 / F17 defence-in-depth (this round, criterion 4): the in-app confirmation's
    /// DISCARD path records the consent, so the reload that navigation reaches — the already-guarded
    /// path — does not announce the author's own decision back at them.
    /// <para>Non-vacuity anchors: the guard really did ask (the confirmation was captured), the
    /// navigation really did proceed (the fake navigation committed), and the form really was dirty at
    /// the time (the same edit shape as the announced case above). The parameter set is then driven by
    /// hand because bUnit renders the component directly rather than through a <c>Router</c>.
    /// </para></summary>
    [TestMethod]
    public async Task UnsavedEdits_ConsentedDiscard_IdChange_ReloadsWithoutTheDiscardNotice()
    {
        var captured = new List<ConfirmDialogContent>();
        RegisterConfirmationDialog(confirm: true, captured);

        var dtoA = MakeDto(AssignmentStatusDto.Draft);
        var dtoB = dtoA with { Id = Guid.NewGuid(), Title = "Science HW" };

        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, dtoA);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW"));
        await SetTitleAsync(cut, "Edited title");

        Navigation.NavigateTo($"/assignments/{dtoB.Id}/edit");
        cut.WaitForAssertion(() => captured.Should().HaveCount(1,
            "the guard asked before the navigation could change the parameter"));
        Navigation.Uri.Should().EndWith($"/assignments/{dtoB.Id}/edit",
            "vacuity anchor: the discard was confirmed, so this navigation is the author's own decision");

        SetupAssignment(dtoB);
        cut.Render(p => p
            .Add(x => x.Mode, AssignmentAuthoringMode.Edit)
            .Add(x => x.Id, dtoB.Id));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Science HW"));
        cut.FindAll(UnsavedEditsDiscardedNoticeId).Should().BeEmpty(
            "D2: a consented discard is not announced back at the author — the notice is for the swap " +
            "nothing guarded");
    }

    /// <summary>UX-7 / F17 defence-in-depth (this round, the leak the consent flag could carry): the
    /// consent belongs to the navigation that produced it. A navigation the author consented to which
    /// never reached a parameter change (a same-route jump) is a real state on this page — the sticky
    /// jump-nav navigates within the route — so the flag is consumed on every parameter set and cannot
    /// be left armed to silence a LATER, genuinely unguarded swap. That later swap is the silent discard
    /// this round exists to remove.
    /// <para>Discriminating: with the flag consumed only on a changed parameter set, the last assertion
    /// fails — the stale consent suppresses a notice the author should have seen.</para></summary>
    [TestMethod]
    public async Task ConsentThatReachedNoParameterChange_DoesNotSilenceALaterUnguardedDiscard()
    {
        var captured = new List<ConfirmDialogContent>();
        RegisterConfirmationDialog(confirm: true, captured);

        var dtoA = MakeDto(AssignmentStatusDto.Draft);
        var dtoB = dtoA with { Id = Guid.NewGuid(), Title = "Science HW" };

        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, dtoA);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW"));
        await SetTitleAsync(cut, "Edited title");

        Navigation.NavigateTo($"/assignments/{dtoA.Id}/edit#authoring-questions");
        cut.WaitForAssertion(() => captured.Should().HaveCount(1,
            "vacuity anchor: the guard asked about the unsaved work before letting the jump through"));

        // The host re-renders with the SAME Id/Mode (the no-op parameter set), so the consented
        // navigation reached no change at all. It discarded nothing, so it must announce nothing.
        cut.Render(p => p
            .Add(x => x.Mode, AssignmentAuthoringMode.Edit)
            .Add(x => x.Id, dtoA.Id));
        cut.FindAll(UnsavedEditsDiscardedNoticeId).Should().BeEmpty(
            "the consented navigation changed nothing, so there was no discard to announce");

        // The real swap is still unguarded and still dirty: it must be announced.
        SetupAssignment(dtoB);
        cut.Render(p => p
            .Add(x => x.Mode, AssignmentAuthoringMode.Edit)
            .Add(x => x.Id, dtoB.Id));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Science HW"));
        cut.FindAll(UnsavedEditsDiscardedNoticeId).Should().ContainSingle(
            "the consent was spent on the navigation that reached no parameter change — it may not " +
            "silence the next unguarded swap");
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

    // ── R2 rework: the "Targets & audience" typed builder ─────────────────────

    private static readonly Guid BuilderGradeAId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid BuilderGradeBId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly Guid BuilderStreamId = Guid.Parse("bbbbbbbb-3333-3333-3333-333333333333");
    private static readonly Guid BuilderStudentId = Guid.Parse("cccccccc-4444-4444-4444-444444444444");

    /// <summary>One grade level the builder's grade picker offers.</summary>
    private static GradeLevelDto BuilderGrade(Guid id, int level, string name) =>
        new(id, Guid.NewGuid(), level, name, 0, 0, 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    /// <summary>Adds one entry by clicking the builder's Add action (the mocked dialog returns the next
    /// queued entry).</summary>
    private static Task AddTargetEntryAsync(IRenderedComponent<Authoring> cut) =>
        cut.InvokeAsync(() => OpenAddTargetDialog(cut));

    /// <summary>R2 (Q1): the Add dialog hands back a TYPED entry per category, and the builder renders
    /// each with its category label over the picked value's label.</summary>
    [TestMethod]
    public async Task Create_AddDialog_AddsATypedEntryOfEachCategoryToTheBuilder()
    {
        _gradeLevels = [BuilderGrade(BuilderGradeAId, 5, "Grade 5")];
        _activityGroups = [Group(GroupAId, "Alpha")];
        RegisterTargetsDialog()
            .Enqueue(new TargetsAndAudienceEntry(TargetsAndAudienceCategory.GradeLevels, BuilderGradeAId, "Grade 5"))
            .Enqueue(new TargetsAndAudienceEntry(TargetsAndAudienceCategory.Streams, BuilderStreamId, "Grade 5 · Blue"))
            .Enqueue(new TargetsAndAudienceEntry(TargetsAndAudienceCategory.Students, BuilderStudentId, "Ada Lovelace (S1001)"))
            .Enqueue(new TargetsAndAudienceEntry(TargetsAndAudienceCategory.ActivityGroups, GroupAId, "Alpha"));
        var previewReads = SetupRecipientPreviewRead();

        var cut = RenderAuthoring(AssignmentAuthoringMode.Create);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Basics"));
        WaitForLoadSettled(cut, previewReads);

        foreach (var expected in new[]
                 {
                     ("Grade Levels", "Grade 5"),
                     ("Streams", "Grade 5 · Blue"),
                     ("Students", "Ada Lovelace (S1001)"),
                     ("Activity Groups", "Alpha")
                 })
        {
            await AddTargetEntryAsync(cut);
            cut.WaitForAssertion(() => BuilderEntries(cut).Should().Contain(expected,
                "each typed category renders its category label over the picked value's label"));
        }

        BuilderEntries(cut).Should().HaveCount(4, "each Add appends exactly one entry");
    }

    /// <summary>Owner rework: the entity pickers are MULTI-select, so ONE dialog submission appends
    /// one entry per picked option — the whole selection rides the page's Add action.</summary>
    [TestMethod]
    public async Task Create_AddDialog_MultiSelect_AppendsOneEntryPerPickedOption()
    {
        _gradeLevels = [BuilderGrade(BuilderGradeAId, 5, "Grade 5"), BuilderGrade(BuilderGradeBId, 6, "Grade 6")];
        RegisterTargetsDialog().Enqueue(
            new TargetsAndAudienceEntry(TargetsAndAudienceCategory.GradeLevels, BuilderGradeAId, "Grade 5"),
            new TargetsAndAudienceEntry(TargetsAndAudienceCategory.GradeLevels, BuilderGradeBId, "Grade 6"));
        var previewReads = SetupRecipientPreviewRead();

        var cut = RenderAuthoring(AssignmentAuthoringMode.Create);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Basics"));
        WaitForLoadSettled(cut, previewReads);

        await AddTargetEntryAsync(cut);

        cut.WaitForAssertion(() => BuilderEntries(cut).Should().Equal(
            new[] { ("Grade Levels", "Grade 5"), ("Grade Levels", "Grade 6") },
            "one multi-select submission appends an entry for every picked option"));
    }

    /// <summary>R2 (TGT-2/Q6): an Everyone entry is mutually exclusive with every typed entry — adding
    /// it clears the entity entries, and adding an entity entry clears it.</summary>
    [TestMethod]
    public async Task Builder_EveryoneEntry_IsMutuallyExclusiveWithEntityEntries()
    {
        _gradeLevels = [BuilderGrade(BuilderGradeAId, 5, "Grade 5"), BuilderGrade(BuilderGradeBId, 6, "Grade 6")];
        RegisterTargetsDialog()
            .Enqueue(new TargetsAndAudienceEntry(TargetsAndAudienceCategory.GradeLevels, BuilderGradeAId, "Grade 5"))
            .Enqueue(new TargetsAndAudienceEntry(
                TargetsAndAudienceCategory.Everyone, null, TargetsAndAudienceEntry.EveryoneLabel))
            .Enqueue(new TargetsAndAudienceEntry(TargetsAndAudienceCategory.GradeLevels, BuilderGradeBId, "Grade 6"));
        var previewReads = SetupRecipientPreviewRead();

        var cut = RenderAuthoring(AssignmentAuthoringMode.Create);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Basics"));
        WaitForLoadSettled(cut, previewReads);

        await AddTargetEntryAsync(cut);
        cut.WaitForAssertion(() => BuilderEntries(cut).Should().Equal(new[] { ("Grade Levels", "Grade 5") }));

        await AddTargetEntryAsync(cut);
        cut.WaitForAssertion(() => BuilderEntries(cut).Should().Equal(
            new[] { ("Applies to All in Org", TargetsAndAudienceEntry.EveryoneLabel) },
            "adding Everyone clears the entity entries (TGT-2)"));

        await AddTargetEntryAsync(cut);
        cut.WaitForAssertion(() => BuilderEntries(cut).Should().Equal(
            new[] { ("Grade Levels", "Grade 6") },
            "adding an entity entry clears the Everyone entry"));
    }

    /// <summary>P1-1: the entry's remove action renders INSIDE the ItemTemplate (SectionCard ignores
    /// ItemActions for a templated card) and removes the entry that names it.</summary>
    [TestMethod]
    public async Task Builder_EntryRemoveAction_RemovesTheEntryItNames()
    {
        _gradeLevels = [BuilderGrade(BuilderGradeAId, 5, "Grade 5"), BuilderGrade(BuilderGradeBId, 6, "Grade 6")];
        RegisterTargetsDialog()
            .Enqueue(new TargetsAndAudienceEntry(TargetsAndAudienceCategory.GradeLevels, BuilderGradeAId, "Grade 5"))
            .Enqueue(new TargetsAndAudienceEntry(TargetsAndAudienceCategory.GradeLevels, BuilderGradeBId, "Grade 6"));
        var previewReads = SetupRecipientPreviewRead();

        var cut = RenderAuthoring(AssignmentAuthoringMode.Create);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Basics"));
        WaitForLoadSettled(cut, previewReads);
        await AddTargetEntryAsync(cut);
        await AddTargetEntryAsync(cut);
        cut.WaitForAssertion(() => BuilderEntries(cut).Should().HaveCount(2));

        cut.FindAll("#authoring-targets fluent-button[title='Remove']").Should().HaveCount(2,
            "P1-1: every entry exposes its own remove action");

        RemoveBuilderEntry(cut, 0);

        cut.WaitForAssertion(() => BuilderEntries(cut).Should().Equal(
            new[] { ("Grade Levels", "Grade 6") }, "the remove action removes the entry it names"));
    }

    /// <summary>AC-5: an edit surface rebuilds the builder from the persisted target rows, in the
    /// persisted DisplayOrder, labelled from the loaded option sources.</summary>
    [TestMethod]
    public void Edit_LoadsThePersistedTargetsIntoTheBuilder()
    {
        _gradeLevels = [BuilderGrade(BuilderGradeAId, 5, "Grade 5")];
        _activityGroups = [Group(GroupAId, "Alpha")];

        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(AssignmentStatusDto.Draft),
            MakeChildren(targets:
            [
                new AssignmentTargetDto(TargetKindDto.ActivityGroup, GroupAId, 0),
                new AssignmentTargetDto(TargetKindDto.GradeLevel, BuilderGradeAId, 1)
            ]));

        cut.WaitForAssertion(() => BuilderEntries(cut).Should().Equal(
            new[] { ("Activity Groups", "Alpha"), ("Grade Levels", "Grade 5") },
            "the persisted rows load in DisplayOrder with their resolved labels"));
    }

    /// <summary>Round <c>drop-primary-grade</c> (AC-6), re-pointed at the effective-policy seam by
    /// OD4: the page's policy resolution follows the DERIVED policy-scope grade — exactly one grade
    /// target ⇒ that grade's override; a SECOND grade target moves the derived grade to null and the
    /// tenant default is resolved. There is no picker to change.</summary>
    [TestMethod]
    public async Task GradeTargets_DriveTheEffectivePolicyResolution()
    {
        _gradeLevels = [BuilderGrade(BuilderGradeAId, 5, "Grade 5"), BuilderGrade(BuilderGradeBId, 6, "Grade 6")];
        RegisterTargetsDialog()
            .Enqueue(new TargetsAndAudienceEntry(TargetsAndAudienceCategory.GradeLevels, BuilderGradeAId, "Grade 5"))
            .Enqueue(new TargetsAndAudienceEntry(TargetsAndAudienceCategory.GradeLevels, BuilderGradeBId, "Grade 6"));
        var previewReads = SetupRecipientPreviewRead();
        // The grade's own policy: a signature requirement, a locked review and a 45-day window —
        // every one of them distinguishable from the built-in tenant-leg defaults below.
        SetupGradeEffectivePolicy(BuilderGradeAId, EffectivePolicyBody(
            SignatureRequirementMode.Optional, mandatoryReview: false, archiveGraceDays: 45));

        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(AssignmentStatusDto.Draft));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW"));
        WaitForLoadSettled(cut, previewReads);

        // The load resolves the policy for the LOADED row's derived grade (create/edit ⇒ none here:
        // the fixture carries no target), then each builder edit re-resolves when that grade moves.
        _effectivePolicyQueries.Should().ContainSingle();
        _effectivePolicyQueries[0].Should().NotContain("gradeLevelId=",
            "a row with no grade target derives the tenant-default leg");
        cut.Find("#authoring-policy-archive").TextContent
            .Should().Contain("Archived 30 days after the due date (built-in default)");

        await AddTargetEntryAsync(cut);

        cut.WaitForAssertion(() =>
        {
            _effectivePolicyQueries.Should().HaveCount(2);
            _effectivePolicyQueries[1].Should().Contain($"gradeLevelId={BuilderGradeAId}",
                "a single grade target derives that grade, so its policy override is what gets resolved");
            cut.FindAll(".authoring-policy-link").Should().NotBeEmpty(
                "the resolved policy links to the single derived grade's card");
            cut.Find("#authoring-policy-archive").TextContent
                .Should().Contain("Archived 45 days after the due date",
                    "the grade's policy replaced the tenant-leg readouts");
            cut.FindComponents<FluentCheckbox>()
                .Single(c => c.Instance.Id == "authoring-basics-mandatory-review").Instance.Disabled
                .Should().BeTrue("the grade's policy sets review, so the author's toggle locks (OD3)");
        }, TimeSpan.FromSeconds(5));

        await AddTargetEntryAsync(cut);

        cut.WaitForAssertion(() =>
        {
            _effectivePolicyQueries.Should().HaveCount(3);
            _effectivePolicyQueries[2].Should().NotContain("gradeLevelId=",
                "two grade targets derive NULL — the tenant-default policy (AC-5b/AC-6)");
            cut.FindAll(".authoring-policy-link").Should().BeEmpty(
                "no single grade owns the policy any more, so the link falls back to the grade-level list");
            cut.Find("#authoring-policy-archive").TextContent
                .Should().Contain("Archived 30 days after the due date (built-in default)");
        }, TimeSpan.FromSeconds(5));
    }

    /// <summary>D2/OD4: registers the effective-policy body a grade-scoped read returns.</summary>
    private void SetupGradeEffectivePolicy(Guid gradeLevelId, string body) =>
        _gradeEffectivePolicies[gradeLevelId] = body;

    /// <summary>AC-4: the save payload carries exactly the authored entries — one target row each — and
    /// the legacy compat audience is derived from them.</summary>
    [TestMethod]
    public async Task Create_Save_CarriesTheAuthoredTargetRows()
    {
        var newAssignmentId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var topicId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        _gradeLevels = [BuilderGrade(BuilderGradeAId, 5, "Grade 5")];
        _activityGroups = [Group(GroupAId, "Alpha")];
        SetupGroupSubjects(topicId);
        RegisterTargetsDialog()
            .Enqueue(new TargetsAndAudienceEntry(TargetsAndAudienceCategory.GradeLevels, BuilderGradeAId, "Grade 5"))
            .Enqueue(new TargetsAndAudienceEntry(TargetsAndAudienceCategory.ActivityGroups, GroupAId, "Alpha"));
        var previewReads = SetupRecipientPreviewRead();

        var cut = RenderAuthoring(AssignmentAuthoringMode.Create);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Basics"));
        WaitForLoadSettled(cut, previewReads);
        await SetTitleAsync(cut, "Targeted HW");
        await AddTargetEntryAsync(cut);
        await AddTargetEntryAsync(cut);
        cut.WaitForAssertion(() => BuilderEntries(cut).Should().HaveCount(2));
        await SelectAsync(cut, "authoring-basics-subject", topicId.ToString());

        string? createBody = null;
        _mockHttp.When(HttpMethod.Post, "http://localhost/assignments")
            .With(req =>
            {
                createBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return true;
            })
            .Respond(HttpStatusCode.OK, "application/json", $"\"{newAssignmentId}\"");
        CaptureRequest(HttpMethod.Put, $"http://localhost/assignments/{newAssignmentId}/groups", _ => { });

        cut.Find("#authoring-primary-action").Click();

        cut.WaitForAssertion(() => createBody.Should().NotBeNull(), TimeSpan.FromSeconds(5));
        createBody.Should().Contain(BuilderGradeAId.ToString()).And.Contain(GroupAId.ToString(),
            "each authored entry rides the create payload as a target row (TGT-1)");
        createBody.Should().Contain("\"targetAudienceType\":\"SelectedGrades\"",
            "the compat audience is DERIVED from the authored target rows (D-1)");
    }

    /// <summary>AC-5: a no-op save leaves the persisted targets alone — the untouched builder sends no
    /// target replacement (the update handler reads null as "preserve").</summary>
    [TestMethod]
    public async Task Edit_NoOpSave_DoesNotRewriteThePersistedTargets()
    {
        var dto = MakeDto(AssignmentStatusDto.Draft);
        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, dto,
            MakeChildren(targets: [new AssignmentTargetDto(TargetKindDto.Stream, BuilderStreamId, 0)]));
        cut.WaitForAssertion(() => BuilderEntries(cut).Should().HaveCount(1));

        string? updateBody = null;
        CaptureRequest(HttpMethod.Put, $"http://localhost/assignments/{dto.Id}", body => updateBody = body);

        SaveDraft(cut);

        cut.WaitForAssertion(() => updateBody.Should().NotBeNull(), TimeSpan.FromSeconds(5));
        updateBody.Should().NotContain(BuilderStreamId.ToString(),
            "an untouched builder sends no target replacement — the persisted rows survive");
    }

    /// <summary>AC-4/P2-1: removing a builder entry reduces the target rows the update carries, and the
    /// legacy link table follows the changed group set.</summary>
    [TestMethod]
    public async Task Edit_RemovingAnActivityGroupEntry_SendsTheReducedTargetSetAndLinks()
    {
        _activityGroups = [Group(GroupAId, "Alpha"), Group(GroupBId, "Beta")];
        var dto = MakeDto(AssignmentStatusDto.Draft, audience: TargetAudienceTypeDto.SelectedGroups);
        SetupGroupSubjects(dto.TopicId);

        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, dto,
            MakeChildren(targets:
            [
                new AssignmentTargetDto(TargetKindDto.ActivityGroup, GroupAId, 0),
                new AssignmentTargetDto(TargetKindDto.ActivityGroup, GroupBId, 1)
            ]),
            linkedGroupIds: [GroupAId, GroupBId]);
        cut.WaitForAssertion(() => BuilderEntries(cut).Should().HaveCount(2));

        RemoveBuilderEntry(cut, 1);
        cut.WaitForAssertion(() => BuilderEntries(cut).Should().HaveCount(1));

        // Removing a group target resets the FR-58 subject sources, so the author re-picks the topic.
        await SelectSubjectAsync(cut, dto.TopicId);

        string? updateBody = null;
        string? linkBody = null;
        CaptureRequest(HttpMethod.Put, $"http://localhost/assignments/{dto.Id}/groups", body => linkBody = body);
        CaptureRequest(HttpMethod.Put, $"http://localhost/assignments/{dto.Id}", body => updateBody = body);

        SaveDraft(cut);

        cut.WaitForAssertion(() => updateBody.Should().NotBeNull(), TimeSpan.FromSeconds(5));
        updateBody.Should().Contain(GroupAId.ToString()).And.NotContain(GroupBId.ToString(),
            "the update carries exactly the reduced target set");
        linkBody.Should().NotBeNull("the changed group set re-issues the replace-set link route");
        linkBody.Should().NotContain(GroupBId.ToString());
    }

    /// <summary>The fail-safe half of the group-link sync: a builder that still holds exactly the
    /// group target set it loaded never issues the replace-set route, whose payload deletes every
    /// link missing from it.</summary>
    [TestMethod]
    public async Task Edit_ReassertingTheSameGroups_DoesNotRewriteTheLinks()
    {
        _activityGroups = [Group(GroupAId, "Alpha")];
        var dto = MakeDto(AssignmentStatusDto.Draft, audience: TargetAudienceTypeDto.SelectedGroups);
        SetupGroupSubjects(dto.TopicId);

        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, dto,
            MakeChildren(targets: [new AssignmentTargetDto(TargetKindDto.ActivityGroup, GroupAId, 0)]),
            linkedGroupIds: [GroupAId]);
        cut.WaitForAssertion(() => BuilderEntries(cut).Should().Equal(new[] { ("Activity Groups", "Alpha") },
            "the persisted group target loads into the builder as the same target set"));

        string? updateBody = null;
        string? linkBody = null;
        CaptureRequest(HttpMethod.Put, $"http://localhost/assignments/{dto.Id}/groups", body => linkBody = body);
        CaptureRequest(HttpMethod.Put, $"http://localhost/assignments/{dto.Id}", body => updateBody = body);

        SaveDraft(cut);

        cut.WaitForAssertion(() => updateBody.Should().NotBeNull(), TimeSpan.FromSeconds(5));
        linkBody.Should().BeNull(
            "an unchanged group target set never issues the replace-set call — the route deletes every " +
            "link missing from its payload");
    }

    /// <summary>P1-3 (UX-21): an editable Edit surface whose persisted targets could not be read
    /// renders the builder DISABLED WITH REASON — the add action is inert and a save writes no target
    /// set at all.</summary>
    [TestMethod]
    public void Edit_TargetsLoadFails_RendersTheBuilderDisabledWithReason_AndBlocksTheSave()
    {
        var dto = MakeDto(AssignmentStatusDto.Draft);
        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, dto, childrenReadFails: true);

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("#authoring-targets-reason").Should().ContainSingle(
                "UX-21: the builder names the reason it is disabled")
                .Which.TextContent.Should().Contain(Authoring.ChildrenUnavailableReason);
            cut.Find("fluent-button[title='Add target']").HasAttribute("disabled").Should().BeTrue(
                "P1-3: no entry may be authored over an unknown persisted set");
        }, TimeSpan.FromSeconds(5));

        string? updateBody = null;
        CaptureRequest(HttpMethod.Put, $"http://localhost/assignments/{dto.Id}", body => updateBody = body);

        SaveDraft(cut);

        updateBody.Should().BeNull("P1-3: the save path refuses to write targets it could not read");

        // The refusal lands on the page's error surface. A kebab-invoked action's state change renders
        // on the NEXT pass (the event is declared by RowActionsMenu, not by this page), so the test
        // re-sets the same parameters — which renders without re-entering the load — before reading it.
        cut.Render(p => p
            .Add(x => x.Mode, AssignmentAuthoringMode.Edit)
            .Add(x => x.Id, dto.Id));

        cut.WaitForAssertion(() => cut.Find("#authoring-error").TextContent
            .Should().Contain(Authoring.ChildrenUnavailableReason,
                "P1-3: the refused save explains itself with the UX-21 reason"), TimeSpan.FromSeconds(5));
    }

    /// <summary>P2-1 rework, fail-closed: an unknown persisted link set blocks the save with the reason
    /// rather than writing a replace-set the author never saw.</summary>
    [TestMethod]
    public void Edit_LinkedGroupsReadFails_BlocksTheSaveWithReason()
    {
        _activityGroups = [Group(GroupAId, "Alpha")];
        var dto = MakeDto(AssignmentStatusDto.Draft, audience: TargetAudienceTypeDto.SelectedGroups);

        // The link read is stubbed to 500 for this dto only; every other route the Edit surface touches
        // is registered here (this test registers its own routes instead of calling SetupAssignment,
        // whose /groups route returns 200).
        _mockHttp.When(HttpMethod.Get, $"http://localhost/assignments/{dto.Id}/authoring")
            .Respond(HttpStatusCode.OK, "application/json", JsonSerializer.Serialize(
                MakeChildren(targets: [new AssignmentTargetDto(TargetKindDto.ActivityGroup, GroupAId, 0)]),
                _apiJsonOptions));
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

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW"));

        string? updateBody = null;
        CaptureRequest(HttpMethod.Put, $"http://localhost/assignments/{dto.Id}", body => updateBody = body);

        SaveDraft(cut);

        cut.WaitForAssertion(() =>
        {
            cut.Find("#authoring-error").TextContent.Should().Contain(Authoring.GroupLinksUnavailableReason,
                "the unknown link set explains why the save is refused");
            updateBody.Should().BeNull("a replace-set over an unread link set would delete the stored links");
        }, TimeSpan.FromSeconds(5));
    }

    /// <summary>TGT-13/FR-58: a group target with no subject selected cannot be saved — the gate
    /// refuses before any request is sent.</summary>
    [TestMethod]
    public async Task Create_GroupTargetWithoutASubject_BlocksTheSave()
    {
        _activityGroups = [Group(GroupAId, "Alpha")];
        SetupGroupSubjects(); // the group resolves NO subject
        RegisterTargetsDialog().Enqueue(
            new TargetsAndAudienceEntry(TargetsAndAudienceCategory.ActivityGroups, GroupAId, "Alpha"));
        var previewReads = SetupRecipientPreviewRead();

        var cut = RenderAuthoring(AssignmentAuthoringMode.Create);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Basics"));
        WaitForLoadSettled(cut, previewReads);

        await SetTitleAsync(cut, "Group-targeted HW");
        await AddTargetEntryAsync(cut);
        cut.WaitForAssertion(() => BuilderEntries(cut).Should().HaveCount(1));

        string? createBody = null;
        CaptureRequest(HttpMethod.Post, "http://localhost/assignments", body => createBody = body);

        cut.Find("#authoring-primary-action").Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Select a subject."), TimeSpan.FromSeconds(5));
        createBody.Should().BeNull("the gate blocks the save before any request is sent");
    }

    /// <summary>R2: with FEATURE:EnableActivityGroups off the builder offers no activity-group values
    /// (the category stays in the dialog; its picker is simply empty).</summary>
    [TestMethod]
    public async Task ActivityGroupsFlagOff_DoesNotOfferAGroupTargetValue()
    {
        _flags.ActivityGroupsEnabled = false;
        _activityGroups = [Group(GroupAId, "Alpha")];
        var stub = RegisterTargetsDialog();
        var previewReads = SetupRecipientPreviewRead();

        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(AssignmentStatusDto.Draft));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Math HW"));
        WaitForLoadSettled(cut, previewReads);

        await AddTargetEntryAsync(cut);

        cut.WaitForAssertion(() =>
        {
            stub.Models.Should().ContainSingle("the add dialog was opened");
            stub.Models[0].ActivityGroupOptions.Should().BeEmpty(
                "the group target must not be offered when FEATURE:EnableActivityGroups is off");
            cut.Markup.Should().NotContain("By Group", "the retired group picker stays gone");
        }, TimeSpan.FromSeconds(5));
    }

    /// <summary>TGT-16 (restored): the preview renders under the builder card and re-resolves from the
    /// CURRENT set, because the model's targets are synced on every add — not only at save.</summary>
    [TestMethod]
    public async Task Builder_Add_ReResolvesTheRecipientPreviewFromTheCurrentSet()
    {
        var previewRequests = 0;
        var previewQueries = new List<string>();
        _mockHttp.When(HttpMethod.Get, "http://localhost/assignments/recipient-preview*")
            .With(req =>
            {
                previewQueries.Add(req.RequestUri!.Query);
                return true;
            })
            .Respond(_ => PreviewResponse(++previewRequests == 1
                ? "{\"studentsMatched\":0,\"primaryContacts\":0,\"otherContacts\":0,\"previewDegraded\":false}"
                : "{\"studentsMatched\":7,\"primaryContacts\":5,\"otherContacts\":2,\"previewDegraded\":false}"));

        _gradeLevels = [BuilderGrade(BuilderGradeAId, 5, "Grade 5")];
        RegisterTargetsDialog().Enqueue(
            new TargetsAndAudienceEntry(TargetsAndAudienceCategory.GradeLevels, BuilderGradeAId, "Grade 5"));

        var clock = new FakeTimeProvider();
        var cut = RenderAuthoring(AssignmentAuthoringMode.Create, timeProvider: clock);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Basics"));
        await AdvanceUntilAsync(clock, () => previewRequests >= 1);

        await AddTargetEntryAsync(cut);
        cut.WaitForAssertion(() => BuilderEntries(cut).Should().HaveCount(1));
        previewRequests.Should().Be(1, "the trailing debounce is armed, not elapsed");

        clock.Advance(FakeTimeProvider.PreviewDebounce);

        cut.WaitForAssertion(() => cut.Find("#authoring-audience-preview").TextContent.Trim()
            .Should().Contain("Students matched: 7"), TimeSpan.FromSeconds(2));
        previewRequests.Should().Be(2, "one preview read per settled debounce");
        previewQueries[1].Should().Contain($"gradeLevelIds={BuilderGradeAId}",
            "P1-4: the model's targets are synced on the ADD, so the preview resolves the current set");
    }

    /// <summary>TGT-16/F10 (restored): a degraded read renders the unavailable reason under the builder
    /// and never the all-zero counts.</summary>
    [TestMethod]
    public async Task Builder_DegradedPreview_RendersTheUnavailableReason()
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
        var cut = RenderAuthoring(AssignmentAuthoringMode.Edit, MakeDto(AssignmentStatusDto.Draft),
            MakeChildren(targets: [new AssignmentTargetDto(TargetKindDto.Stream, BuilderStreamId, 0)]),
            timeProvider: clock);

        await AdvanceUntilAsync(clock, () => previewRequests >= 1);

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain(Authoring.PreviewUnavailableReason,
                "TGT-16: a degraded preview shows the inline advisory note");
            cut.Markup.Should().NotContain("Students matched",
                "F10: a degraded read answers all-zero counts — they are never rendered as trustworthy");
            cut.FindAll("#authoring-audience-preview").Should().BeEmpty(
                "F10: the reason replaces the counts line, it is never shown beside it");
            cut.FindAll("#authoring-audience-preview-note").Should().ContainSingle();
        }, TimeSpan.FromSeconds(2));
    }

    /// <summary>TGT-16/D-4: a burst of builder changes leaves exactly ONE trailing debounce alive, so
    /// the audience is read once per settle rather than once per change.</summary>
    [TestMethod]
    public async Task Builder_RapidChanges_CoalesceIntoASinglePreviewRead()
    {
        var previewRequests = 0;
        _mockHttp.When(HttpMethod.Get, "http://localhost/assignments/recipient-preview*")
            .Respond(_ =>
            {
                previewRequests++;
                return PreviewResponse(
                    "{\"studentsMatched\":7,\"primaryContacts\":5,\"otherContacts\":2,\"previewDegraded\":false}");
            });

        RegisterTargetsDialog()
            .Enqueue(new TargetsAndAudienceEntry(TargetsAndAudienceCategory.Students, BuilderStudentId, "Ada Lovelace (S1001)"))
            .Enqueue(new TargetsAndAudienceEntry(
                TargetsAndAudienceCategory.Students, Guid.Parse("dddddddd-5555-5555-5555-555555555555"), "Bob Stone (S1002)"))
            .Enqueue(new TargetsAndAudienceEntry(
                TargetsAndAudienceCategory.Everyone, null, TargetsAndAudienceEntry.EveryoneLabel));

        var clock = new FakeTimeProvider();
        var cut = RenderAuthoring(AssignmentAuthoringMode.Create, timeProvider: clock);
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Basics"));
        await AdvanceUntilAsync(clock, () => previewRequests >= 1);
        previewRequests.Should().Be(1);

        // Three builder changes in quick succession. Each one re-schedules the trailing debounce and
        // cancels its predecessor.
        var burst = new[]
        {
            AddTargetEntryAsync(cut),
            AddTargetEntryAsync(cut),
            AddTargetEntryAsync(cut)
        };
        await Task.WhenAll(burst).WaitAsync(TimeSpan.FromSeconds(5));

        cut.WaitForAssertion(() => clock.PendingTimers.Should().Be(1,
            "the burst left exactly ONE trailing debounce armed"), TimeSpan.FromSeconds(5));
        previewRequests.Should().Be(1, "a held debounce reads nothing before the clock is advanced");

        clock.Advance(FakeTimeProvider.PreviewDebounce);

        cut.WaitForAssertion(() => previewRequests.Should().Be(2), TimeSpan.FromSeconds(2));
        clock.PendingTimers.Should().Be(0, "nothing is left armed to fire a second read");
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
