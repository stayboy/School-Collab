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
using CreatePage = SchoolCollab.Assignments.Application.Components.Pages.Assignments.Create;
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
/// R1 (round <c>round-assignment-authoring-r1</c>, supervisor-approved port) — the create
/// surface is now the shared <see cref="AssignmentAuthoring"/> component in Create mode
/// (documents/specs/assignment-authoring-compartments.md D1/UX-3 retires the FluentWizard).
///
/// <para>The retired suite drove the wizard through reflection on Create's private members
/// (<c>_requiresSignature</c>, <c>_selectedGradeLevel</c>, <c>_selectedSubject</c>, nested
/// option records) and indexed <c>FindComponents&lt;FluentCheckbox&gt;()[1]</c>. Every
/// behavioural assertion it protected is ported here onto the shared component's real
/// surface: the public pickers' <c>SelectedOptionChanged</c> callback, the stable control
/// ids, and the action-bar primary button.</para>
/// </summary>
[TestClass]
public class AssignmentCreateBunitTests : BunitContext
{
    private readonly MockHttpMessageHandler _mockHttp;
    private readonly JsonSerializerOptions _apiJsonOptions;
    private readonly List<string> _createLogs = [];
    private readonly StubFlagService _flags = new();

    /// <summary>D2/OD4: the resolved effective-policy body the page's
    /// <c>/assignments/effective-policy</c> read returns for the tenant-default leg. Mutable so a
    /// test can switch the resolved policy before rendering.</summary>
    private string _effectivePolicyBody = EffectivePolicyBody();

    /// <summary>The effective-policy body a GRADE-scoped read returns (the ctor's matcher dispatches
    /// on the query string, which MockHttp's path matchers ignore).</summary>
    private readonly Dictionary<Guid, string> _gradeEffectivePolicies = [];

    /// <summary>Set before rendering: answers the effective-policy read with 500 so the page's
    /// fail-open path is exercised (the pre-fix transport-failure case).</summary>
    private bool _effectivePolicyUnreachable;

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

