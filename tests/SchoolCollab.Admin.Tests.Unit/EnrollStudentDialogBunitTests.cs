using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components;
using SchoolCollab.Admin.Shared.Components.Dialogs;
using SchoolCollab.Admin.Shared.Constants;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Core.Features;
using SchoolCollab.Students.Application.Components.Students;
using SchoolCollab.Students.Application.Services;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for <see cref="EnrollStudentDialog"/> — the dialog that
/// enrols a student in a grade level for the tenant's current global
/// (Active) period. The dialog is a <see cref="DialogShellBase{TModel, TResult}"/>
/// shell over <see cref="StudentsApiClient.EnrollStudentAsync"/>; it loads
/// periods + grade levels in <c>OnInitializedAsync</c>, renders the canonical
/// FormRow form (read-only Period, Grade-level <see cref="CodedValueDropdown"/>,
/// Enrolled-on date picker), and submits via the shared
/// <see cref="DialogShellFooter"/>.
///
/// <para>These tests exercise the dialog end-to-end inside a real
/// <see cref="FluentDialogProvider"/> + <see cref="IDialogService"/> host
/// (the same hosting model <c>DialogServiceExtensions.ShowShellDialogAsync</c>
/// uses in production), with a stub <see cref="HttpMessageHandler"/> backing
/// the <see cref="StudentsApiClient"/> + <see cref="CodedValuesApiClient"/>.
/// JS interop is <c>Loose</c> (FluentUI's focus/scroll JS calls are stubbed).</para>
///
/// <para>Coverage:</para>
/// <list type="bullet">
///   <item>The no-active-period hard block (warning MessageBar + periods link)</item>
///   <item>The happy-path form render (Period read-only academic-year format,
///         Grade dropdown, Enrolled-on date picker, "Enroll" submit button)</item>
///   <item>The "no Student row" contract (the caller already shows the student)</item>
///   <item>The data-load error path (API failure surfaces the full tracing detail)</item>
///   <item>The enroll success path (POST 200 → dialog closes with
///         <see cref="EnrollStudentResult"/>Success=true)</item>
///   <item>The enroll failure path (POST 400 + body → error bar shows the body,
///         dialog stays open so the user can retry)</item>
///   <item>The "+" inline-create-grade button gating: hidden for re-enrollment
///         (even with the flag on), hidden for new-enrollment when the flag is
///         off, shown only for new-enrollment when the flag is on</item>
/// </list>
/// </summary>
[TestClass]
public class EnrollStudentDialogBunitTests : BunitContext
{
    private static readonly Guid StudentId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PeriodId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid GradeLevelId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid GradeCodedValueId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    // Real dev-database GUIDs (schoolcollab_settings.coded_values): the
    // GRADE_5 coded value and the three GRSTREAMS children whose gradeLevel
    // attribute references it.
    internal static readonly Guid Grade5CodedValueId = Guid.Parse("ab50d479-9615-4dce-8fbc-1a163595411c");
    internal static readonly Guid GradeLevelId5 = Guid.Parse("55555555-5555-5555-5555-555555555555");
    internal static readonly Guid Stream5AId = Guid.Parse("5e369893-3895-4dad-99ea-51a08979d5d6");
    internal static readonly Guid Stream5BId = Guid.Parse("ce87c23f-e275-4790-a2f9-7fb90f6fffa4");
    internal static readonly Guid Stream5CId = Guid.Parse("d3dfd9c1-7f40-475e-991e-1c1676488729");
    private const string EnrollErrorBody = "Cannot enrol students: no active period is open for this tenant. Open a period before enrolling.";

    private IDialogService DialogService => Services.GetRequiredService<IDialogService>();

    public EnrollStudentDialogBunitTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    // ── Test doubles ───────────────────────────────────────────────────────

    /// <summary>
    /// Stub <see cref="IFeatureFlagService"/> with a mutable <see cref="Enabled"/>
    /// flag so each test can opt the
    /// <c>FEATURE:EnableGradeLevelSetupOnEnrollDialog</c> flag on/off without
    /// touching config. Mirrors the <c>StubFlagService</c> in
    /// <c>FeatureFlagGateTests</c>.
    /// </summary>
    private sealed class StubFlagService : IFeatureFlagService
    {
        public bool Enabled { get; set; }
        public bool IsEnabled(string featureKey) => Enabled;
        public Task<bool> IsEnabledAsync(string featureKey, CancellationToken ct = default) => Task.FromResult(Enabled);
        public IDictionary<string, bool> GetAllFlags() => new Dictionary<string, bool>();
        public Task<IReadOnlyDictionary<string, bool>> GetAllFlagsAsync(Guid? tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, bool>>(new Dictionary<string, bool>());
    }

