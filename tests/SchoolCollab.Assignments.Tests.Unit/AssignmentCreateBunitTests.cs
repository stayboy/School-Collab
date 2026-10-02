using System.Net;
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
    /// Stubs the tenant-level <c>/assignments/signature-default</c> body (Round B1 / D2
    /// widening: <c>{ requiresSignature, signatureMode }</c>, where the boolean keeps its
    /// pre-widening derivation <c>mode != Disabled</c>).
    /// </summary>
    private void SetupSignatureDefault(SignatureRequirementMode mode)
    {
        var requiresSignature = mode != SignatureRequirementMode.Disabled ? "true" : "false";
        _mockHttp.When(HttpMethod.Get, "http://localhost/assignments/signature-default")
            .Respond(HttpStatusCode.OK, "application/json",
                $"{{\"requiresSignature\":{requiresSignature},\"signatureMode\":\"{mode}\"}}");
    }

    /// <summary>Stubs the grade-scoped signature default. Registered BEFORE the tenant-level
    /// matcher so the query-carrying call wins (MockHttp matches in registration order).
    /// Request matching is on the query string because MockHttp's string matchers ignore it
    /// when both calls share the same path.</summary>
    private void SetupGradeSignatureDefault(Guid gradeLevelId, SignatureRequirementMode mode)
    {
        var requiresSignature = mode != SignatureRequirementMode.Disabled ? "true" : "false";
        _mockHttp.When(HttpMethod.Get, "http://localhost/assignments/signature-default")
            .With(req => req.RequestUri!.Query.Contains(gradeLevelId.ToString(), StringComparison.OrdinalIgnoreCase))
            .Respond(HttpStatusCode.OK, "application/json",
                $"{{\"requiresSignature\":{requiresSignature},\"signatureMode\":\"{mode}\"}}");
    }

    private static Guid TopicId { get; } = Guid.Parse("00000000-0000-0000-0000-0000000000cc");

    private static Guid GradeFiveId { get; } = Guid.Parse("00000000-0000-0000-0000-000000000005");

    private static GradeLevelDto GradeFive =>
        new(GradeFiveId, Guid.NewGuid(), 5, "Grade 5", 5, 1, 0, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    /// <summary>The guardian-signature checkbox, found by its stable id (the retired suite's
    /// index-based <c>FindComponents&lt;FluentCheckbox&gt;()[1]</c> is deliberately gone).</summary>
    private static FluentCheckbox SignatureCheckbox(IRenderedComponent<CreatePage> cut) =>
        cut.FindComponents<FluentCheckbox>()
            .Single(c => c.Instance.Id == "authoring-submission-requires-signature").Instance;

    private static FluentSelect<Authoring.PickerOption> Picker(IRenderedComponent<CreatePage> cut, string id) =>
        cut.FindComponents<FluentSelect<Authoring.PickerOption>>().Single(s => s.Instance.Id == id).Instance;

    /// <summary>Drives a picker through its real <c>SelectedOptionChanged</c> callback (the
    /// AssignmentPolicyFieldEditDialogTests pattern) — never a raw DOM event.</summary>
    private static Task SelectAsync(IRenderedComponent<CreatePage> cut, string pickerId, Guid value, string label) =>
        cut.InvokeAsync(() => Picker(cut, pickerId).SelectedOptionChanged.InvokeAsync(
            new Authoring.PickerOption(value.ToString(), label)));

    /// <summary>Selects the primary grade. The picker binds <c>SelectedOptionChanged</c>
    /// explicitly (it carries the FR-58 subject reload plus the signature re-resolve), so the
    /// test drives that same callback — exactly what the select raises for a real user pick.</summary>
    private static Task SelectGradeAsync(IRenderedComponent<CreatePage> cut, Guid value, string label) =>
        SelectAsync(cut, "authoring-basics-grade", value, label);

    /// <summary>Fills the Basics title through the bound text field's own callback — the create
    /// guards require a non-empty title before anything is posted.</summary>
    private static Task SetTitleAsync(IRenderedComponent<CreatePage> cut, string title) =>
        cut.InvokeAsync(() => cut.FindComponents<FluentTextField>()
            .Single(f => f.Instance.Id == "authoring-basics-title").Instance.ValueChanged.InvokeAsync(title));

    /// <summary>The D2 Mandatory-lock tooltip copy, bound once so the assertion and the
    /// component cannot drift silently (the <c>QuestionGenerationGate.DisabledTooltip</c>
    /// precedent).</summary>
    private const string AssignmentSignatureMandatoryTooltip = Authoring.SignatureMandatoryReason;

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
        SetupSignatureDefault(SignatureRequirementMode.Disabled);

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
        SetupSignatureDefault(SignatureRequirementMode.Disabled);

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
        SetupSignatureDefault(SignatureRequirementMode.Disabled);

        var cut = Render<CreatePage>();

        cut.WaitForAssertion(() =>
            cut.Markup.Should().Contain("Require guardian signature after completion"),
            TimeSpan.FromSeconds(5));

        cut.FindComponents<FluentProgressRing>().Should().BeEmpty(
            "the page must not be left spinning by the absent optional fetch");
    }

    /// <summary>Assertion 1 (ported): a <c>Disabled</c> requirement leaves the signature
    /// checkbox unchecked and free.</summary>
    [TestMethod]
    public void Create_DisabledSignatureDefault_LeavesCheckboxUncheckedAndFree()
    {
        SetupGradeLevels(GradeFive);
        SetupActivityGroups();
        SetupSignatureDefault(SignatureRequirementMode.Disabled);

        var cut = Render<CreatePage>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Require guardian signature after completion");
            SignatureCheckbox(cut).Value.Should().BeFalse("a Disabled requirement leaves the signature checkbox unchecked");
            SignatureCheckbox(cut).Disabled.Should().BeFalse("a Disabled requirement leaves the checkbox free");
        }, TimeSpan.FromSeconds(5));
    }

    /// <summary>Assertion 2 (ported): an <c>Optional</c> tenant default pre-ticks the checkbox
    /// while leaving the author in control.</summary>
    [TestMethod]
    public void Create_OptionalSignatureDefault_PreFillsTheCheckbox()
    {
        SetupGradeLevels(GradeFive);
        SetupActivityGroups();
        SetupSignatureDefault(SignatureRequirementMode.Optional);

        var cut = Render<CreatePage>();

        cut.WaitForAssertion(() =>
        {
            SignatureCheckbox(cut).Value.Should().BeTrue("the resolved default pre-fills the checkbox");
            SignatureCheckbox(cut).Disabled.Should().BeFalse(
                "an Optional requirement pre-fills but leaves the author in control");
        }, TimeSpan.FromSeconds(5));
    }

    /// <summary>Assertion 1 (ported, discriminating): the grade-scoped requirement wins over
    /// the tenant default as soon as a grade is chosen.</summary>
    [TestMethod]
    public async Task Create_GradeSelected_ReResolvesTheSignatureDefault()
    {
        SetupGradeLevels(GradeFive);
        SetupActivityGroups();
        SetupGradeSignatureDefault(GradeFiveId, SignatureRequirementMode.Mandatory);
        SetupSignatureDefault(SignatureRequirementMode.Disabled); // the init no-grade resolve
        SetupSubjects(GradeFiveId);

        var cut = Render<CreatePage>();
        cut.WaitForAssertion(() => SignatureCheckbox(cut).Value.Should().BeFalse());

        await SelectGradeAsync(cut, GradeFiveId, "Grade 5");

        cut.WaitForAssertion(() =>
        {
            SignatureCheckbox(cut).Value.Should().BeTrue(
                "the grade's Mandatory requirement replaces the tenant Disabled default");
            SignatureCheckbox(cut).Disabled.Should().BeTrue("Mandatory locks the checkbox");
        }, TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Assertion 3 (ported, the discriminating case): a <c>Mandatory</c> requirement
    /// pre-ticks, DISABLES and explains the checkbox, and the submitted create body still
    /// carries <c>requiresSignature: true</c> (the snapshot the policy locked in).
    /// </summary>
    [TestMethod]
    public async Task Create_MandatoryPolicy_LocksCheckboxAndSubmitsTrue()
    {
        SetupGradeLevels(GradeFive);
        SetupActivityGroups();
        SetupSignatureDefault(SignatureRequirementMode.Mandatory);
        SetupSubjects(GradeFiveId);

        var cut = Render<CreatePage>();

        await SelectGradeAsync(cut, GradeFiveId, "Grade 5");

        cut.WaitForAssertion(() =>
        {
            SignatureCheckbox(cut).Value.Should().BeTrue("Mandatory pre-ticks the checkbox");
            SignatureCheckbox(cut).Disabled.Should().BeTrue("Mandatory locks the checkbox");
            cut.Markup.Should().Contain(AssignmentSignatureMandatoryTooltip,
                "the disabled checkbox explains why it cannot be unticked");
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

        cut.WaitForAssertion(() => capturedBody.Should().NotBeNull("the primary action saves the draft"),
            TimeSpan.FromSeconds(5));
        capturedBody.Should().Contain("\"requiresSignature\":true",
            "the locked value is what the author submits — the persisted assignment keeps the policy snapshot");
    }

    /// <summary>Assertion 4 (ported): an author override of an <c>Optional</c> default is what
    /// gets submitted.</summary>
    [TestMethod]
    public async Task Create_AuthorOverrides_OverridesPrefillAndSubmitsValue()
    {
        SetupGradeLevels(GradeFive);
        SetupActivityGroups();
        SetupSignatureDefault(SignatureRequirementMode.Optional);
        SetupSubjects(GradeFiveId);

        var cut = Render<CreatePage>();

        await SelectGradeAsync(cut, GradeFiveId, "Grade 5");

        // The author overrides the pre-filled true back to false.
        await cut.InvokeAsync(() => SignatureCheckbox(cut).ValueChanged.InvokeAsync(false));

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

        cut.WaitForAssertion(() => capturedBody.Should().NotBeNull("the primary action saves the draft"),
            TimeSpan.FromSeconds(5));
        capturedBody.Should().Contain("\"requiresSignature\":false",
            "the author's override is submitted in the create request");
    }

    [TestMethod]
    public void Create_PreFillFetchFails_CheckboxStaysDefault_NoError()
    {
        SetupGradeLevels(GradeFive);
        SetupActivityGroups();
        _mockHttp.When(HttpMethod.Get, "http://localhost/assignments/signature-default")
            .Respond(HttpStatusCode.InternalServerError);

        var cut = Render<CreatePage>();

        cut.WaitForAssertion(() =>
        {
            SignatureCheckbox(cut).Value.Should().BeFalse("a failed resolve keeps the checkbox at its default");
            SignatureCheckbox(cut).Disabled.Should().BeFalse(
                "fail-open: a transport failure must never lock the author out of their own choice");
            cut.FindComponents<FluentMessageBar>()
                .Should().NotContain(mb => mb.Instance.Intent == MessageIntent.Error,
                    "the fail-open pre-fill does not render an error message bar");
        }, TimeSpan.FromSeconds(5));
    }
}