    private sealed class CaptureLogger<T> : ILogger<T>
    {
        private readonly List<string> _logs;
        public CaptureLogger(List<string> logs) => _logs = logs;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _logs.Add($"[{logLevel}] {formatter(state, exception)}");
        }
    }

    /// <summary>
    /// Value handed to the registered <see cref="StubFlagService"/>. Set to false (before
    /// rendering) to reproduce the default dark-launched state, in which
    /// <c>/activity-groups</c> is not mapped at all.
    /// </summary>
    protected bool ActivityGroupsEnabled
    {
        get => _flags.ActivityGroupsEnabled;
        set => _flags.ActivityGroupsEnabled = value;
    }

    public AssignmentCreateBunitTests()
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
            }
        };

        _mockHttp = new MockHttpMessageHandler();
        var httpClient = _mockHttp.ToHttpClient();
        httpClient.BaseAddress = new Uri("http://localhost");

        // WS-B2 (step 6): the authoring page loads the org AI-prompt lock on init.
        _mockHttp.When(HttpMethod.Get, "http://localhost/assignments/ai-prompt-policy")
            .Respond(HttpStatusCode.OK, "application/json", "{\"aiPromptLocked\":false}");
        // D2/OD4: the page's ONE policy read — the whole effective policy (the Rules readouts and
        // the guardian-review lock). Dispatches on the query string so a grade-scoped body can be
        // substituted without fighting MockHttp's registration order.
        _mockHttp.When(HttpMethod.Get, "http://localhost/assignments/effective-policy")
            .Respond(request =>
            {
                if (_effectivePolicyUnreachable)
                {
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                }

                var query = request.RequestUri!.Query;
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
        // R2 (TGT-5): the stream picker sources its options from the grade-stream listing.
        _mockHttp.When(HttpMethod.Get, "http://localhost/students/grade-levels/*/streams")
            .Respond(HttpStatusCode.OK, "application/json", "[]");
        // R2 (TGT-16): the live recipient preview — advisory, always 200.
        _mockHttp.When(HttpMethod.Get, "http://localhost/assignments/recipient-preview*")
            .Respond(HttpStatusCode.OK, "application/json",
                "{\"studentsMatched\":7,\"primaryContacts\":5,\"otherContacts\":2,\"previewDegraded\":false}");

        Services.AddSingleton(httpClient);
        Services.AddSingleton<AssignmentsApiClient>();
        Services.AddSingleton<StudentsApiClient>();
        // CodedValuesApiClient is required by StudentsApiClient's ctor.
        Services.AddSingleton<SchoolCollab.Admin.Shared.Services.CodedValuesApiClient>();
        Services.AddSingleton(Mock.Of<ILogger<AssignmentsApiClient>>());
        Services.AddSingleton(Mock.Of<ILogger<StudentsApiClient>>());
        Services.AddSingleton(Mock.Of<ILogger<CreatePage>>());
        Services.AddSingleton<ILogger<Authoring>>(new CaptureLogger<Authoring>(_createLogs));
        // The Questions & AI compartment injects the generation seams; these tests never
        // click Generate, so bare mocks are the right shape.
        Services.AddSingleton(Mock.Of<IAssignmentQuestionGenerator>());
        Services.AddSingleton(Mock.Of<IUrlTextExtractor>());
        Services.AddSingleton<IFeatureFlagService>(_flags);
    }

    /// <summary>
    /// Minimal <see cref="IFeatureFlagService"/> whose state is read live from the owning
    /// test, so a test can flip the flag before rendering.
    /// </summary>
    private sealed class StubFlagService : IFeatureFlagService
    {
        public bool ActivityGroupsEnabled { get; set; } = true;
        public bool IsEnabled(string featureKey) => ActivityGroupsEnabled;
        public Task<bool> IsEnabledAsync(string featureKey, CancellationToken ct = default) => Task.FromResult(ActivityGroupsEnabled);
        public IDictionary<string, bool> GetAllFlags() => new Dictionary<string, bool>();
        public Task<IReadOnlyDictionary<string, bool>> GetAllFlagsAsync(Guid? tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, bool>>(new Dictionary<string, bool>());
    }

    private void SetupGradeLevels(params GradeLevelDto[] grades)
    {
        _mockHttp.When(HttpMethod.Get, "http://localhost/students/grade-levels")
            .Respond(HttpStatusCode.OK, "application/json", JsonSerializer.Serialize(grades, _apiJsonOptions));
    }

    private void SetupActivityGroups(params ActivityGroupDto[] groups)
    {
        _mockHttp.When(HttpMethod.Get, "http://localhost/activity-groups*")
            .Respond(HttpStatusCode.OK, "application/json", JsonSerializer.Serialize(groups, _apiJsonOptions));
    }

    private void SetupSubjects(Guid gradeLevelId)
    {
        _mockHttp.When(HttpMethod.Get, $"http://localhost/students/subjects/by-grade/{gradeLevelId}*")
            .Respond(HttpStatusCode.OK, "application/json",
                $"[{{\"id\":\"{TopicId}\",\"name\":\"Mathematics\",\"displayOrder\":0}}]");
    }

    /// <summary>
    /// Stubs the tenant-default effective-policy body (round <c>assignment-rules-policy-rework</c>
    /// D2/OD4; the <c>/signature-default</c> boolean body is retired with the route).
    /// </summary>
    private void SetupEffectivePolicy(SignatureRequirementMode signature = SignatureRequirementMode.Disabled,
        bool? mandatoryReview = null, int? archiveGraceDays = null) =>
        _effectivePolicyBody = EffectivePolicyBody(signature, mandatoryReview, archiveGraceDays);

    /// <summary>Stubs the grade-scoped effective-policy body — the grade's own resolved policy.</summary>
    private void SetupGradeEffectivePolicy(Guid gradeLevelId,
        SignatureRequirementMode signature = SignatureRequirementMode.Disabled,
        bool? mandatoryReview = null, int? archiveGraceDays = null) =>
        _gradeEffectivePolicies[gradeLevelId] = EffectivePolicyBody(signature, mandatoryReview, archiveGraceDays);

    /// <summary>Round <c>drop-primary-grade</c>: the assignment has no primary-grade picker — its grade
    /// scope IS its grade TARGET rows — so a create surface's subject list is fed by the grade target the
    /// author adds through the builder. Mocks the Add dialog to return ONE grade target.</summary>
    private void RegisterGradeTargetDialog(Guid gradeLevelId, string label)
    {
        var dialogRef = new Mock<IDialogReference>();
        dialogRef.SetupGet(r => r.Result).Returns(Task.FromResult(
            DialogResult.Ok<object?>(new DialogShellResult<TargetsAndAudienceDialogResult>(
                new TargetsAndAudienceDialogResult(
                    [new TargetsAndAudienceEntry(TargetsAndAudienceCategory.GradeLevels, gradeLevelId, label)])))));
        var dialogMock = new Mock<IDialogService>();
        dialogMock
            .Setup(d => d.ShowDialogAsync<TargetsAndAudienceDialog, DialogShellData<TargetsAndAudienceDialogModel>>(
                It.IsAny<DialogShellData<TargetsAndAudienceDialogModel>>(), It.IsAny<DialogParameters>()))
            .ReturnsAsync(dialogRef.Object);

        Services.AddSingleton(dialogMock.Object);
    }

    private static Guid TopicId { get; } = Guid.Parse("00000000-0000-0000-0000-0000000000cc");

    private static Guid GradeFiveId { get; } = Guid.Parse("00000000-0000-0000-0000-000000000005");

    private static GradeLevelDto GradeFive =>
        new(GradeFiveId, Guid.NewGuid(), 5, "Grade 5", 5, 1, 0, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    /// <summary>The author's guardian-review toggle (OD3: it lives in Basics and is editable only
    /// while the effective policy leaves review unset), found by its stable id.</summary>
    private static FluentCheckbox ReviewToggle(IRenderedComponent<CreatePage> cut) =>
        cut.FindComponents<FluentCheckbox>()
            .Single(c => c.Instance.Id == "authoring-basics-mandatory-review").Instance;

    private static FluentSelect<Authoring.PickerOption> Picker(IRenderedComponent<CreatePage> cut, string id) =>
        cut.FindComponents<FluentSelect<Authoring.PickerOption>>().Single(s => s.Instance.Id == id).Instance;

    /// <summary>Renders the create page and waits for the builder's Add action to be live — the create
    /// surface's load marker now that there is no grade picker to wait on (round
    /// <c>drop-primary-grade</c>: Create-mode <c>_loading</c> is false from the start, so a rendered form is
    /// not evidence the load finished; the Add button is rendered only by the loaded surface).
    /// </summary>
    private IRenderedComponent<CreatePage> RenderCreatePage()
    {
        var cut = Render<CreatePage>();
        cut.WaitForAssertion(() =>
            cut.FindAll("#authoring-targets fluent-button[title='Add target']").Should().NotBeEmpty(
                "the create surface must be loaded and live before the first authoring step"),
            TimeSpan.FromSeconds(5));
        return cut;
    }

    /// <summary>Drives a picker through its real <c>SelectedOptionChanged</c> callback (the
    /// AssignmentPolicyFieldEditDialogTests pattern) — never a raw DOM event. Each pick is
    /// followed by a wait-for-assertion so the bound state is observably applied before the next
    /// interaction; this closes the FR-58 subject-reload race that otherwise lets a late cascade
    /// clobber a just-set value.</summary>
    private static async Task SelectAsync(IRenderedComponent<CreatePage> cut, string pickerId, Guid value, string label)
    {
        var option = new Authoring.PickerOption(value.ToString(), label);
        await cut.InvokeAsync(() => Picker(cut, pickerId).SelectedOptionChanged.InvokeAsync(option));

        cut.WaitForAssertion(() =>
        {
            var picker = Picker(cut, pickerId);
            picker.SelectedOption.Should().NotBeNull($"picker {pickerId} should have a selected option after picking {label}");
            picker.SelectedOption!.Value.Should().Be(value.ToString(),
                $"picker {pickerId} should reflect the picked value {value}");
        }, TimeSpan.FromSeconds(5));
    }

    /// <summary>Adds ONE grade-level target through the builder's Add dialog (the mocked dialog returns
    /// it) and waits for the FR-58 subject union that target feeds to land — round
    /// <c>drop-primary-grade</c>: the grade TARGET is the create surface's grade scope, and it is what the
    /// subject picker's options (and the derived signature default) follow.</summary>
    private static async Task AddGradeTargetAsync(IRenderedComponent<CreatePage> cut, string label)
    {
        await cut.InvokeAsync(() => cut.Find("fluent-button[title='Add target']").Click());

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("#authoring-targets .targets-audience-item__value").Should()
                .ContainSingle(e => e.TextContent.Trim() == label,
                    "the grade target should be the authored set");
            Picker(cut, "authoring-basics-subject").Items.Should().NotBeNullOrEmpty(
                "the grade target's effective subject list should have landed");
        }, TimeSpan.FromSeconds(5));
    }
    /// <summary>Fills the Basics title through the bound text field's own callback — the create
    /// guards require a non-empty title before anything is posted.</summary>
    private static async Task SetTitleAsync(IRenderedComponent<CreatePage> cut, string title)
    {
        await cut.InvokeAsync(() => cut.FindComponents<FluentTextField>()
            .Single(f => f.Instance.Id == "authoring-basics-title").Instance.ValueChanged.InvokeAsync(title));

        cut.WaitForAssertion(() =>
        {
            var field = cut.FindComponents<FluentTextField>()
                .Single(f => f.Instance.Id == "authoring-basics-title").Instance;
            field.Value.Should().Be(title, "the title field should reflect the typed value");
        }, TimeSpan.FromSeconds(5));
    }

    /// <summary>Sets the guardian-signature checkbox and waits until the bound value is reflected,
    /// closing the same late-cascade window as the other settled helpers.</summary>
    private static async Task SetGuardianReviewAsync(IRenderedComponent<CreatePage> cut, bool value)
    {
        await cut.InvokeAsync(() => ReviewToggle(cut).ValueChanged.InvokeAsync(value));

        cut.WaitForAssertion(() =>
        {
            ReviewToggle(cut).Value.Should().Be(value,
                $"the guardian-review toggle should reflect the author choice {value}");
        }, TimeSpan.FromSeconds(5));
    }

    /// <summary>Permanent diagnostic wrapper mandated by the flake-fix skill: if the primary action
    /// never posts, dump the page's guard state so a recurrence names the bailed guard instead of
    /// a bare <c>NotBeNull</c>.</summary>
    private static void AssertCapturedBodyNotNull(string? capturedBody, IRenderedComponent<CreatePage> cut, List<string> createLogs)
    {
        if (capturedBody is not null) return;

        var authoring = cut.FindComponents<Authoring>().FirstOrDefault()?.Instance;
        var error = ReadPrivateField<string?>(authoring, "_error") ?? "(none)";
        var subject = ReadPrivateField<Authoring.PickerOption?>(authoring, "_selectedSubject");
        var subjectText = subject is not null ? $"{subject.Label} ({subject.Value})" : "(none)";

        Assert.Fail(
            "Expected capturedBody not to be <null> because the primary action saves the draft, " +
            $"but the guard bailed. Page error: '{error}'; selected subject: {subjectText}; " +
            $"create logs: [{string.Join("; ", createLogs)}].");
    }

    private static T? ReadPrivateField<T>(object? instance, string fieldName)
    {
        if (instance is null) return default;
        var field = instance.GetType().GetField(fieldName,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return field is null ? default : (T?)field.GetValue(instance);
    }

    /// <summary>The OD3 policy-set review lock copy, bound once so the assertion and the component
    /// cannot drift silently (the <c>QuestionGenerationGate.DisabledTooltip</c> precedent).</summary>
    private const string PolicyReviewLockedReason = Authoring.PolicyReviewLockedReason;

    // ── QuestionGenerationGate (pure, no render) ────────────────────────────

    [TestMethod]
    public void QuestionGenerationGate_IsEnabled_TruthTable_FR220()
    {
        // Enabled: Digital/SemiManual + AutoGraded/InstantGraded
        QuestionGenerationGate.IsEnabled(AssignmentTypeDto.Digital, GradingFormatDto.AutoGraded).Should().BeTrue();
        QuestionGenerationGate.IsEnabled(AssignmentTypeDto.Digital, GradingFormatDto.InstantGraded).Should().BeTrue();
        QuestionGenerationGate.IsEnabled(AssignmentTypeDto.SemiManual, GradingFormatDto.AutoGraded).Should().BeTrue();
        QuestionGenerationGate.IsEnabled(AssignmentTypeDto.SemiManual, GradingFormatDto.InstantGraded).Should().BeTrue();

        // Manual any: never enabled
        QuestionGenerationGate.IsEnabled(AssignmentTypeDto.Manual, GradingFormatDto.AutoGraded).Should().BeFalse();
        QuestionGenerationGate.IsEnabled(AssignmentTypeDto.Manual, GradingFormatDto.InstantGraded).Should().BeFalse();
        QuestionGenerationGate.IsEnabled(AssignmentTypeDto.Manual, GradingFormatDto.TeacherGraded).Should().BeFalse();

        // Any type with TeacherGraded: never enabled
        QuestionGenerationGate.IsEnabled(AssignmentTypeDto.Digital, GradingFormatDto.TeacherGraded).Should().BeFalse();
        QuestionGenerationGate.IsEnabled(AssignmentTypeDto.SemiManual, GradingFormatDto.TeacherGraded).Should().BeFalse();

        // Null assignment type: never enabled
        QuestionGenerationGate.IsEnabled(null, GradingFormatDto.AutoGraded).Should().BeFalse();
    }

    [TestMethod]
    public void QuestionGenerationGate_HintText_TracksGate()
    {
        QuestionGenerationGate.HintText(AssignmentTypeDto.Digital, GradingFormatDto.AutoGraded)
            .Should().Be(QuestionGenerationGate.EnabledHint);
        QuestionGenerationGate.HintText(AssignmentTypeDto.Manual, GradingFormatDto.TeacherGraded)
            .Should().Be(QuestionGenerationGate.DisabledHint);
    }

    [TestMethod]
    public void QuestionGenerationGate_DisabledTooltip_HasStableCopy()
    {
        QuestionGenerationGate.DisabledTooltip.Should().Contain("Auto Scored");
    }

    [TestMethod]
    public void Create_DefaultMarkup_ShowsDisabledHint_UnderManualTeacherGraded()
    {
        SetupGradeLevels();
        SetupActivityGroups();
        SetupEffectivePolicy();

        var cut = Render<CreatePage>();

        // Default state: Manual + TeacherGraded → gate closed → the compartment 1 hint carries
        // the DisabledHint copy.
        cut.WaitForAssertion(() =>
            cut.Markup.Should().Contain(QuestionGenerationGate.DisabledHint),
            TimeSpan.FromSeconds(5));
    }

    // ── Ported behavioural assertions ───────────────────────────────────────

    /// <summary>
    /// Regression: <c>/activity-groups</c> is only mapped when
    /// <c>FEATURE:EnableActivityGroups</c> is on. With the flag off the page must load grade
    /// levels normally and must not offer the group target (and, in particular, must not
    /// produce a load-failure log for the dark-launched backend).
    /// </summary>
    [TestMethod]
    public void Create_ActivityGroupsFlagOff_LoadsGradeLevelsAndHidesSelectedGroups()
    {
        ActivityGroupsEnabled = false;

        SetupGradeLevels(GradeFive);
        SetupActivityGroups(); // mapped, but must never be requested
        SetupEffectivePolicy(archiveGraceDays: 45);

        var cut = Render<CreatePage>();

        cut.WaitForAssertion(() =>
            cut.Markup.Should().NotContain("By Group",
                "the group target must not be offered when the feature is off"),
            TimeSpan.FromSeconds(5));

        _createLogs.Should().NotContain(l => l.Contains("Failed to load", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Create_ActivityGroupsFlagOff_DefaultMarkupStillRenders()
    {
        ActivityGroupsEnabled = false;

        SetupGradeLevels(GradeFive);
        SetupActivityGroups();
        SetupEffectivePolicy();

        var cut = Render<CreatePage>();

        cut.WaitForAssertion(() =>
            cut.Markup.Should().Contain("Require guardian review before student submits"),
            TimeSpan.FromSeconds(5));

        cut.FindComponents<FluentProgressRing>().Should().BeEmpty(
            "the page must not be left spinning by the absent optional fetch");
    }

    /// <summary>D10/AC10: the author-facing guardian-signature checkbox is retired — the signature
    /// requirement is policy-decided and the Rules readout states the outcome.</summary>
    [TestMethod]
    public void Create_GuardianSignatureCheckbox_IsRetired_AndThePolicyRowStatesTheOutcome()
    {
        SetupGradeLevels(GradeFive);
        SetupActivityGroups();
        SetupEffectivePolicy(SignatureRequirementMode.Mandatory);

        var cut = Render<CreatePage>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().NotContain("authoring-submission-requires-signature");
            cut.Markup.Should().NotContain("Require guardian signature after completion");
            cut.Find("#authoring-policy-signature").TextContent
                .Should().Contain("A guardian signature is required after completion",
                    "D10: the readonly policy row states the outcome the assignment will be saved with");
        }, TimeSpan.FromSeconds(5));
    }

    /// <summary>OD3/AC10: the author's guardian-review toggle lives in Basics and is editable exactly
    /// while the effective policy leaves review unset.</summary>
    [TestMethod]
    public void Create_UnsetReviewPolicy_LeavesTheGuardianReviewToggleFree()
    {
        SetupGradeLevels(GradeFive);
        SetupActivityGroups();
        SetupEffectivePolicy(SignatureRequirementMode.Disabled, mandatoryReview: null);

        var cut = Render<CreatePage>();

        cut.WaitForAssertion(() =>
        {
            ReviewToggle(cut).Disabled.Should().BeFalse(
                "an unset policy leaves the guardian-review choice to the author");
            cut.FindAll("#authoring-basics #authoring-basics-mandatory-review").Should().ContainSingle(
                "OD3: the toggle lives in Basics, never inside the readouts-only Rules section");
            cut.Find("#authoring-policy-review").TextContent
                .Should().Contain("Not set by the policy");
        }, TimeSpan.FromSeconds(5));
    }

    /// <summary>OD3/OD5: an <c>Optional</c> signature requirement implies guardian review, so the
    /// policy locks the toggle and the readout states both outcomes.</summary>
    [TestMethod]
    public void Create_OptionalSignaturePolicy_ImpliesAndLocksGuardianReview()
    {
        SetupGradeLevels(GradeFive);
        SetupActivityGroups();
        SetupEffectivePolicy(SignatureRequirementMode.Optional);

        var cut = Render<CreatePage>();

        cut.WaitForAssertion(() =>
        {
            ReviewToggle(cut).Value.Should().BeTrue("OD5/D4: an Optional signature requirement requires review");
            ReviewToggle(cut).Disabled.Should().BeTrue("a policy-set review value is not author-editable");
            cut.Markup.Should().Contain(PolicyReviewLockedReason,
                "the locked toggle explains why it cannot be changed");
            cut.Find("#authoring-policy-signature").TextContent
                .Should().Contain("A guardian signature is required after completion");
        }, TimeSpan.FromSeconds(5));
    }

    /// <summary>AC6/OD4 (round <c>drop-primary-grade</c>): the grade-scoped effective policy wins over
    /// the tenant default as soon as a grade TARGET makes that grade the DERIVED policy-scope grade.</summary>
    [TestMethod]
    public async Task Create_GradeTargetAdded_ReResolvesTheEffectivePolicyForTheDerivedGrade()
    {
        SetupGradeLevels(GradeFive);
        SetupActivityGroups();
        SetupGradeEffectivePolicy(GradeFiveId, archiveGraceDays: 45);
        SetupEffectivePolicy(); // the init no-grade resolve: the built-in defaults
        SetupSubjects(GradeFiveId);
        RegisterGradeTargetDialog(GradeFiveId, GradeFive.Name);

        var cut = RenderCreatePage();
        cut.WaitForAssertion(() => cut.Find("#authoring-policy-archive").TextContent
            .Should().Contain("Archived 30 days after the due date (built-in default)"));

        await AddGradeTargetAsync(cut, GradeFive.Name);

        cut.WaitForAssertion(() =>
        {
            cut.Find("#authoring-policy-archive").TextContent
                .Should().Contain("Archived 45 days after the due date",
                    "the derived grade's policy replaces the tenant-leg readouts");
        }, TimeSpan.FromSeconds(5));
    }

    /// <summary>AC5/D6/D10: the create request carries no <c>archiveGraceDays</c> and no
    /// <c>requiresSignature</c> any more — both are snapshotted server-side from the resolved policy;
    /// only the author half of guardian review still rides the wire (OD1).</summary>
    [TestMethod]
    public async Task Create_MandatoryPolicy_SubmitsWithoutTheRetiredAuthorInputs()
    {
        SetupGradeLevels(GradeFive);
        SetupActivityGroups();
        SetupEffectivePolicy(SignatureRequirementMode.Mandatory);
        SetupSubjects(GradeFiveId);
        RegisterGradeTargetDialog(GradeFiveId, GradeFive.Name);

        var cut = RenderCreatePage();

        await AddGradeTargetAsync(cut, GradeFive.Name);

        cut.WaitForAssertion(() =>
        {
            ReviewToggle(cut).Value.Should().BeTrue("D4/OD5: the signature requirement implies review");
            ReviewToggle(cut).Disabled.Should().BeTrue("the policy-set value is locked");
        }, TimeSpan.FromSeconds(5));

        string? capturedBody = null;
        _mockHttp.Expect(HttpMethod.Post, "http://localhost/assignments")
            .With(req =>
            {
                capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return true;
            })
            .Respond(HttpStatusCode.OK, "application/json", "\"11111111-1111-1111-1111-111111111111\"");

        await SetTitleAsync(cut, "Algebra HW");
        await SelectAsync(cut, "authoring-basics-subject", TopicId, "Mathematics");
        cut.Find("#authoring-primary-action").Click();

        cut.WaitForAssertion(() => AssertCapturedBodyNotNull(capturedBody, cut, _createLogs),
            TimeSpan.FromSeconds(5));
        capturedBody.Should().Contain("\"mandatoryReview\":true",
            "the author half still rides the wire (OD1) and the policy wins server-side");
        capturedBody.Should().NotContain("requiresSignature",
            "D10: the retired author signature input left the request contract");
        capturedBody.Should().NotContain("archiveGraceDays",
            "D6: the archive grace window is no longer an author input");
    }

    /// <summary>OD1: when the policy leaves review unset the author's toggle IS what the create
    /// request carries — the policy still wins server-side whenever it sets a value.</summary>
    [TestMethod]
    public async Task Create_UnsetReviewPolicy_SubmitsTheAuthorsReviewChoice()
    {
        SetupGradeLevels(GradeFive);
        SetupActivityGroups();
        SetupEffectivePolicy(SignatureRequirementMode.Disabled, mandatoryReview: null);
        SetupSubjects(GradeFiveId);
        RegisterGradeTargetDialog(GradeFiveId, GradeFive.Name);

        var cut = RenderCreatePage();

        await AddGradeTargetAsync(cut, GradeFive.Name);

        // The author turns the default-true review off; the policy sets nothing, so this stands.
        await SetGuardianReviewAsync(cut, false);

        string? capturedBody = null;
        _mockHttp.Expect(HttpMethod.Post, "http://localhost/assignments")
            .With(req =>
            {
                capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return true;
            })
            .Respond(HttpStatusCode.OK, "application/json", "\"11111111-1111-1111-1111-111111111111\"");

        await SetTitleAsync(cut, "Algebra HW");
        await SelectAsync(cut, "authoring-basics-subject", TopicId, "Mathematics");
        cut.Find("#authoring-primary-action").Click();

        cut.WaitForAssertion(() => AssertCapturedBodyNotNull(capturedBody, cut, _createLogs),
            TimeSpan.FromSeconds(5));
        capturedBody.Should().Contain("\"mandatoryReview\":false",
            "the author's choice is submitted when the policy leaves review unset (OD1)");
    }

    /// <summary>OD2/OD4 (fail-open): a failed effective-policy fetch leaves every readout unresolved,
    /// keeps the author's review choice free, and renders no error bar.</summary>
    [TestMethod]
    public void Create_EffectivePolicyFetchFails_ReadoutsUnresolvedAndToggleStaysFree()
    {
        SetupGradeLevels(GradeFive);
        SetupActivityGroups();
        _effectivePolicyUnreachable = true;

        var cut = Render<CreatePage>();

        cut.WaitForAssertion(() =>
        {
            cut.Find("#authoring-policy-signature").TextContent.Should().Contain("Not resolved yet");
            ReviewToggle(cut).Disabled.Should().BeFalse(
                "fail-open: a transport failure must never lock the author out of their own choice");
            cut.FindComponents<FluentMessageBar>()
                .Should().NotContain(mb => mb.Instance.Intent == MessageIntent.Error,
                    "the fail-open policy read does not render an error message bar");
        }, TimeSpan.FromSeconds(5));
    }
}