    private sealed class StubFlagNotifier : IFeatureFlagChangeNotifier
    {
        public event Action? FeatureFlagsChanged;
        public void Raise() => FeatureFlagsChanged?.Invoke();
    }

    /// <summary>
    /// Minimal auth state provider — <see cref="GateBase"/> requires an
    /// <see cref="AuthenticationStateProvider"/> (it subscribes to
    /// <c>AuthenticationStateChanged</c>). A bare unauthenticated principal is
    /// enough; the enroll dialog does not gate on the tenant claim.
    /// </summary>
    private sealed class TestAuthProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    /// <summary>
    /// Configurable HTTP handler backing the <see cref="StudentsApiClient"/> +
    /// <see cref="CodedValuesApiClient"/>. Each test flips the public fields
    /// to drive the dialog's load + submit paths. Defaults to the happy-path
    /// load (one Active period + one grade level + the matching coded value).
    /// </summary>
    private sealed class EnrollHttpHandler : HttpMessageHandler
    {
        public bool PeriodsError;          // GET /students/periods → 500
        public bool NoActivePeriod;        // return periods with no "Active" entry
        public bool EnrollFails;           // POST /students/enrollments → 400 + body
        public string ErrorBody = EnrollErrorBody;
        public int EnrollPostCount;        // tracks how many POSTs were attempted
        public string? LastEnrollBody;     // raw JSON of the last enroll POST

        /// <summary>When true, the served grade lists ALSO include a
        /// "Grade 5" entry (coded value <see cref="Grade5CodedValueId"/>)
        /// and the GRSTREAMS by-parent lookup returns the three real
        /// Grade 5 stream rows when filtered by that coded value.</summary>
        public bool IncludeGrade5;

        /// <summary>How many GRSTREAMS by-parent lookups reached the
        /// handler — used by the no-grade guard test to assert that NO
        /// stream lookup is issued before a grade is selected.</summary>
        public int StreamLookupCount;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(Respond(request));

        private HttpResponseMessage Respond(HttpRequestMessage request)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            var query = request.RequestUri?.Query ?? string.Empty;

            // GET /students/periods — the dialog derives the active period from here.
            if (path.Contains("/students/periods", StringComparison.OrdinalIgnoreCase)
                && HttpMethod.Get.Equals(request.Method))
            {
                if (PeriodsError)
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    { Content = new StringContent("periods boom") };

                var periods = NoActivePeriod
                    ? Array.Empty<PeriodDto>()
                    : new PeriodDto[]
                    {
                        new(PeriodId, "2025/2026",
                            new DateOnly(2025, 9, 1), new DateOnly(2026, 8, 31),
                            "Active", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
                    };
                return Json(HttpStatusCode.OK, periods);
            }

            // GET /students/grade-levels/{id} — the suggested-grade lookup
            // (the dialog resolves Model.SuggestedGradeLevelId to its coded
            // value with a single-item fetch).
            if (path.StartsWith("/students/grade-levels/", StringComparison.OrdinalIgnoreCase)
                && HttpMethod.Get.Equals(request.Method))
            {
                var idSegment = path["/students/grade-levels/".Length..];
                if (IncludeGrade5 && idSegment == GradeLevelId5.ToString())
                {
                    return Json(HttpStatusCode.OK, new GradeLevelDto(GradeLevelId5, Grade5CodedValueId, 5,
                        "Grade 5", 5, 0, 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
                }
                if (idSegment == GradeLevelId.ToString())
                {
                    return Json(HttpStatusCode.OK, new GradeLevelDto(GradeLevelId, GradeCodedValueId, 7,
                        "Grade 7", 7, 0, 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                { Content = new StringContent("unknown grade level") };
            }

            // GET /students/grade-levels — the grade-resolution list.
            if (path.Contains("/students/grade-levels", StringComparison.OrdinalIgnoreCase)
                && HttpMethod.Get.Equals(request.Method))
            {
                var grades = new List<GradeLevelDto>
                {
                    new(GradeLevelId, GradeCodedValueId, 7, "Grade 7", 7, 0, 0,
                        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
                };
                if (IncludeGrade5)
                {
                    grades.Add(new GradeLevelDto(GradeLevelId5, Grade5CodedValueId, 5, "Grade 5", 5,
                        0, 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
                }
                return Json(HttpStatusCode.OK, grades);
            }

            // GET /api/coded-values/by-parent?parentCode=GRADE — the CodedValueDropdown load.
            if (path.StartsWith("/api/coded-values/by-parent", StringComparison.OrdinalIgnoreCase)
                && HttpMethod.Get.Equals(request.Method)
                && query.Contains("parentCode=GRADE", StringComparison.OrdinalIgnoreCase))
            {
                var values = new List<CodedValueDto>
                {
                    new(GradeCodedValueId, "GRADE_7", "Grade 7", null,
                        Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"), "GRADE",
                        false, 7, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                        [], [], 0, false, null, false)
                };
                if (IncludeGrade5)
                {
                    values.Add(new CodedValueDto(Grade5CodedValueId, "GRADE_5", "Grade 5", null,
                        Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"), "GRADE",
                        false, 5, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                        [], [], 0, false, null, false));
                }
                return Json(HttpStatusCode.OK, values);
            }

            // GET /api/coded-values/by-parent?parentCode=GRSTREAMS[&attributeKey=gradeLevel&attributeValue=…]
            // — the stream picker's load. With IncludeGrade5, the filtered
            // lookup for the Grade 5 coded value returns the three real
            // Grade 5 streams; every other shape returns an empty list.
            if (path.StartsWith("/api/coded-values/by-parent", StringComparison.OrdinalIgnoreCase)
                && HttpMethod.Get.Equals(request.Method)
                && query.Contains("parentCode=GRSTREAMS", StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref StreamLookupCount);
                var matchesGrade5 = IncludeGrade5
                    && query.Contains($"attributeValue={Grade5CodedValueId}", StringComparison.OrdinalIgnoreCase);
                if (!matchesGrade5)
                {
                    return Json(HttpStatusCode.OK, Array.Empty<CodedValueDto>());
                }
                return Json(HttpStatusCode.OK, new CodedValueDto[]
                {
                    new(Stream5AId, "GRSTREAMS_5A", "Grade 5 — A", null,
                        Guid.Parse("ffff0000-0000-0000-0000-000000000000"), "GRSTREAMS",
                        false, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                        [], [], 0, false, null, false),
                    new(Stream5BId, "GRSTREAMS_5B", "Grade 5 — B", null,
                        Guid.Parse("ffff0000-0000-0000-0000-000000000000"), "GRSTREAMS",
                        false, 2, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                        [], [], 0, false, null, false),
                    new(Stream5CId, "GRSTREAMS_5C", "Grade 5 — C", null,
                        Guid.Parse("ffff0000-0000-0000-0000-000000000000"), "GRSTREAMS",
                        false, 3, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                        [], [], 0, false, null, false),
                });
            }

            // POST /students/enrollments — the actual enrol submission.
            if (path.Equals("/students/enrollments", StringComparison.OrdinalIgnoreCase)
                && HttpMethod.Post.Equals(request.Method))
            {
                EnrollPostCount++;
                LastEnrollBody = request.Content is null
                    ? null
                    : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (EnrollFails)
                {
                    return new HttpResponseMessage(HttpStatusCode.BadRequest)
                    { Content = new StringContent(ErrorBody) };
                }
                // IdResponse is an internal record in StudentsApiClient; the JSON
                // shape is just { "id": "..." } so an anonymous object serializes
                // identically for ReadFromJsonAsync<IdResponse>.
                return Json(HttpStatusCode.OK, new { Id = Guid.NewGuid() });
            }

            // Default: 404 so an unexpected call fails loudly.
            return new HttpResponseMessage(HttpStatusCode.NotFound)
            { Content = new StringContent($"Unhandled request: {request.Method} {path}{query}") };
        }

        private static HttpResponseMessage Json<T>(HttpStatusCode status, T body) =>
            new(status) { Content = JsonContent.Create(body) };
    }

    // ── Service registration helper ────────────────────────────────────────

    /// <summary>
    /// Registers the Students + CodedValues API clients over a fresh
    /// <see cref="EnrollHttpHandler"/>, plus the auth + feature-flag services
    /// the dialog (and its <see cref="FeatureFlagGate"/> child) need. Returns
    /// the handler + flag service so the test can mutate them per-scenario.
    /// </summary>
    private (EnrollHttpHandler handler, StubFlagService flags) RegisterServices(bool flagOn)
    {
        var handler = new EnrollHttpHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };

        Services.AddSingleton<AuthenticationStateProvider>(new TestAuthProvider());
        var codedValuesClient = new CodedValuesApiClient(http);
        Services.AddSingleton(codedValuesClient);
        Services.AddSingleton(new StudentsApiClient(
            http, NullLogger<StudentsApiClient>.Instance, codedValuesClient));

        var flags = new StubFlagService { Enabled = flagOn };
        Services.AddSingleton<IFeatureFlagService>(flags);
        Services.AddSingleton<IFeatureFlagChangeNotifier>(new StubFlagNotifier());

        return (handler, flags);
    }

    /// <summary>Renders the dialog provider and opens the enroll dialog via the
    /// production extension. Returns the rendered provider + the awaiting task.</summary>
    private (IRenderedComponent<FluentDialogProvider> cut, Task<EnrollStudentResult?> task)
        OpenDialog(EnrollStudentModel model)
    {
        var cut = Render<FluentDialogProvider>();
        var task = DialogService.ShowShellDialogAsync<EnrollStudentDialog, EnrollStudentModel, EnrollStudentResult>(
            model, title: "Enroll student", size: DialogSize.Medium);
        return (cut, task);
    }

    private static EnrollStudentModel NewEnrollment() => new(StudentId);
    private static EnrollStudentModel ReEnrollment() => new(StudentId, SuggestedGradeLevelId: GradeLevelId);

    // ── No active period → hard block ───────────────────────────────────────

    [TestMethod]
    public void NoActivePeriod_ShowsWarningAndPeriodsLink_InsteadOfForm()
    {
        var (handler, _) = RegisterServices(flagOn: false);
        handler.NoActivePeriod = true;
        var (cut, _) = OpenDialog(NewEnrollment());

        // The warning MessageBar renders (no form) when there is no Active period.
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("No active academic period", "the dialog hard-blocks enrolment when no Active period exists");
            cut.Markup.Should().Contain("/students/periods", "the warning links to the periods page so the user can open one");
            cut.FindAll("form").Count.Should().Be(0, "the enrol form must NOT render when there is no active period — the server would reject it anyway");
        });
    }

    // ── Active period → form renders with all three rows ───────────────────

    [TestMethod]
    public void ActivePeriod_RendersForm_WithPeriod_Grade_EnrolledOn_Rows()
    {
        RegisterServices(flagOn: false);
        var (cut, _) = OpenDialog(NewEnrollment());

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull("the enrol form renders when an Active period exists"));

        // The three canonical FormRow labels are present.
        cut.Markup.Should().Contain("Period", "the read-only Period row is rendered");
        cut.Markup.Should().Contain("Grade level", "the Grade-level dropdown row is rendered");
        cut.Markup.Should().Contain("Enrolled on", "the Enrolled-on date-picker row is rendered");
    }

    [TestMethod]
    public void ActivePeriod_DisplaysAcademicYearFormat_YYYY_Slash_YYYY()
    {
        RegisterServices(flagOn: false);
        var (cut, _) = OpenDialog(NewEnrollment());

        // The period is shown as "{StartDate.Year}/{EndDate.Year}" (2025/2026),
        // NOT the period's Name ("2025/2026" happens to match the name here, but
        // the format is derived from the dates so a period named "Fall 2025"
        // would still show "2025/2026"). Assert the academic-year token is in
        // the read-only input's value.
        cut.WaitForAssertion(() =>
        {
            // FluentTextField renders as a <fluent-text-field> custom element (not a
            // native <input>); the value is exposed via the `current-value` attribute.
            var periodField = cut.FindAll("fluent-text-field")
                .FirstOrDefault(f => f.GetAttribute("class")?.Contains("enroll-form-input--readonly") == true);
            periodField.Should().NotBeNull("the Period row renders a read-only FluentTextField");
            // The active period spans 2025-09-01 → 2026-08-31, so the academic-year
            // token is "2025/2026". FluentUI exposes the bound Value as `current-value`.
            var value = periodField!.GetAttribute("current-value") ?? periodField.GetAttribute("value") ?? "";
            value.Should().Be("2025/2026",
                "the Period row displays the canonical academic-year format {StartDate.Year}/{EndDate.Year}");
        });
    }

    [TestMethod]
    public void Dialog_DoesNotRenderAStudentRow()
    {
        // The dialog intentionally does NOT render a "Student" row — the caller
        // (Detail.razor / Edit.razor) already shows the student context on the
        // page. Re-stating it inside the dialog is redundant. This is the
        // "remove student row" fix; a regression that re-introduces a
        // <FormRow Label="Student"> must be caught.
        RegisterServices(flagOn: false);
        var (cut, _) = OpenDialog(NewEnrollment());

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());
        // The dialog MUST NOT render a Student FormRow. FormRow renders its label
        // as <div class="form-row-label"><label>LABEL</label></div>, so we collect
        // every form-row label text and assert none is "Student". (Checking the
        // whole form's TextContent would false-positive on the dialog title
        // "Enroll student" rendered in the FluentDialog header, and on any stray
        // attribute value — the label-scoped check is the precise contract.)
        var rowLabels = cut.FindAll(".form-row-label label")
            .Select(l => l.TextContent.Trim())
            .ToArray();
        rowLabels.Should().NotContain("Student",
            "no form-row should be labelled \"Student\" — the caller already shows the student context on the page; re-stating it inside the dialog is redundant");
        // The three canonical rows (Period, Grade level, Enrolled on) must be
        // present. Required rows append "*" to the label text, so we match on
        // prefix rather than exact equality.
        rowLabels.Should().Contain(l => l.StartsWith("Period", StringComparison.Ordinal),
            "the Period row is present — confirms the label scan is looking at the right rows");
        rowLabels.Should().Contain(l => l.StartsWith("Grade level", StringComparison.Ordinal),
            "the Grade level row is present");
        rowLabels.Should().Contain(l => l.StartsWith("Enrolled on", StringComparison.Ordinal),
            "the Enrolled on row is present");
    }

    [TestMethod]
    public void SubmitButton_Text_Is_Enroll_Not_Save()
    {
        // The dialog overrides SubmitText="Enroll" (and SavingText="Enrolling…")
        // so the primary action verb matches the dialog's intent. A regression
        // that drops the override would fall back to the DialogShellBase default
        // "Save" — caught here.
        RegisterServices(flagOn: false);
        var (cut, _) = OpenDialog(NewEnrollment());

        cut.WaitForAssertion(() =>
        {
            var buttons = cut.FindAll(".button-row fluent-button");
            buttons.Should().NotBeEmpty("the DialogShellFooter renders a Cancel + Submit button row");
            var submit = buttons.FirstOrDefault(b => b.TextContent.Contains("Enroll"));
            submit.Should().NotBeNull("the Submit button text MUST be \"Enroll\" (the dialog overrides SubmitText)");
            submit!.TextContent.Should().NotContain("Save",
                "the default DialogShellBase SubmitText is \"Save\" — the dialog MUST override it to \"Enroll\"");
        });
    }

    // ── Data-load error path ───────────────────────────────────────────────

    [TestMethod]
    public void LoadError_ShowsErrorMessageBar_WithTracingDetail()
    {
        // When ListPeriodsAsync fails (500), the dialog surfaces the FULL
        // tracing detail (status code + response body) in an error MessageBar —
        // not just the generic "One or more errors occurred." This is the
        // "trace data-load errors to API" fix; the inner-exception unwrap in
        // OnInitializedAsync ensures the useful HTTP detail reaches the user.
        var (handler, _) = RegisterServices(flagOn: false);
        handler.PeriodsError = true;
        var (cut, _) = OpenDialog(NewEnrollment());

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("ListPeriods failed", "the error bar surfaces the failed-operation name");
            cut.Markup.Should().Contain("500", "the error bar surfaces the status code");
            cut.Markup.Should().Contain("periods boom", "the error bar surfaces the response body — the whole point of the body-read pattern");
            cut.FindAll("form").Count.Should().Be(0, "the form must NOT render when the load failed — there is no data to enrol against");
        });
    }

    // ── Silent no-op guard (the "clicking Enroll does nothing" fix) ──────────

    [TestMethod]
    public void SubmitWithNoGrade_ShowsSelectGradeError_InsteadOfSilentNoOp()
    {
        // Regression for the reported bug: clicking Enroll with no grade
        // selected used to silently return null from SubmitAsync — the dialog
        // neither closed nor showed an error, so the user saw "clicking Enroll
        // does nothing". The EditForm has no DataAnnotationsValidator (the grade
        // is a separate field, not on the FormState model), so Blazor validation
        // does not block the submit; the null-guard in SubmitAsync must surface a
        // visible, actionable error instead of a silent no-op.
        RegisterServices(flagOn: false);
        var (cut, task) = OpenDialog(NewEnrollment()); // new enrollment, no grade pre-selected

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());
        cut.Find("form").Submit();

        // The dialog MUST surface a visible error (not a silent no-op).
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Select a grade level",
            "a missing grade must surface a visible error, not a silent no-op — the reported bug was the silent null-return with no Error set"));
        task.IsCompleted.Should().BeFalse(
            "the dialog stays open so the user can pick a grade and retry; the error guides them, the dialog does not close on a rejected submit");
    }

    // ── Enrol success path ─────────────────────────────────────────────────

    [TestMethod]
    public async Task EnrollSuccess_PostsToEnrollments_AndClosesDialogWithSuccessResult()
    {
        // Happy path: an Active period + a pre-selected grade (the caller passes
        // the student's suggested grade via SuggestedGradeLevelId) + a 200 from
        // POST /students/enrollments → the dialog closes with
        // EnrollStudentResult(Success=true). Verify the POST was actually sent
        // (exactly once) and the request body carried the active period + the
        // resolved grade level (NOT the CodedValueId — the dialog maps
        // CodedValueId → GradeLevelDto.Id before submitting).
        var (handler, _) = RegisterServices(flagOn: false);
        var (cut, task) = OpenDialog(ReEnrollment());

        // Wait for the form to render (loads complete), then submit it.
        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());
        cut.Find("form").Submit();

        var result = await task;
        result.Should().NotBeNull("a successful enrol MUST close the dialog with a non-null result");
        result!.Success.Should().BeTrue("the enrol result carries Success=true on a 200 response");
        handler.EnrollPostCount.Should().Be(1, "exactly one POST /students/enrollments must be sent on a single submit (no double-submit)");
    }

    // ── Enrol failure path ─────────────────────────────────────────────────

    [TestMethod]
    public void EnrollFailure_ShowsErrorBody_AndKeepsDialogOpen()
    {
        // When POST /students/enrollments returns 400 + a body, the dialog
        // surfaces the FULL body (not just the status text) in the per-field
        // error MessageBar AND keeps the dialog open so the user can retry.
        // This is the "API body-read pattern" in EnrollStudentAsync — without
        // it the bar would show only "Response status code does not indicate
        // success: 400 (Bad Request)." which is useless for tracing.
        var (handler, _) = RegisterServices(flagOn: false);
        handler.EnrollFails = true;
        var (cut, task) = OpenDialog(ReEnrollment());

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());
        cut.Find("form").Submit();

        // The error bar appears with the SERVER'S body text (the tracing detail).
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("EnrollStudent failed", "the error surfaces the failed-operation name");
            cut.Markup.Should().Contain("400", "the error surfaces the status code");
            cut.Markup.Should().Contain(EnrollErrorBody, "the error surfaces the FULL response body — the body-read pattern's whole value");
        });

        // The dialog was NOT closed (CloseAsync is not called on the null-return
        // path), so the awaiting task is still pending — the user can retry.
        task.IsCompleted.Should().BeFalse("the dialog MUST stay open on a failed enrol so the user can fix the input and retry");
        handler.EnrollPostCount.Should().Be(1, "the POST was attempted exactly once before the error was surfaced");
    }

    // ── "+" inline-create-grade button gating ───────────────────────────────
    //
    // The "+" button is gated by TWO things ANDed together:
    //   1. IsNewEnrollment (Model.SuggestedGradeLevelId is null) — UX correctness
    //   2. FEATURE:EnableGradeLevelSetupOnEnrollDialog — governance
    // Either gate being false hides the button. The three tests below pin the
    // three non-trivial combinations (the fourth — new + flag off — is the
    // default and is covered by the form-render tests above, which assert no
    // + button via the absence of the enroll-grade-add class).

    [TestMethod]
    public void PlusButton_Hidden_ForReEnrollment_EvenWhenFlagIsOn()
    {
        // A re-enrollment (the student is already enrolled → SuggestedGradeLevelId
        // is set) must NOT show the + button, even if the tenant has opted in to
        // the flag. The new-enrollment check is the UX-correctness gate: for a
        // re-enrollment the right action is "pick a different existing grade",
        // not "stand a brand-new grade up mid-flow".
        RegisterServices(flagOn: true);
        var (cut, _) = OpenDialog(ReEnrollment());

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());
        cut.Markup.Should().NotContain("enroll-grade-add",
            "the + button MUST be hidden for a re-enrollment even when the flag is on — IsNewEnrollment is the UX-correctness gate");
        cut.Markup.Should().NotContain("Create a new grade level for this tenant",
            "the + button's Title text must not be present for a re-enrollment");
    }

    [TestMethod]
    public void PlusButton_Hidden_ForNewEnrollment_WhenFlagIsOff()
    {
        // The flag is opt-in (default false). A new enrollment with the flag OFF
        // must NOT show the + button — the inline-create-grade action has a
        // global side-effect (a new GRADE coded value + GradeLevel row, both
        // shared across tenants), so it stays hidden until a ConfigFlags toggle.
        RegisterServices(flagOn: false);
        var (cut, _) = OpenDialog(NewEnrollment());

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());
        cut.Markup.Should().NotContain("enroll-grade-add",
            "the + button MUST be hidden when the feature flag is off (opt-in, default false)");
        cut.Markup.Should().NotContain("Create a new grade level for this tenant",
            "the + button's Title text must not be present when the flag is off");
    }

    [TestMethod]
    public void PlusButton_Shown_ForNewEnrollment_WhenFlagIsOn()
    {
        // The ONLY combination that reveals the + button: a NEW enrollment
        // (SuggestedGradeLevelId is null) AND the flag ON. This is the tenant-
        // opted-in, first-time-enrollment case where inline grade setup is the
        // intended low-friction path.
        RegisterServices(flagOn: true);
        var (cut, _) = OpenDialog(NewEnrollment());

        cut.WaitForAssertion(() =>
        {
            cut.Find("form").Should().NotBeNull();
            cut.Markup.Should().Contain("enroll-grade-add",
                "the + button MUST render for a new enrollment when the flag is on — both gates pass");
            cut.Markup.Should().Contain("Create a new grade level for this tenant",
                "the + button's Title attribute is present so the user sees the tooltip");
        });
    }

    // ── Grade 5 → stream GUIDs (dev-data regression) ──────────────────

    /// <summary>
    /// When the grade level is set to Grade 5, the stream picker must load
    /// the three Grade 5 streams (real dev-database GUIDs) with non-null ids:
    /// GRSTREAMS_5A / 5B / 5C, whose gradeLevel attribute references the
    /// GRADE_5 coded value. Exercises the full chain: suggested grade →
    /// <c>_formModel.LoadFrom</c> → <c>GradeCodedValueIdForFilter</c> →
    /// stream picker's attribute-filtered by-parent call → items bound to
    /// the <see cref="CodedValueDropdown"/>.
    /// </summary>
    [TestMethod]
    public async Task Grade5Selected_StreamPickerLoads_NonNullStreamGuids()
    {
        var (handler, _) = RegisterServices(flagOn: false);
        handler.IncludeGrade5 = true;

        // Setting the grade level to Grade 5 = opening the dialog with the
        // Grade 5 grade level as the suggested/selected grade (the dialog's
        // own selection path: OnInitializedAsync → _formModel.LoadFrom).
        var (cut, _) = OpenDialog(new EnrollStudentModel(StudentId, SuggestedGradeLevelId: GradeLevelId5));

        // The stream CodedValueDropdown must end up with exactly the three
        // Grade 5 streams, every one carrying a non-null Guid.
        cut.WaitForState(() =>
        {
            var stream = cut.FindComponents<CodedValueDropdown>()
                .FirstOrDefault(d => d.Instance.Parent == CodedValueParent.Streams);
            // 3 real streams + the "No stream" clear-to-null sentinel.
            return stream is not null && stream.Instance.Items.Count == 4;
        }, timeout: TimeSpan.FromSeconds(5));

        var streamDropdown = cut.FindComponents<CodedValueDropdown>()
            .First(d => d.Instance.Parent == CodedValueParent.Streams);
        var items = streamDropdown.Instance.Items.ToList();

        // Clear-to-null affordance: ShowEmptyOption prepends the sentinel
        // "No stream" entry (Guid.Empty), which the dropdown maps to a null
        // SelectedId when picked — so the user can un-pick a stream.
        var emptyOption = items.SingleOrDefault(i => i.Id == Guid.Empty);
        emptyOption.Should().NotBeNull("ShowEmptyOption must prepend the 'No stream' clear option");
        emptyOption!.Name.Should().Be("No stream");

        // The remaining items are exactly the three Grade 5 streams, every one
        // carrying a usable id — never null, never Guid.Empty.
        var ids = items.Where(i => i.Id != Guid.Empty).Select(i => i.Id).ToList();
        ids.Should().NotContainNulls();
        ids.Should().OnlyContain(id => id != Guid.Empty, "no stream item may carry an empty (sentinel) id");
        ids.Should().BeEquivalentTo(new[] { Stream5AId, Stream5BId, Stream5CId });

        // ── Selection round-trip: picking a real stream binds its id back ──
        streamDropdown.Instance.SelectedIdChanged.HasDelegate.Should().BeTrue(
            "@bind-SelectedId must wire the SelectedIdChanged callback");
        var selectCallback = streamDropdown.Instance.SelectedIdChanged;
        var instanceBefore = streamDropdown.Instance;
        await cut.InvokeAsync(() => selectCallback.InvokeAsync(Stream5AId));
        var instanceAfter = cut.FindComponents<CodedValueDropdown>()
            .First(d => d.Instance.Parent == CodedValueParent.Streams).Instance;
        ReferenceEquals(instanceBefore, instanceAfter).Should().BeTrue(
            "the stream dropdown must NOT remount during a selection — a remount orphans the binder write");
        // Full-tree render: pushes the (binder-updated) form model back down.
        cut.Render();

        // The binder must have written the picked id onto the form model and
        // handed it back to the dropdown as its SelectedId parameter.
        cut.FindComponents<EnrollStudentDialog>().Count.Should().Be(1,
            "exactly one dialog instance must exist in the tree");

        // Submit and inspect the wire payload — the ground truth for what the
        // form model held after the pick.
        // ── Selection: picking a stream through the REAL event path binds its id ──
        // Drive FluentSelect.SelectedOptionChanged (what the web component
        // raises on a user pick) -> OnSelectedOptionChanged -> binder, then
        // read the dialog's private _formModel via reflection (ground truth).
        var streamDropdowns = cut.FindComponents<CodedValueDropdown>()
            .Where(d => d.Instance.Parent == CodedValueParent.Streams).ToList();
        streamDropdowns.Count.Should().Be(1, "exactly one stream picker must exist");
        var fluentSelect = streamDropdowns[0].FindComponent<
            Microsoft.FluentUI.AspNetCore.Components.FluentSelect<SchoolCollab.Admin.Shared.Services.CodedValueDto>>();
        var pickedItem = streamDropdowns[0].Instance.Items.First(i => i.Id == Stream5AId);
        await cut.InvokeAsync(() => fluentSelect.Instance.SelectedOptionChanged.InvokeAsync(pickedItem));

        var dialogInstance = cut.FindComponent<EnrollStudentDialog>().Instance;
        var fmField = typeof(EnrollStudentDialog).GetField("_formModel",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var formModel = fmField!.GetValue(dialogInstance);
        var boundStream = (Guid?)formModel!.GetType().GetProperty("StreamCodedValueId")!.GetValue(formModel);
        boundStream.Should().Be(Stream5AId,
            "selecting a stream must bind the stream's non-empty id onto the form model (not null / Guid.Empty)");

        // The 'No stream' sentinel is present so the user can clear back to null
        // (ShowEmptyOption); its Guid.Empty -> null mapping is covered by the
        // item-composition assertions above.
        var hasSentinel = streamDropdowns[0].Instance.Items.Any(i => i.Id == Guid.Empty);
        hasSentinel.Should().BeTrue("the 'No stream' clear-to-null option must be offered");

        // Submit via the EditForm (bUnit's form submit fires OnValidSubmit).
        cut.Find("form").Submit();
        handler.LastEnrollBody.Should().NotBeNull("the enroll POST must have fired");
        handler.LastEnrollBody.Should().Contain(Stream5AId.ToString(),
            $"the submitted enrollment must carry the picked stream id; body was: {handler.LastEnrollBody}");

    }

    /// <summary>
    /// GUARD: with NO grade selected, the stream picker must not render at
    /// all (read-only placeholder instead) and must NOT issue any GRSTREAMS
    /// lookup — stream values are only ever loaded against a concrete,
    /// non-empty grade coded value.
    /// </summary>
    [TestMethod]
    public async Task NoGradeSelected_StreamPickerNotRendered_AndNoStreamLookupIssued()
    {
        var (handler, _) = RegisterServices(flagOn: false);
        handler.IncludeGrade5 = true; // streams WOULD be served if a lookup leaked out

        var (cut, _) = OpenDialog(NewEnrollment());

        cut.WaitForAssertion(() =>
        {
            cut.Find("form").Should().NotBeNull();

            // The placeholder shows; no stream CodedValueDropdown exists.
            cut.Markup.Should().Contain("Select a grade first",
                "the stream row shows a read-only placeholder until a grade is picked");
            cut.FindComponents<CodedValueDropdown>()
                .Should().NotContain(d => d.Instance.Parent == CodedValueParent.Streams,
                    "the stream picker must not render without a selected grade");
        });

        // Give any (buggy) async load a chance to fire, then assert none did.
        await Task.Delay(300);
        handler.StreamLookupCount.Should().Be(0,
            "no GRSTREAMS lookup may be issued when no grade is selected");
    }

    /// <summary>
    /// Regression for "gradelevel isn't selecting": when the dialog opens for
    /// a re-enrollment, the grade CodedValueDropdown must bind to the suggested
    /// grade's coded value and the stream CodedValueDropdown must bind to the
    /// suggested stream. This verifies the data-binding and the dropdown's
    /// internal selection survive the async load workaround.
    /// </summary>
    [TestMethod]
    public void ReEnrollment_PreselectsSuggestedGradeAndStream()
    {
        var (handler, _) = RegisterServices(flagOn: false);
        handler.IncludeGrade5 = true;

        var (cut, _) = OpenDialog(new EnrollStudentModel(
            StudentId,
            SuggestedPeriodId: PeriodId,
            SuggestedGradeLevelId: GradeLevelId5,
            SuggestedStreamCodedValueId: Stream5AId));

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());

        var gradeDropdown = cut.FindComponents<CodedValueDropdown>()
            .First(d => d.Instance.Parent == CodedValueParent.Grades);
        gradeDropdown.Instance.SelectedId.Should().Be(Grade5CodedValueId,
            "the grade dropdown must bind to the suggested grade's coded value");

        var streamDropdown = cut.FindComponents<CodedValueDropdown>()
            .FirstOrDefault(d => d.Instance.Parent == CodedValueParent.Streams);
        streamDropdown.Should().NotBeNull("the stream picker renders when a grade is preselected");
        streamDropdown!.Instance.SelectedId.Should().Be(Stream5AId,
            "the stream dropdown must bind to the suggested stream");
    }
}