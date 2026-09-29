using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components.Dialogs;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Students.Application.Components.Students;
using SchoolCollab.Students.Application.Services;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for the <c>EnrollmentExceptionsDialog</c> — the management surface that
/// replaced the <c>/students/enrollment-exceptions</c> page
/// (subject-period-exception-model.md v8 §5.1) — AC-17.
///
/// <para>The dialog is the ONLY place an exception is edited, so these tests pin what a
/// management surface must not get wrong: zero exceptions is the normal state (no warning),
/// a term-shaped and a plain-window row both render their part and their span, open bounds
/// read <c>from …</c> / <c>to …</c>, the last exception stays removable, a server 409
/// surfaces the server's own message, the add section's period PART is CHOSEN as a division
/// and only where the tenant can write it, the POSITION is a structural multi-select rather
/// than a period pick, and the write rides the shell's own submit (DialogShellBase) through
/// <c>POST /students/enrollment-exceptions/bulk</c>.</para>
///
/// <para>The SCOPE (owner + subject) is locked by the call site (D5), so — unlike the retired
/// page — these tests supply it through the model rather than through a query string, and the
/// owner-toggle / subject-filter / "All subjects" / query-string suites are gone with the
/// chrome they exercised. Rendered through <see cref="FluentDialogProvider"/>, the same real
/// show&rarr;interact path <c>DialogShellTests</c> uses: the dialog's form IS a
/// <see cref="DialogShellBase{TModel,TResult}"/> EditForm, so its submit is driven through the
/// form (<c>Submit()</c>) rather than by clicking the footer's <c>Type=Submit</c> button.</para>
/// </summary>
[TestClass]
public class EnrollmentExceptionsDialogTests : BunitContext
{
    private const string ActivityGroupsUrl = "/activity-groups";
    private const string PeriodsUrl = "/students/periods";

    /// <summary>The exception resource both the list read and the bulk write live under.</summary>
    private const string ExceptionsResource = "/students/enrollment-exceptions";

    /// <summary>What the dialog is scoped to when a test does not say otherwise.</summary>
    private const string SubjectName = "Mathematics";

    private static readonly Guid GradeId = Guid.Parse("5b000000-0000-0000-0000-000000000001");
    private static readonly Guid GroupId = Guid.Parse("5b000000-0000-0000-0000-000000000002");
    private static readonly Guid TopicId = Guid.Parse("5b000000-0000-0000-0000-000000000003");
    private static readonly Guid YearId = Guid.Parse("5b000000-0000-0000-0000-000000000004");
    private static readonly Guid Term1Id = Guid.Parse("5b000000-0000-0000-0000-000000000005");
    private static readonly Guid Term5Id = Guid.Parse("5b000000-0000-0000-0000-000000000008");
    private static readonly Guid Semester2Id = Guid.Parse("5b000000-0000-0000-0000-00000000000b");
    private static readonly Guid YearBId = Guid.Parse("5b000000-0000-0000-0000-000000000009");
    private static readonly Guid Term1BId = Guid.Parse("5b000000-0000-0000-0000-00000000000a");

    /// <summary>Term 1's dates — the span the 1st position fills (§5.1).</summary>
    private static readonly DateOnly Term1Start = new(2027, 3, 1);
    private static readonly DateOnly Term1End = new(2027, 6, 30);

    /// <summary>Semester 2's dates — the span the 2nd SEMESTER position fills, so a test can
    /// tell a CHOSEN part from a derived one.</summary>
    private static readonly DateOnly Sem2Start = new(2027, 7, 1);
    private static readonly DateOnly Sem2End = new(2027, 12, 20);

    public EnrollmentExceptionsDialogTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    // ── Fixtures ───────────────────────────────────────────────────────────

    private static string ActivityGroupsJson(string span = "Termly") =>
        JsonSerializer.Serialize(new[]
        {
            new Dictionary<string, object?>
            {
                ["id"] = GroupId,
                ["name"] = "Robotics Club",
                ["description"] = (string?)null,
                ["category"] = (string?)null,
                ["capacity"] = (int?)null,
                ["isActive"] = true,
                ["span"] = span,
                ["enrollmentStartDate"] = (string?)null,
                ["enrollmentEndDate"] = (string?)null,
                ["autoRenewDefault"] = false,
                ["eligibleGradeIds"] = Array.Empty<Guid>(),
                ["activeMemberCount"] = 0,
                ["createdAt"] = DateTimeOffset.UnixEpoch,
                ["updatedAt"] = DateTimeOffset.UnixEpoch,
            },
        });

    private static Dictionary<string, object?> PeriodJson(
        Guid id, string name, Guid? parentPeriodId, string division, string start, string end,
        string status = "Active", int? sequence = null) => new()
    {
        ["id"] = id,
        ["name"] = name,
        ["startDate"] = start,
        ["endDate"] = end,
        ["status"] = status,
        ["parentPeriodId"] = parentPeriodId,
        ["nextPeriodId"] = (Guid?)null,
        ["division"] = division,
        ["activationToleranceDays"] = (int?)null,
        ["sequence"] = sequence,
        ["createdAt"] = DateTimeOffset.UnixEpoch,
        ["updatedAt"] = DateTimeOffset.UnixEpoch,
    };

    /// <summary>A tenant whose year is divided into terms: one undivided academic year, and
    /// Term 1 POSITIONED as 1 (v5 §0 decision 15) — the period a chosen "1st" fills the range
    /// from. Term 2/3/4 deliberately do NOT exist.</summary>
    private static string PeriodsWithTermJson() => JsonSerializer.Serialize(new[]
    {
        PeriodJson(YearId, "2027 Academic Year", null, "None", "2027-01-01", "2027-12-31"),
        PeriodJson(Term1Id, "Term 1", YearId, "Terms", "2027-03-01", "2027-06-30", sequence: 1),
    });

    /// <summary>A tenant whose year is divided into terms AND semesters, both positioned — the
    /// discriminator for "the body carries the CHOSEN part": the term and the semester sit at
    /// DIFFERENT positions, so a derivation from the dates could not produce this pair.</summary>
    private static string PeriodsWithSemesterJson() => JsonSerializer.Serialize(new[]
    {
        PeriodJson(YearId, "2027 Academic Year", null, "None", "2027-01-01", "2027-12-31"),
        PeriodJson(Term1Id, "Term 1", YearId, "Terms", "2027-03-01", "2027-06-30", sequence: 1),
        PeriodJson(Semester2Id, "Semester 2", YearId, "Semesters", "2027-07-01", "2027-12-20", sequence: 2),
    });

    /// <summary>A tenant with a 5TH term declared — the ladder must reach 5th, even though the
    /// row is about a term that is not the 5th.</summary>
    private static string PeriodsWithFifthTermJson() => JsonSerializer.Serialize(new[]
    {
        PeriodJson(YearId, "2027 Academic Year", null, "None", "2027-01-01", "2027-12-31"),
        PeriodJson(Term1Id, "Term 1", YearId, "Terms", "2027-03-01", "2027-06-30", sequence: 1),
        PeriodJson(Term5Id, "Term 5", YearId, "Terms", "2027-11-01", "2027-12-20", sequence: 5),
    });

    /// <summary>Two years that BOTH have a positioned Term 1 — the pick the dialog has to make
    /// deterministically: the active year's wins, not an arbitrary one.</summary>
    private static string PeriodsWithTwoYearsJson() => JsonSerializer.Serialize(new[]
    {
        PeriodJson(YearId, "2027 Academic Year", null, "None", "2027-01-01", "2027-12-31", status: "Completed"),
        PeriodJson(Term1Id, "Term 1", YearId, "Terms", "2027-03-01", "2027-06-30", status: "Completed", sequence: 1),
        PeriodJson(YearBId, "2028 Academic Year", null, "None", "2028-01-01", "2028-12-31", status: "Active"),
        PeriodJson(Term1BId, "Term 1", YearBId, "Terms", "2028-03-01", "2028-06-30", sequence: 1),
    });

    /// <summary>A tenant with plain, undivided years — the part picker must never offer an
    /// empty Term/Semester list.</summary>
    private static string PeriodsWithoutDivisionsJson() => JsonSerializer.Serialize(new[]
    {
        PeriodJson(YearId, "2027 Academic Year", null, "None", "2027-01-01", "2027-12-31"),
    });

    /// <summary>One enrollment exception row: a period PART plus the span the subject is NOT
    /// offered in, and the descriptive ordinal it was written as. <c>division</c> crosses the
    /// DTO boundary as its name (§2.1).</summary>
    private static Dictionary<string, object?> ExceptionJson(
        Guid id, Guid topicId, string division, DateOnly? start, DateOnly? end,
        string? reason = null, int? ordinal = null) => new()
    {
        ["id"] = id,
        ["gradeLevelId"] = GradeId,
        ["activityGroupId"] = (Guid?)null,
        ["topicId"] = topicId,
        ["division"] = division,
        ["startDate"] = start?.ToString("yyyy-MM-dd"),
        ["endDate"] = end?.ToString("yyyy-MM-dd"),
        ["reason"] = reason,
        ["ordinal"] = ordinal,
        ["createdAt"] = DateTimeOffset.UnixEpoch,
        ["updatedAt"] = DateTimeOffset.UnixEpoch,
    };

    /// <summary>One listed exception — the state the dialog opens in when its add form must be
    /// read-then-armed (F1) rather than offered straight away.</summary>
    private static string SeededExceptionJson() =>
        ExceptionsJson(ExceptionJson(Guid.NewGuid(), TopicId, "Terms", Term1Start, Term1End));

    private static string ExceptionsJson(params Dictionary<string, object?>[] rows) =>
        JsonSerializer.Serialize(rows);

    /// <summary>The grade-owned list read the dialog makes for its locked scope: the owner AND
    /// the subject, because every row it shows belongs to that one subject (D5).</summary>
    private static string GradeExceptionsUrl => $"{ExceptionsResource}?gradeLevelId={GradeId:D}&topicId={TopicId:D}";

    private static string GroupExceptionsUrl => $"{ExceptionsResource}?activityGroupId={GroupId:D}&topicId={TopicId:D}";

    // ── Harness ────────────────────────────────────────────────────────────

    /// <summary>
    /// Scripted HTTP stubs keyed on (method, url): exact matches win, a prefix map models the
    /// pickers' <c>/check</c> (whose query string the test should not have to reproduce character
    /// for character), and a dynamic map lets a test model a server-side mutation. HTTP is never
    /// mocked with Moq — the dialog gets a real <see cref="StudentsApiClient"/> over this stubbed
    /// handler. Unmapped routes 404, exactly as the real API does.
    /// </summary>
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public readonly List<(string Method, string Url, string? Body)> Calls = new();
        private readonly Dictionary<(string Method, string Url), (HttpStatusCode Status, string Body)> _responses = new();
        private readonly Dictionary<(string Method, string Url), (HttpStatusCode Status, Func<string> Body)> _dynamic = new();
        private readonly List<(string Method, string Url, HttpStatusCode Status, Func<string> Body)> _prefixes = new();

        public ScriptedHandler Map(string method, string url, HttpStatusCode status, string body)
        {
            _responses[(method.ToUpperInvariant(), url)] = (status, body);
            return this;
        }

        /// <summary>Answers every request whose url starts with <paramref name="urlPrefix"/>.</summary>
        public ScriptedHandler MapPrefix(string method, string urlPrefix, HttpStatusCode status, Func<string> body)
        {
            _prefixes.Add((method.ToUpperInvariant(), urlPrefix, status, body));
            return this;
        }

        /// <summary>An exact-match response whose body is produced on arrival.</summary>
        public ScriptedHandler MapDynamic(string method, string url, HttpStatusCode status, Func<string> body)
        {
            _dynamic[(method.ToUpperInvariant(), url)] = (status, body);
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var url = request.RequestUri!.PathAndQuery;
            var method = request.Method.Method.ToUpperInvariant();
            Calls.Add((request.Method.Method, url, body));

            if (_dynamic.TryGetValue((method, url), out var dynamicResponse))
            {
                return Json(dynamicResponse.Status, dynamicResponse.Body());
            }
            if (_responses.TryGetValue((method, url), out var exact))
            {
                return Json(exact.Status, exact.Body);
            }
            foreach (var prefix in _prefixes)
            {
                if (prefix.Method == method && url.StartsWith(prefix.Url, StringComparison.OrdinalIgnoreCase))
                {
                    return Json(prefix.Status, prefix.Body());
                }
            }

            return Json(HttpStatusCode.NotFound, $"{{\"message\":\"No route matches {request.Method.Method} {url}\"}}");
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private sealed class Harness
    {
        /// <summary>The dialog provider — the dialog's markup and components are queried from
        /// here, the way <c>DialogShellTests</c> does.</summary>
        public required IRenderedComponent<FluentDialogProvider> Dialog { get; init; }
        public required ScriptedHandler Handler { get; init; }
    }

    private void RegisterClient(ScriptedHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        var codedValues = new CodedValuesApiClient(http);
        Services.AddSingleton(codedValues);
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, codedValues));
    }

    /// <summary>
    /// Opens the dialog for the locked scope (<see cref="SubjectName"/> under the owner named by
    /// <paramref name="ownerType"/>) and returns the provider plus the handler. The scope travels
    /// on the MODEL — the dialog reads it and renders it read-only (D5) — so there is no query
    /// string, no owner toggle and no subject filter anywhere in this file.
    /// </summary>
    private async Task<Harness> RenderDialogAsync(
        string ownerType = "GradeLevel",
        string? exceptionsJson = null,
        string? periodsJson = null,
        HttpStatusCode addStatus = HttpStatusCode.Created,
        string? addBody = null,
        string checkJson = "{\"excepted\":false}",
        string groupSpan = "Termly")
    {
        var isGroup = ownerType == "ActivityGroup";

        var handler = new ScriptedHandler()
            .Map("GET", PeriodsUrl, HttpStatusCode.OK, periodsJson ?? PeriodsWithTermJson())
            .Map("GET", isGroup ? GroupExceptionsUrl : GradeExceptionsUrl, HttpStatusCode.OK, exceptionsJson ?? "[]")
            .MapPrefix("GET", $"{ExceptionsResource}/check", HttpStatusCode.OK, () => checkJson)
            // v6 §11.3: the dialog writes through the BULK route (one row per chosen sequence, one
            // transaction), and the client reads the created ids back.
            .Map("POST", $"{ExceptionsResource}/bulk", addStatus, addBody ?? $"{{\"ids\":[\"{Guid.NewGuid():D}\"]}}");

        if (isGroup)
        {
            handler.Map("GET", ActivityGroupsUrl, HttpStatusCode.OK, ActivityGroupsJson(groupSpan));
        }

        RegisterClient(handler);

        var provider = Render<FluentDialogProvider>();
        var model = new EnrollmentExceptionsDialog.EnrollmentExceptionsModel
        {
            OwnerType = ownerType,
            OwnerId = isGroup ? GroupId : GradeId,
            OwnerName = isGroup ? "Robotics Club" : "Grade 5",
            TopicId = TopicId,
            TopicName = SubjectName,
        };

        await Services.GetRequiredService<IDialogService>().ShowDialogAsync<
            EnrollmentExceptionsDialog,
            DialogShellData<EnrollmentExceptionsDialog.EnrollmentExceptionsModel>>(
            new DialogShellData<EnrollmentExceptionsDialog.EnrollmentExceptionsModel>(model),
            DialogServiceExtensions.BuildShellParameters($"Enrollment exceptions — {SubjectName}", DialogSize.Large));

        provider.WaitForAssertion(() => provider.FindAll(".exceptions-section").Should().NotBeEmpty(
            "the dialog renders its list region before any assertion can read it"));

        return new Harness { Dialog = provider, Handler = handler };
    }

    // ── Driving the add form ──────────────────────────────────────────────

    /// <summary>
    /// The add section's "Fill from" control — the CHOSEN period part (v5: decision 13
    /// reversed).
    /// </summary>
    private static FluentSelect<EnrollmentExceptionsDialog.DivisionOption> DivisionPicker(
        IRenderedComponent<FluentDialogProvider> dialog) =>
        dialog.FindComponent<FluentSelect<EnrollmentExceptionsDialog.DivisionOption>>().Instance;

    /// <summary>What the add section says the exception's part is.</summary>
    private static string DivisionLabel(IRenderedComponent<FluentDialogProvider> dialog) =>
        DivisionPicker(dialog).SelectedOption?.Label ?? "<no selection>";

    /// <summary>Chooses the part the exception will be expressed in, waiting for the tenant's
    /// periods to have widened the options first.</summary>
    private static Task SelectDivisionAsync(
        IRenderedComponent<FluentDialogProvider> dialog, AcademicYearDivision division)
    {
        EnrollmentExceptionsDialog.DivisionOption? option = null;
        dialog.WaitForAssertion(() =>
        {
            option = DivisionPicker(dialog).Items?.FirstOrDefault(o => o.Value == division);
            option.Should().NotBeNull($"the add section offers the {division} part it can write");
        });
        return dialog.InvokeAsync(() => DivisionPicker(dialog).SelectedOptionChanged.InvokeAsync(option));
    }

    /// <summary>
    /// The position choices (v6 §11.3: a MULTI-select — one FluentCheckbox per offered sequence), in
    /// DOM order. That order IS the ladder order (1st, 2nd, 3rd …), so the Nth box is the Nth
    /// sequence. The binding pair is Value/ValueChanged — <c>FluentCheckbox :
    /// FluentInputBase&lt;bool&gt;</c> — and NOT CheckState, which is the THREE-state parameter and
    /// throws unless ThreeState is true.
    /// </summary>
    private static IReadOnlyList<IRenderedComponent<FluentCheckbox>> PositionBoxes(
        IRenderedComponent<FluentDialogProvider> dialog) =>
        dialog.FindComponents<FluentCheckbox>();

    /// <summary>The positions ON OFFER, as the reader sees them — read off the rendered
    /// checkboxes rather than a bound list, because the option set IS the behaviour under test
    /// here (1st–4th plus any higher declared position).</summary>
    private static string[] PositionLabels(IRenderedComponent<FluentDialogProvider> dialog) =>
        [.. dialog.FindAll("fluent-checkbox").Select(r => r.TextContent.Trim())];

    /// <summary>Ticks the <paramref name="position"/>-th offered sequence (1-based) through its
    /// own ValueChanged — the same shape every other control in this file is driven with.</summary>
    private static Task ChoosePositionAsync(IRenderedComponent<FluentDialogProvider> dialog, int position) =>
        SetPositionAsync(dialog, position, ticked: true);

    /// <summary>Un-ticks that sequence. A checkbox is the one control in this section that can be
    /// un-picked, which is what lets a multi-select test drop one sequence without re-picking the
    /// part.</summary>
    private static Task UntickPositionAsync(IRenderedComponent<FluentDialogProvider> dialog, int position) =>
        SetPositionAsync(dialog, position, ticked: false);

    private static Task SetPositionAsync(IRenderedComponent<FluentDialogProvider> dialog, int position, bool ticked)
    {
        var boxes = PositionBoxes(dialog);
        var index = position - 1;
        index.Should().BeInRange(0, boxes.Count - 1,
            $"the part in force offers at least {position} sequence(s)");
        return dialog.InvokeAsync(() => boxes[index].Instance.ValueChanged.InvokeAsync(ticked));
    }

    /// <summary>Waits for the period list to have loaded, which is what puts positions on offer at
    /// all.</summary>
    private static void AwaitPositions(IRenderedComponent<FluentDialogProvider> dialog) =>
        dialog.WaitForAssertion(() => PositionLabels(dialog).Should().NotBeEmpty(
            "a real part puts the position choices on offer"));

    /// <summary>Drives the whole add section to a complete write: choose the Term part, and choose
    /// the 1st position — whose period EXISTS in the fixture, so the span is DERIVED and the range
    /// is hidden (v7).</summary>
    private static async Task FillAddSectionAsync(IRenderedComponent<FluentDialogProvider> dialog)
    {
        await SelectDivisionAsync(dialog, AcademicYearDivision.Terms);
        await ChoosePositionAsync(dialog, 1);
    }

    /// <summary>
    /// The add region's trigger (F2), or <c>null</c> when it is not offered — either because the
    /// list is EMPTY (nothing to read: the form is armed by construction) or because the form is
    /// already armed. It is found by its own visible text, which is why it reads
    /// "+ Add exception" while the footer's submit reads "Add exception".
    /// </summary>
    private static IElement? AddTrigger(IRenderedComponent<FluentDialogProvider> dialog) =>
        dialog.FindAll("fluent-button").FirstOrDefault(b => b.TextContent.Trim() == "+ Add exception");

    /// <summary>
    /// The add region's Reset (F7) — the ARMED form's own escape — or <c>null</c> while the form is
    /// gated. It is the trigger's exact inverse, so the two are never both here; an empty list (armed
    /// by construction, with nothing to cancel) renders no Reset either. Found by its own visible
    /// text, like the trigger.
    /// </summary>
    private static IElement? AddReset(IRenderedComponent<FluentDialogProvider> dialog) =>
        dialog.FindAll("fluent-button").FirstOrDefault(b => b.TextContent.Trim() == "Reset");

    /// <summary>
    /// Arms the add form when the dialog opened GATED (F1/F2). With a NON-EMPTY exception list the
    /// entry fields start disabled and the add region's heading row carries the trigger, so every
    /// test that drives the form against a seeded list has to click it first — arming is the
    /// reader's own first action there. A no-op on an empty list (already armed) and a no-op for a
    /// test that armed the form itself.
    /// </summary>
    private static void ArmAddForm(IRenderedComponent<FluentDialogProvider> dialog) =>
        AddTrigger(dialog)?.Click();

    /// <summary>
    /// The gate the entry fields share (F3), read off the controls the dialog BINDS it to: the four
    /// the free window shows — Fill from, both range ends and Reason. The controls never collapse,
    /// so they are always found; only their Disabled state differs — which is what makes
    /// <paramref name="gated"/> the one thing a caller has to say.
    /// </summary>
    private static void AssertFreeWindowFieldsGated(
        IRenderedComponent<FluentDialogProvider> dialog, bool gated)
    {
        var state = gated ? "gated behind the trigger" : "armed";
        DivisionPicker(dialog).Disabled.Should().Be(gated, $"Fill from is {state}");
        DatePicker(dialog, "exception-start-date").Disabled.Should().Be(gated, $"'Not offered from' is {state}");
        DatePicker(dialog, "exception-end-date").Disabled.Should().Be(gated, $"'Not offered to' is {state}");
        ReasonFieldComponent(dialog).Disabled.Should().Be(gated, $"Reason is {state}");
    }

    /// <summary>The reason input as a COMPONENT, so its own <c>Disabled</c> flag can be read. Found
    /// by its placeholder, because each <c>FluentDatePicker</c> renders an INTERNAL
    /// <c>FluentTextField</c> of its own.</summary>
    private static FluentTextField ReasonFieldComponent(IRenderedComponent<FluentDialogProvider> dialog) =>
        dialog.FindComponents<FluentTextField>().Single(f => f.Instance.Placeholder == "Why?").Instance;

    /// <summary>Types into the reason field through its own bound callback — the same shape every
    /// other control in this file is driven with.</summary>
    private static Task SetReasonAsync(IRenderedComponent<FluentDialogProvider> dialog, string value) =>
        dialog.InvokeAsync(() => ReasonFieldComponent(dialog).ValueChanged.InvokeAsync(value));

    /// <summary>
    /// The shell footer's submit button — the dialog's write affordance. It is
    /// <c>Type=Submit</c> with NO <c>@onclick</c>, so the form submit below is what fires
    /// <c>OnValidSubmit → HandleSubmitAsync → SubmitAsync</c>; this finder exists to read its
    /// DISABLED state, which is <see cref="EnrollmentExceptionsDialog.CanAdd"/> made visible
    /// through the footer's <c>SubmitDisabled</c>.
    /// </summary>
    private static IElement SubmitButton(IRenderedComponent<FluentDialogProvider> dialog) =>
        dialog.FindAll("fluent-button").Single(b => b.TextContent.Trim() == "Add exception");

    /// <summary>Submits the add form: the one form the dialog renders.</summary>
    private static void SubmitAdd(IRenderedComponent<FluentDialogProvider> dialog) =>
        dialog.Find("form").Submit();

    /// <summary>Drives a range end's bound value directly — the same bound-callback shape the
    /// selects are driven with. <c>null</c> opens that end, which v4 decision 12 expresses in the
    /// picker's own placeholder rather than with a Clear button.</summary>
    private static Task SetDateAsync(IRenderedComponent<FluentDialogProvider> dialog, string pickerId, DateTime? value) =>
        dialog.InvokeAsync(() => DatePicker(dialog, pickerId).ValueChanged.InvokeAsync(value));

    /// <summary>One of the two ends of the range, by its own <c>Id</c> — the dialog renders two,
    /// so "the first picker" is an assumption no test here should make.</summary>
    private static FluentDatePicker DatePicker(IRenderedComponent<FluentDialogProvider> dialog, string id) =>
        dialog.FindComponents<FluentDatePicker>().Single(p => p.Instance.Id == id).Instance;

    /// <summary>The add panel's one free-text field (the optional reason), found in the DOM by its
    /// placeholder. NOT by component type: each <c>FluentDatePicker</c> renders an INTERNAL
    /// <c>FluentTextField</c> of its own, so a <c>FindComponent&lt;FluentTextField&gt;</c> lookup returns a
    /// date picker's inner field rather than this one.</summary>
    private static IElement ReasonField(IRenderedComponent<FluentDialogProvider> dialog) =>
        dialog.Find("fluent-text-field[placeholder='Why?']");

    /// <summary>Asserts the element is a polite live region — the shape every state the dialog
    /// changes without a navigation has to carry.</summary>
    private static void AssertLiveRegion(IElement element)
    {
        element.GetAttribute("role").Should().Be("status");
        element.GetAttribute("aria-live").Should().Be("polite");
    }

    // ── AC-17: the list ────────────────────────────────────────────────────

    /// <summary>
    /// FR-ED-14 is dissolved: a subject with no exceptions is the NORMAL state, so the dialog says
    /// so in plain prose and raises no warning at all.
    /// </summary>
    [TestMethod]
    public async Task EmptyList_IsTheNormalState_WithNoWarning()
    {
        var harness = await RenderDialogAsync(exceptionsJson: "[]");

        harness.Dialog.WaitForAssertion(() => harness.Dialog.Markup.Should()
            .Contain("No exceptions for Mathematics — this subject is offered on every date."));

        harness.Dialog.FindComponents<FluentMessageBar>().Should().BeEmpty(
            "zero exceptions is the expected state, not a warning");
        harness.Dialog.FindAll(".exception-remove").Should().BeEmpty("there are no rows to remove");
        harness.Handler.Calls.Should().Contain(c => c.Method == "GET" && c.Url == GradeExceptionsUrl,
            "the dialog reads the locked scope's exceptions");
    }

    /// <summary>
    /// The two shapes an exception comes in: a term-scoped one (part + the term's dates) and a
    /// plain date window (no part). Both render their part AND their span, and the count badge
    /// counts ROWS — never a period list. The subject column is omitted: the dialog is already one
    /// subject (D5), so the name belongs to the heading, not to every row.
    /// </summary>
    [TestMethod]
    public async Task TermShapedAndPlainWindowRows_RenderTheirPartAndSpan()
    {
        var harness = await RenderDialogAsync(exceptionsJson: ExceptionsJson(
            ExceptionJson(Guid.NewGuid(), TopicId, "Terms", Term1Start, Term1End, reason: "Staffing"),
            ExceptionJson(Guid.NewGuid(), TopicId, "None", new DateOnly(2027, 5, 1), new DateOnly(2027, 5, 14))));

        harness.Dialog.WaitForAssertion(() => harness.Dialog.Markup.Should().Contain("1 Mar 2027 – 30 Jun 2027"));

        var markup = harness.Dialog.Markup;
        markup.Should().Contain("Mathematics", "the dialog names the subject its scope is locked to");
        markup.Should().Contain("Grade 5", "and the owner, as the scope's description line");
        harness.Dialog.FindAll(".exception-row fluent-badge").Select(b => b.TextContent.Trim()).Should()
            .BeEquivalentTo(new[] { "Term", "Any date" },
                "each row names its period part, and a date-only row says so in words");
        markup.Should().NotContain("Not term-scoped",
            "v3 spelled this concept two more ways on the same page; one spelling per concept");
        markup.Should().Contain("1–14 May 2027", "the plain window renders its own span");
        markup.Should().Contain("2 exceptions", "the badge counts exceptions, not periods");
        markup.Should().Contain("Staffing", "the optional reason is shown when one is set");
        harness.Dialog.FindAll(".exception-remove").Should().HaveCount(2);
    }

    /// <summary>
    /// Open bounds are first-class (§0 decision 8): a null start reads <c>from …</c> and a null end
    /// reads <c>to …</c> — a reader must never be shown an empty span.
    /// </summary>
    [TestMethod]
    public async Task OpenBounds_RenderFromAndTo()
    {
        var harness = await RenderDialogAsync(exceptionsJson: ExceptionsJson(
            ExceptionJson(Guid.NewGuid(), TopicId, "None", new DateOnly(2027, 6, 1), null),
            ExceptionJson(Guid.NewGuid(), TopicId, "None", null, new DateOnly(2027, 6, 30))));

        harness.Dialog.WaitForAssertion(() => harness.Dialog.Markup.Should().Contain("from 1 Jun 2027"));
        harness.Dialog.Markup.Should().Contain("to 30 Jun 2027");
    }

    /// <summary>
    /// Removing the LAST exception must work: there is no "the set may not be emptied" rule
    /// (FR-ED-14 dissolved) — the row is deleted on the server and the dialog falls back to the
    /// normal empty state.
    /// </summary>
    [TestMethod]
    public async Task RemovingTheLastException_Succeeds_AndReturnsToTheEmptyState()
    {
        var exceptionId = Guid.NewGuid();
        var harness = await RenderDialogAsync(exceptionsJson: ExceptionsJson(
            ExceptionJson(exceptionId, TopicId, "Terms", Term1Start, Term1End)));

        harness.Dialog.WaitForAssertion(() => harness.Dialog.FindAll(".exception-remove").Should().HaveCount(1));

        var removed = false;
        harness.Handler.MapDynamic("GET", GradeExceptionsUrl, HttpStatusCode.OK,
            () => removed ? "[]" : ExceptionsJson(ExceptionJson(exceptionId, TopicId, "Terms", Term1Start, Term1End)));
        harness.Handler.MapDynamic("DELETE", $"{ExceptionsResource}/{exceptionId:D}", HttpStatusCode.NoContent,
            () => { removed = true; return ""; });

        harness.Dialog.Find(".exception-remove").Click();

        harness.Dialog.WaitForAssertion(() => harness.Handler.Calls.Should()
            .Contain(c => c.Method == "DELETE" && c.Url == $"{ExceptionsResource}/{exceptionId:D}",
                "removing an exception reaches the API immediately"));
        harness.Dialog.WaitForAssertion(() => harness.Dialog.Markup.Should()
            .Contain("No exceptions for Mathematics — this subject is offered on every date."));
    }

    // ── AC-17: the add path ────────────────────────────────────────────────

    /// <summary>
    /// The write is immediate and carries exactly the chosen shape: owner form, subject, part and
    /// the resolved span — no period id anywhere (§0 decision 7). The list is then re-read so it
    /// shows the server's answer rather than a hopeful local append, and the dialog STAYS OPEN
    /// (D3: a management surface, not a single-shot form).
    /// </summary>
    [TestMethod]
    public async Task Add_WritesTheExceptionImmediately_AndRereadsTheList()
    {
        var createdId = Guid.NewGuid();
        var harness = await RenderDialogAsync(exceptionsJson: "[]");

        var live = new List<Dictionary<string, object?>>();
        harness.Handler.MapDynamic("GET", GradeExceptionsUrl, HttpStatusCode.OK, () => ExceptionsJson(live.ToArray()));
        harness.Handler.MapDynamic("POST", $"{ExceptionsResource}/bulk", HttpStatusCode.Created, () =>
        {
            live.Add(ExceptionJson(createdId, TopicId, "Terms", Term1Start, Term1End));
            return $"{{\"ids\":[\"{createdId:D}\"]}}";
        });

        await FillAddSectionAsync(harness.Dialog);

        harness.Dialog.WaitForAssertion(() => SubmitButton(harness.Dialog).GetAttribute("disabled").Should()
            .BeNull("a chosen part and a position whose span is derived is a complete write"));

        SubmitAdd(harness.Dialog);

        harness.Dialog.WaitForAssertion(() => harness.Handler.Calls.Should()
            .Contain(c => c.Method == "POST" && c.Url == $"{ExceptionsResource}/bulk"));

        var post = harness.Handler.Calls.Single(c => c.Method == "POST");
        post.Body.Should().Contain($"\"gradeLevelId\":\"{GradeId:D}\"");
        post.Body.Should().Contain("\"activityGroupId\":null", "a grade-owned exception names no group");
        post.Body.Should().Contain($"\"topicId\":\"{TopicId:D}\"");
        post.Body.Should().Contain("\"division\":1", "the chosen part travels as the Terms enum value");
        post.Body.Should().Contain("\"ordinal\":1", "the chosen position travels as the ordinal (v5 decision 15)");
        post.Body.Should().Contain($"\"startDate\":\"{Term1Start:yyyy-MM-dd}\"");
        post.Body.Should().Contain($"\"endDate\":\"{Term1End:yyyy-MM-dd}\"",
            "the chosen position resolved to that period's dates");
        post.Body.Should().NotContain("periodId", "the body carries a period PART, never a period instance");

        harness.Dialog.WaitForAssertion(() => harness.Dialog.Markup.Should()
            .Contain("1 Mar 2027 – 30 Jun 2027", "the list re-reads the server after the write"));
        harness.Dialog.Markup.Should().Contain("Enrollment exceptions",
            "and the dialog is still open — a management surface is not a single-shot form");
    }

    /// <summary>
    /// An unbound end reads as open IN WORDS, and the write affordance follows one rule: at least
    /// one bound, or there is nothing to write (neither bound means "never offered", a different
    /// concept, §2.2). v7 adds the other half: the range belongs to the FREE WINDOW, and the
    /// Position row replaces it for a real part.
    /// </summary>
    [TestMethod]
    public async Task OpenEnds_ReadAsAnyStartAndAnyDate_AndGateTheWrite()
    {
        var harness = await RenderDialogAsync(exceptionsJson: "[]");

        // The dialog lands on the free window, which is the ONE part that keeps the range: the open
        // state is NAMED, not implied by a missing value or a button's absence.
        DatePicker(harness.Dialog, "exception-start-date").Placeholder.Should().Be("(any start)");
        DatePicker(harness.Dialog, "exception-end-date").Placeholder.Should().Be("(any end)");
        harness.Dialog.FindAll(".clear-date").Should().BeEmpty(
            "v4 decision 12 removed the Clear buttons: an open end is the picker's own empty state");

        // No bound at all: nothing to write.
        SubmitButton(harness.Dialog).GetAttribute("disabled").Should().NotBeNull(
            "the range needs at least one bound");

        // Typing ONE end is enough: an open end is a legitimate span rather than a hole in one.
        await SetDateAsync(harness.Dialog, "exception-end-date", Term1End.ToDateTime(TimeOnly.MinValue));
        harness.Dialog.WaitForAssertion(() => SubmitButton(harness.Dialog).GetAttribute("disabled")
            .Should().BeNull("one bound is enough"));
        DatePicker(harness.Dialog, "exception-start-date").Value.Should().BeNull(
            "the open start stays open — decision 12's placeholder, not a Clear button");

        // A real part makes the range SILENT for the WHOLE part (v7): the Position row replaces it,
        // and a position with no period behind it renders but is DISABLED. The fixture has no
        // 4th-term period, so that box is the discriminator for the v7 rule.
        await SelectDivisionAsync(harness.Dialog, AcademicYearDivision.Terms);
        AwaitPositions(harness.Dialog);
        harness.Dialog.FindAll(".span-group").Should().BeEmpty(
            "every real part hides the range — the Position row is the only control left (v7)");
        PositionBoxes(harness.Dialog)[3].Instance.Disabled.Should().BeTrue(
            "the 4th term has no period, and the range is silent, so it could never produce a write");
        SubmitButton(harness.Dialog).GetAttribute("disabled").Should().NotBeNull(
            "and with no position ticked yet there is nothing to write");

        // The 1st term HAS a period, so choosing it derives the whole span and arms the write.
        await ChoosePositionAsync(harness.Dialog, 1);
        harness.Dialog.WaitForAssertion(() => SubmitButton(harness.Dialog).GetAttribute("disabled")
            .Should().BeNull("a sequence whose span is derived is a complete write"));
    }

    /// <summary>
    /// The server is the gate: a 409 (the same owner + subject + part + span already exists) must
    /// surface the SERVER's own message rather than being swallowed or replaced by a generic one —
    /// rendered by the shared footer's error bar (D3/D8).
    /// </summary>
    [TestMethod]
    public async Task DuplicateRejectedByTheServer_SurfacesTheServerMessage()
    {
        const string serverMessage = "This subject is already excepted for that part and span.";

        var harness = await RenderDialogAsync(
            exceptionsJson: "[]",
            addStatus: HttpStatusCode.Conflict,
            addBody: $"{{\"message\":\"{serverMessage}\"}}");

        await FillAddSectionAsync(harness.Dialog);
        SubmitAdd(harness.Dialog);

        harness.Dialog.WaitForAssertion(() => harness.Dialog.Markup.Should().Contain(serverMessage,
            "the server's 409 message reaches the user verbatim"));
        harness.Dialog.Markup.Should().NotContain("&quot;message&quot;",
            "the user reads the server's sentence, not the JSON envelope it arrived in");
        harness.Dialog.Markup.Should().NotContain("CreateSubjectEnrollmentExceptions failed",
            "the client's diagnostic prefix is not an answer to show a user");
    }

    // ── AC-17: the part picker, the position ladder ────────────────────────

    /// <summary>
    /// The part is CHOSEN (v5: decision 13 reversed), it is offered only where the tenant can write
    /// it, and there is no read-only badge to read it off. What does NOT change is decision 14's
    /// one-spelling rule — the control's labels come from the SAME helper the list rows use.
    /// </summary>
    [TestMethod]
    public async Task ChosenPart_IsAControl_OfferedOnlyWhereTheTenantHasPeriods()
    {
        var harness = await RenderDialogAsync(exceptionsJson: "[]");

        // The tenant's periods are the source: Terms has a period behind it, Semesters does not,
        // and a free window is always expressible.
        harness.Dialog.WaitForAssertion(() => DivisionPicker(harness.Dialog).Items!.Select(o => o.Label).Should()
            .BeEquivalentTo(new[] { "Term", "Any date" },
                "the divisions the tenant has periods FOR, plus the free window — never a part it cannot fill"));

        harness.Dialog.FindAll(".part-readonly").Should().BeEmpty(
            "v4's read-only part badge is gone: the part is a control now, so there is nothing to read it off");

        // Every add-section row is the house primitive with its label BENEATH its input — and the
        // POSITION row is not among them yet, because a free window has no position. Counted as
        // DESCENDANTS, not children: the range's two ends are label-below rows too, inside the group.
        harness.Dialog.FindAll(".add-form .form-row--label-below").Should().HaveCount(4,
            "Fill from / Not offered from / Not offered to / Reason, each with its label under its input");
        harness.Dialog.FindAll("fluent-checkbox").Should().BeEmpty("Any date has no position to choose");

        await SelectDivisionAsync(harness.Dialog, AcademicYearDivision.Terms);
        AwaitPositions(harness.Dialog);

        harness.Dialog.FindAll(".add-form .form-row--label-below").Should().HaveCount(3,
            "a real part swaps the two range ends for the position row (v7): Fill from / Position / Reason");
        harness.Dialog.FindAll("[aria-label='Position']").Should().HaveCount(1,
            "and the choices are a NAMED group of checkboxes, not anonymous ones");
    }

    /// <summary>
    /// The body carries the CHOSEN part and the CHOSEN position, not something derived from the
    /// range: a tenant whose term and semester sit at DIFFERENT positions is the discriminator,
    /// because no derivation from the filled dates could produce this pair.
    /// </summary>
    [TestMethod]
    public async Task Add_CarriesTheChosenPartAndPosition_ToTheServer()
    {
        var harness = await RenderDialogAsync(exceptionsJson: "[]", periodsJson: PeriodsWithSemesterJson());

        await SelectDivisionAsync(harness.Dialog, AcademicYearDivision.Semesters);
        await ChoosePositionAsync(harness.Dialog, 2);

        harness.Dialog.WaitForAssertion(() => SubmitButton(harness.Dialog).GetAttribute("disabled").Should()
            .BeNull("the 2nd semester exists, so the position supplies the whole range"));

        SubmitAdd(harness.Dialog);

        harness.Dialog.WaitForAssertion(() => harness.Handler.Calls.Should()
            .Contain(c => c.Method == "POST" && c.Url == $"{ExceptionsResource}/bulk"));

        var post = harness.Handler.Calls.Single(c => c.Method == "POST");
        post.Body.Should().Contain("\"division\":2", "the CHOSEN part travels — Semesters, as picked");
        post.Body.Should().Contain("\"ordinal\":2", "and the chosen position travels as the ordinal (§0 decision 15)");
        post.Body.Should().Contain($"\"startDate\":\"{Sem2Start:yyyy-MM-dd}\"");
        post.Body.Should().Contain($"\"endDate\":\"{Sem2End:yyyy-MM-dd}\"",
            "the 2nd semester's own period resolved the range");
    }

    /// <summary>
    /// A tenant with plain undivided years: no part it cannot write is ever on offer. The control
    /// offers exactly "Any date", and a free window has no position to choose at all.
    /// </summary>
    [TestMethod]
    public async Task ChosenPart_TenantWithoutDivisions_OffersAnyDateOnly_AndNoPosition()
    {
        var harness = await RenderDialogAsync(exceptionsJson: "[]", periodsJson: PeriodsWithoutDivisionsJson());

        harness.Dialog.WaitForAssertion(() => harness.Handler.Calls.Should()
            .Contain(c => c.Url == PeriodsUrl, "the period hierarchy is what decides which parts exist"));

        DivisionPicker(harness.Dialog).Items!.Select(o => o.Label).Should().BeEquivalentTo(new[] { "Any date" },
            "an undivided tenant has no term/semester period to fill from, so only the free window is offered");
        DivisionLabel(harness.Dialog).Should().Be("Any date");
        harness.Dialog.FindAll("fluent-checkbox").Should().BeEmpty(
            "a free window has no position in a run of terms or semesters, so none is offered — the server rejects an ordinal on one");
        harness.Dialog.Find(".add-panel").TextContent.Should().NotContain("Term",
            "and never the word of a part this tenant has no periods for");
    }

    /// <summary>
    /// FR-56 is still enforced server-side; the dialog enforces it at the control as well, and more
    /// bluntly: a Termly group is offered Terms and NOTHING else, so an invalid part cannot be
    /// selected. The positions the division permits are offered and do travel, which is the
    /// group-side half of the ordinal path — and the group's span is read from the group's own
    /// definition, because the call site supplies only its id and name.
    /// </summary>
    [TestMethod]
    public async Task ChosenPart_GroupOwned_TermlyGroup_OffersTermOnly()
    {
        var harness = await RenderDialogAsync(ownerType: "ActivityGroup", exceptionsJson: "[]");

        harness.Dialog.WaitForAssertion(() => DivisionPicker(harness.Dialog).Items!.Select(o => o.Label).Should()
            .BeEquivalentTo(new[] { "Term" },
                "FR-56 lets a Termly group write Terms and nothing else, so nothing else may be on offer"));
        DivisionLabel(harness.Dialog).Should().Be("Term", "and it is what the section is actually set to");
        harness.Handler.Calls.Should().Contain(c => c.Url == GroupExceptionsUrl,
            "the group owner lists the GROUP's exceptions");

        await ChoosePositionAsync(harness.Dialog, 1);
        harness.Dialog.WaitForAssertion(() => SubmitButton(harness.Dialog).GetAttribute("disabled").Should()
            .BeNull("the 1st term exists, so the group's only legal part writes a complete row"));

        SubmitAdd(harness.Dialog);

        harness.Dialog.WaitForAssertion(() => harness.Handler.Calls.Should()
            .Contain(c => c.Method == "POST" && c.Url == $"{ExceptionsResource}/bulk"));

        var post = harness.Handler.Calls.Single(c => c.Method == "POST");
        post.Body.Should().Contain($"\"activityGroupId\":\"{GroupId:D}\"");
        post.Body.Should().Contain("\"division\":1", "Terms — the only part FR-56 permits here");
        post.Body.Should().Contain("\"ordinal\":1");
    }

    /// <summary>
    /// The other half of FR-56: a group whose span is not period-aligned may store <c>None</c> only,
    /// so its control offers "Any date" and no other part at all — and therefore no position either,
    /// which is exactly the ordinal the server would reject on a free window.
    /// </summary>
    [TestMethod]
    public async Task ChosenPart_GroupOwned_UnAlignedSpan_OffersAnyDateOnly_AndNoPosition()
    {
        var harness = await RenderDialogAsync(
            ownerType: "ActivityGroup",
            exceptionsJson: "[]",
            groupSpan: "WholeAcademicYear");

        DivisionPicker(harness.Dialog).Items!.Select(o => o.Label).Should().BeEquivalentTo(new[] { "Any date" },
            "FR-56 lets a non-period-aligned group store None only, so no real part is on offer at all");
        harness.Dialog.FindAll("fluent-checkbox").Should().BeEmpty(
            "a free window carries no position — which is the ordinal the server would reject");
        harness.Dialog.Find(".add-panel").TextContent.Should().NotContain("Term",
            "the tenant's terms are unreachable from this owner, exactly as FR-56 requires");
    }

    /// <summary>
    /// The ladder RENDERS 1st–4th always, extended by any higher position the tenant has declared —
    /// here a 5th term — so a 5th appears the moment a tenant creates one, and its SHAPE never
    /// depends on which periods exist. The positions with no period behind them (2nd, 3rd, 4th here)
    /// render but are DISABLED, so the structural ladder stays visible without offering a choice
    /// that could never be written.
    /// </summary>
    [TestMethod]
    public async Task Positions_AreOneToFourPlusAnyHigherDeclared_NotOnlyTheExistingPeriods()
    {
        var harness = await RenderDialogAsync(exceptionsJson: "[]", periodsJson: PeriodsWithFifthTermJson());

        await SelectDivisionAsync(harness.Dialog, AcademicYearDivision.Terms);
        AwaitPositions(harness.Dialog);

        PositionLabels(harness.Dialog).Should().BeEquivalentTo(new[] { "1st", "2nd", "3rd", "4th", "5th" },
            "1st–4th always, plus the 5th the tenant declared — the shape is structural");

        PositionBoxes(harness.Dialog).Select(b => b.Instance.Disabled).Should()
            .BeEquivalentTo(new[] { false, true, true, true, false },
                "v7: only the 1st and 5th — the positions this fixture has a period for — are selectable");
    }

    /// <summary>
    /// The range is silent for EVERY real part, so a position the tenant has no period for could
    /// never satisfy the write guard — it renders (the ladder is structural) but is DISABLED, and the
    /// range stays hidden. Multi-select is preserved for the positions that DO have periods: ticking
    /// two of them writes one row per sequence in ONE request (§11.3 decision 18).
    /// </summary>
    [TestMethod]
    public async Task PositionsWithoutAPeriod_AreDisabled_AndTheRangeStaysHidden()
    {
        var harness = await RenderDialogAsync(exceptionsJson: "[]", periodsJson: PeriodsWithFifthTermJson());

        await SelectDivisionAsync(harness.Dialog, AcademicYearDivision.Terms);
        AwaitPositions(harness.Dialog);

        // 1st and 5th have periods; 2nd, 3rd and 4th do not, and cannot be chosen at all.
        PositionBoxes(harness.Dialog).Select(b => b.Instance.Disabled).Should()
            .BeEquivalentTo(new[] { false, true, true, true, false },
                "a position with no period behind it cannot supply a span, so it is not selectable");

        await ChoosePositionAsync(harness.Dialog, 1);
        harness.Dialog.WaitForAssertion(() => SubmitButton(harness.Dialog).GetAttribute("disabled")
            .Should().BeNull("a derived span is a complete write on the choice alone"));
        harness.Dialog.FindAll(".span-group").Should().BeEmpty(
            "the range is hidden for every real part — not shown read-only");

        // Multi-select still writes one row per chosen sequence: the 5th is this fixture's second
        // position WITH a period, and both ordinals must travel. Un-ticking is exercised first,
        // because a checkbox is the one control here that can be un-picked.
        await ChoosePositionAsync(harness.Dialog, 5);
        await UntickPositionAsync(harness.Dialog, 5);
        PositionBoxes(harness.Dialog).Count(b => b.Instance.Value).Should().Be(1,
            "un-ticking drops that sequence from the set without re-picking the part");
        await ChoosePositionAsync(harness.Dialog, 5);

        harness.Dialog.FindAll(".span-group").Should().BeEmpty(
            "two derived sequences need nothing typed either");
        SubmitAdd(harness.Dialog);

        harness.Dialog.WaitForAssertion(() => harness.Handler.Calls.Should()
            .Contain(c => c.Method == "POST" && c.Url == $"{ExceptionsResource}/bulk"));

        var post = harness.Handler.Calls.Single(c => c.Method == "POST");
        post.Body.Should().Contain("\"division\":1");
        post.Body.Should().Contain("\"ordinal\":1", "the first chosen sequence travels");
        post.Body.Should().Contain("\"ordinal\":5",
            "and the second — the selection is a SET written as one row per sequence, not a replacement");
        post.Body.Should().Contain($"\"startDate\":\"{Term1Start:yyyy-MM-dd}\"",
            "the 1st term's own period supplies that item's span");
    }

    /// <summary>
    /// A position is per year (§0 decision 15), so two years may each hold a "1st term" — which makes
    /// "which period fills the range from 1st?" a real question the dialog has to answer
    /// deterministically rather than arbitrarily. The ACTIVE year's wins: it is the year the reader
    /// is administrating.
    /// </summary>
    [TestMethod]
    public async Task Positions_TwoYearsBothTermOne_FillFromTheActiveYearsPeriod()
    {
        var harness = await RenderDialogAsync(exceptionsJson: "[]", periodsJson: PeriodsWithTwoYearsJson());

        await SelectDivisionAsync(harness.Dialog, AcademicYearDivision.Terms);
        await ChoosePositionAsync(harness.Dialog, 1);

        harness.Dialog.FindAll(".span-group").Should().BeEmpty(
            "the span is derived, so the range inputs are silent — not shown read-only");

        SubmitAdd(harness.Dialog);

        harness.Dialog.WaitForAssertion(() => harness.Handler.Calls.Should()
            .Contain(c => c.Method == "POST" && c.Url == $"{ExceptionsResource}/bulk"));

        var post = harness.Handler.Calls.Single(c => c.Method == "POST");
        post.Body.Should().Contain("\"startDate\":\"2028-03-01\"",
            "the ACTIVE year's Term 1, not the completed year's");
        post.Body.Should().Contain("\"endDate\":\"2028-06-30\"", "and that period's end");
    }

    /// <summary>
    /// A REJECTED write changes only what the server rejected. The scope survives — the dialog is
    /// locked to the subject the reader came for — and they still have the part and the sequence they
    /// chose, so the retry is one click rather than starting over. A failure must not also move them
    /// somewhere else.
    /// </summary>
    [TestMethod]
    public async Task FailedAdd_LeavesTheScopeUnchanged()
    {
        const string serverMessage = "This subject is already excepted for that part and span.";

        var harness = await RenderDialogAsync(
            exceptionsJson: ExceptionsJson(
                ExceptionJson(Guid.NewGuid(), TopicId, "Terms", Term1Start, Term1End, reason: "Staffing")),
            addStatus: HttpStatusCode.Conflict,
            addBody: $"{{\"message\":\"{serverMessage}\"}}");

        harness.Dialog.WaitForAssertion(() => harness.Dialog.FindAll(".exception-row").Should().HaveCount(1));

        // The list has a row, so the dialog opened GATED (F1) — arming the form is the reader's
        // first action here, and what this test is about is what a rejection leaves behind.
        ArmAddForm(harness.Dialog);

        await FillAddSectionAsync(harness.Dialog);
        SubmitAdd(harness.Dialog);

        harness.Dialog.WaitForAssertion(() => harness.Dialog.Markup.Should().Contain(serverMessage,
            "the rejected write surfaces the server's message"));
        harness.Dialog.Markup.Should().Contain("Mathematics",
            "a failed write is no reason to change the scope");
        harness.Dialog.FindAll(".exception-row").Should().HaveCount(1,
            "the list is still the one the user entered on");
        harness.Dialog.FindAll(".span-group").Should().BeEmpty(
            "the 1st term's span is DERIVED, so what survives the rejection is the CHOSEN SEQUENCE, "
            + "not a typed range");
        PositionBoxes(harness.Dialog).Should().Contain(b => b.Instance.Value,
            "and the choice survives a rejection, so the retry is one click, not seven fields");
    }

    /// <summary>
    /// The pickers' <c>/check</c> is a courtesy, not the gate: when the subject is already excepted
    /// on the span's anchor date the dialog says so before the write, and the server stays the
    /// authority (a duplicate is still a 409).
    /// </summary>
    [TestMethod]
    public async Task PickersCheck_ShowsTheHintWhenTheDateIsAlreadyExcepted()
    {
        var harness = await RenderDialogAsync(exceptionsJson: "[]", checkJson: "{\"excepted\":true}");

        await FillAddSectionAsync(harness.Dialog);

        harness.Dialog.WaitForAssertion(() => harness.Dialog.Markup.Should()
            .Contain("already excepted on 1 Mar 2027"));
        harness.Handler.Calls.Should().Contain(
            c => c.Method == "GET" && c.Url.StartsWith($"{ExceptionsResource}/check", StringComparison.Ordinal),
            "the check goes to the dedicated containment route");
    }

    // ── AC-17 (v8 amendment): the add form is gated behind an action ──────

    /// <summary>
    /// G1 — with something to READ, the form is gated: every entry field is disabled IN PLACE
    /// (F3: greyed, never collapsed) and the trigger is the only add affordance on offer, so it
    /// cannot compete with the footer's own <c>Add exception</c>. A grade owner opens on the free
    /// window, so this render is the one carrying the range.
    /// </summary>
    [TestMethod]
    public async Task NonEmptyList_GatesTheFreeWindowFields_AndOffersTheTrigger()
    {
        var harness = await RenderDialogAsync(exceptionsJson: SeededExceptionJson());

        harness.Dialog.WaitForAssertion(() => harness.Dialog.FindAll(".exception-row").Should().HaveCount(1));

        AddTrigger(harness.Dialog).Should().NotBeNull("a list to read is what puts the trigger on offer");
        AssertFreeWindowFieldsGated(harness.Dialog, gated: true);
        SubmitButton(harness.Dialog).GetAttribute("disabled").Should().NotBeNull(
            "and the footer's write affordance is disabled with the fields it would write");
    }

    /// <summary>
    /// G1, the position half — a group owner opens on the one part FR-56 permits, so THIS render is
    /// the one with the position ladder in it: while the form is gated every box is disabled,
    /// including the 1st, whose period the fixture HAS and which v7 on its own would leave
    /// selectable. That is the line between the two rules (F3): the gate disables the ladder, and an
    /// armed form still answers to <c>PositionIsDerivable</c>.
    /// </summary>
    [TestMethod]
    public async Task NonEmptyList_GatesThePositionLadder()
    {
        var harness = await RenderDialogAsync(ownerType: "ActivityGroup", exceptionsJson: SeededExceptionJson());

        harness.Dialog.WaitForAssertion(() => harness.Dialog.FindAll(".exception-row").Should().HaveCount(1));

        PositionLabels(harness.Dialog).Should().BeEquivalentTo(new[] { "1st", "2nd", "3rd", "4th" },
            "the structural ladder is still RENDERED while the form is gated — it is disabled, not hidden");
        PositionBoxes(harness.Dialog).Select(b => b.Instance.Disabled).Should()
            .BeEquivalentTo(new[] { true, true, true, true },
                "an unarmed form disables the whole ladder, not only the positions v7 would");
        DivisionPicker(harness.Dialog).Disabled.Should().BeTrue("Fill from is gated too");
        ReasonFieldComponent(harness.Dialog).Disabled.Should().BeTrue("and so is Reason");
        AddTrigger(harness.Dialog).Should().NotBeNull("the trigger is what arms them");
        SubmitButton(harness.Dialog).GetAttribute("disabled").Should().NotBeNull();
    }

    /// <summary>
    /// G2 — an EMPTY list is armed by construction: there is nothing to read and adding is the only
    /// thing left to do, so no trigger is rendered and the entry fields are live. This is the clause
    /// that makes deleting the last row re-arm the form with no special case (F1).
    /// </summary>
    [TestMethod]
    public async Task EmptyList_LeavesTheAddFormLive_AndOffersNoTrigger()
    {
        var harness = await RenderDialogAsync(exceptionsJson: "[]");

        harness.Dialog.WaitForAssertion(() => harness.Dialog.FindAll(".exceptions-empty").Should().NotBeEmpty());

        AddTrigger(harness.Dialog).Should().BeNull(
            "an empty list has no gate to lift — the form is already the only thing to do");
        AssertFreeWindowFieldsGated(harness.Dialog, gated: false);
    }

    /// <summary>
    /// G3 — the trigger ARMS the form and then goes away (F2): it is never on screen beside an armed
    /// form, and the armed form answers to the same write guard an empty list's does, so arming it is
    /// the whole difference.
    /// </summary>
    [TestMethod]
    public async Task Trigger_ArmsTheForm_AndThenDisappears()
    {
        var harness = await RenderDialogAsync(exceptionsJson: SeededExceptionJson());

        harness.Dialog.WaitForAssertion(() => harness.Dialog.FindAll(".exception-row").Should().HaveCount(1));
        var trigger = AddTrigger(harness.Dialog);
        trigger.Should().NotBeNull("the gated form offers the action that opens it");

        trigger!.Click();

        harness.Dialog.WaitForAssertion(() => AddTrigger(harness.Dialog).Should()
            .BeNull("the trigger is gone the moment the form it arms is on offer"));
        AssertFreeWindowFieldsGated(harness.Dialog, gated: false);

        await SetDateAsync(harness.Dialog, "exception-end-date", Term1End.ToDateTime(TimeOnly.MinValue));
        harness.Dialog.WaitForAssertion(() => SubmitButton(harness.Dialog).GetAttribute("disabled").Should()
            .BeNull("one bound is enough on an armed form, exactly as it is on an empty list"));
    }

    /// <summary>
    /// F7 — arming SWAPS the trigger for the Reset: exactly one of the two controls is ever in the
    /// DOM, and the Reset takes the trigger's own slot in the heading row, so the control does not
    /// move under the reader between the two states. A gated form offers no Reset: there is no
    /// pending add to cancel.
    /// </summary>
    [TestMethod]
    public async Task Reset_ReplacesTheTrigger_WhenTheFormIsArmed()
    {
        var harness = await RenderDialogAsync(exceptionsJson: SeededExceptionJson());

        harness.Dialog.WaitForAssertion(() => harness.Dialog.FindAll(".exception-row").Should().HaveCount(1));
        AddReset(harness.Dialog).Should().BeNull("a gated form has nothing to cancel — the trigger is the offer");

        AddTrigger(harness.Dialog)!.Click();

        harness.Dialog.WaitForAssertion(() => AddReset(harness.Dialog).Should()
            .NotBeNull("the armed form offers the escape it otherwise has none of"));
        AddTrigger(harness.Dialog).Should().BeNull("the trigger and the Reset are mutually exclusive in the DOM");
        harness.Dialog.Find(".add-head").QuerySelectorAll("fluent-button").Select(b => b.TextContent.Trim())
            .Should().Equal(new[] { "Reset" },
                "and the Reset sits in the heading row's own slot — the trigger's — so nothing moves "
                + "between the two states (the footer's Close is the only other button, and it is elsewhere)");
    }

    /// <summary>
    /// F7/H2, the free-window half — Reset returns BOTH typed range ends and the reason to their
    /// default and re-gates every field. The fields are moved off their defaults and asserted there
    /// BEFORE the click, which is what makes the clears below able to fail.
    ///
    /// <para>No division is asserted here: a real part REMOVES the range entirely
    /// (<c>SpanIsDerived</c>), so this render cannot both hold a typed range and be off "Any date" —
    /// the division half of the clear is asserted where it is observable, in
    /// <see cref="Reset_ClearsTheChosenPart_AndReGatesTheForm"/>.</para>
    /// </summary>
    [TestMethod]
    public async Task Reset_ClearsTheTypedRangeAndReason_WithoutWritingAnything()
    {
        var harness = await RenderDialogAsync(exceptionsJson: SeededExceptionJson());

        harness.Dialog.WaitForAssertion(() => harness.Dialog.FindAll(".exception-row").Should().HaveCount(1));
        ArmAddForm(harness.Dialog);

        var start = Term1Start.ToDateTime(TimeOnly.MinValue);
        var end = Term1End.ToDateTime(TimeOnly.MinValue);
        await SetDateAsync(harness.Dialog, "exception-start-date", start);
        await SetDateAsync(harness.Dialog, "exception-end-date", end);
        await SetReasonAsync(harness.Dialog, "Staffing");

        harness.Dialog.WaitForAssertion(() =>
        {
            DatePicker(harness.Dialog, "exception-start-date").Value.Should().Be(start,
                "the typed start is what the Reset has to consume");
            DatePicker(harness.Dialog, "exception-end-date").Value.Should().Be(end,
                "and so is the typed end");
            ReasonFieldComponent(harness.Dialog).Value.Should().Be("Staffing", "and the reason");
        });
        SubmitButton(harness.Dialog).GetAttribute("disabled").Should().BeNull(
            "the form is a complete, writable exception — the Reset cancels a genuine pending add");

        harness.Dialog.WaitForAssertion(() => harness.Handler.Calls
            .Count(c => c.Url.StartsWith($"{ExceptionsResource}/check", StringComparison.Ordinal)).Should()
            .Be(2, "one containment check per typed range end — both settled before the request count below"));
        var callsBeforeReset = harness.Handler.Calls.Count;

        AddReset(harness.Dialog)!.Click();

        harness.Dialog.WaitForAssertion(() =>
        {
            DatePicker(harness.Dialog, "exception-start-date").Value.Should().BeNull("Reset discarded the typed start");
            DatePicker(harness.Dialog, "exception-end-date").Value.Should().BeNull("and the typed end");
            ReasonFieldComponent(harness.Dialog).Value.Should().BeNullOrEmpty("and the reason");
        });

        harness.Handler.Calls.Should().NotContain(c => c.Method == "POST",
            "Reset writes nothing — in particular nothing reaches the bulk create route");
        harness.Handler.Calls.Should().HaveCount(callsBeforeReset,
            "and it issues no other request either: it discards in-progress input only");
        harness.Dialog.FindAll(".exceptions-section").Should().NotBeEmpty("the dialog stays OPEN — Reset is not Close");
        harness.Dialog.FindAll(".exception-row").Should().HaveCount(1, "and the list it was reading is untouched");
    }

    /// <summary>
    /// F7/H2–H3, the part half — the clause the free-window test cannot carry: a grade opens on
    /// <c>Any date</c> whether or not anything resets it, so the Reset's division reset is only
    /// observable from a render that has MOVED to a real part first. It also re-gates the form, so
    /// the trigger comes back and the footer's write affordance goes dead with the fields it would
    /// write.
    /// </summary>
    [TestMethod]
    public async Task Reset_ClearsTheChosenPart_AndReGatesTheForm()
    {
        var harness = await RenderDialogAsync(exceptionsJson: SeededExceptionJson());

        harness.Dialog.WaitForAssertion(() => harness.Dialog.FindAll(".exception-row").Should().HaveCount(1));
        ArmAddForm(harness.Dialog);
        await SelectDivisionAsync(harness.Dialog, AcademicYearDivision.Terms);
        await ChoosePositionAsync(harness.Dialog, 1);

        harness.Dialog.WaitForAssertion(() =>
        {
            DivisionLabel(harness.Dialog).Should().Be("Term",
                "the part is OFF its default before the Reset — without that move the reset below is unobservable");
            PositionBoxes(harness.Dialog)[0].Instance.Value.Should().BeTrue("and a sequence is ticked");
        });

        AddReset(harness.Dialog)!.Click();

        harness.Dialog.WaitForAssertion(() => DivisionLabel(harness.Dialog).Should().Be("Any date",
            "Reset returns the division to the default the dialog opens on"));
        PositionBoxes(harness.Dialog).Should().NotContain(b => b.Instance.Value,
            "and no ticked sequence survives it — 'Any date' offers no sequence at all");
        AssertFreeWindowFieldsGated(harness.Dialog, gated: true);
        AddReset(harness.Dialog).Should().BeNull("the Reset goes away with the armed state it belonged to");
        AddTrigger(harness.Dialog).Should().NotBeNull("and the trigger is back in its place");
        SubmitButton(harness.Dialog).GetAttribute("disabled").Should().NotBeNull(
            "with nothing left in the fields there is nothing to write");
        harness.Handler.Calls.Should().NotContain(c => c.Method == "POST", "a Reset is never a write");
    }

    /// <summary>
    /// F7/H2, the POSITION half, on the one render where the untick is observable at all: FR-56 leaves
    /// a group owner exactly one division, so the Reset cannot take the ladder off screen — what the
    /// reader sees is the tick itself coming off, with the part staying put.
    /// </summary>
    [TestMethod]
    public async Task Reset_UnTicksTheChosenSequence_WhereTheLadderSurvivesIt()
    {
        var harness = await RenderDialogAsync(ownerType: "ActivityGroup", exceptionsJson: SeededExceptionJson());

        harness.Dialog.WaitForAssertion(() => harness.Dialog.FindAll(".exception-row").Should().HaveCount(1));
        ArmAddForm(harness.Dialog);
        await ChoosePositionAsync(harness.Dialog, 1);

        harness.Dialog.WaitForAssertion(() => PositionBoxes(harness.Dialog)[0].Instance.Value.Should()
            .BeTrue("the 1st term is ticked before the Reset"));

        AddReset(harness.Dialog)!.Click();

        harness.Dialog.WaitForAssertion(() => PositionBoxes(harness.Dialog).Should().OnlyContain(b => !b.Instance.Value,
            "the ticked sequence was abandoned, not written"));
        PositionLabels(harness.Dialog).Should().BeEquivalentTo(new[] { "1st", "2nd", "3rd", "4th" },
            "the ladder is still RENDERED — for a group the part cannot move, so it is the tick that changes");
        DivisionLabel(harness.Dialog).Should().Be("Term", "and the part the group is allowed is still the one in force");
        harness.Handler.Calls.Should().NotContain(c => c.Method == "POST");
    }

    /// <summary>
    /// F7/H5 — an EMPTY list is armed by construction, so there is no pending add to cancel and no
    /// Reset is rendered. The form stays live and the heading row carries its name alone.
    /// </summary>
    [TestMethod]
    public async Task EmptyList_OffersNoReset()
    {
        var harness = await RenderDialogAsync(exceptionsJson: "[]");

        harness.Dialog.WaitForAssertion(() => harness.Dialog.FindAll(".exceptions-empty").Should().NotBeEmpty());

        AddReset(harness.Dialog).Should().BeNull(
            "the form is live by construction here — there is no add to abandon");
        AddTrigger(harness.Dialog).Should().BeNull("and no trigger either: nothing gates the form");
        AssertFreeWindowFieldsGated(harness.Dialog, gated: false);
        harness.Dialog.Find(".add-head").QuerySelectorAll("fluent-button").Should().BeEmpty(
            "the heading row names the region only while nothing can be cancelled");
    }

    /// <summary>
    /// F7/R5, the corner the fresh-open case cannot reach: the form can be ARMED while the list it was
    /// armed against is re-read EMPTY. The row's own Remove is gated by the busy state and by nothing
    /// else — not by the armed one — so removing the LAST row is reachable with the Reset in hand, and
    /// the reload it triggers leaves an EMPTY list. That list is live by construction (F1), so there is
    /// nothing left to cancel and the Reset goes with it: the form is the only thing on offer.
    /// </summary>
    [TestMethod]
    public async Task ArmedForm_LosesItsReset_WhenTheLastRowIsRemoved()
    {
        var exceptionId = Guid.NewGuid();
        var harness = await RenderDialogAsync(exceptionsJson: ExceptionsJson(
            ExceptionJson(exceptionId, TopicId, "Terms", Term1Start, Term1End)));

        harness.Dialog.WaitForAssertion(() => harness.Dialog.FindAll(".exception-remove").Should().HaveCount(1));

        var removed = false;
        harness.Handler.MapDynamic("GET", GradeExceptionsUrl, HttpStatusCode.OK,
            () => removed ? "[]" : ExceptionsJson(ExceptionJson(exceptionId, TopicId, "Terms", Term1Start, Term1End)));
        harness.Handler.MapDynamic("DELETE", $"{ExceptionsResource}/{exceptionId:D}", HttpStatusCode.NoContent,
            () => { removed = true; return ""; });

        ArmAddForm(harness.Dialog);
        harness.Dialog.WaitForAssertion(() => AddReset(harness.Dialog).Should()
            .NotBeNull("the armed form is what makes the Remove below reachable with a Reset in hand"));

        harness.Dialog.Find(".exception-remove").Click();

        harness.Dialog.WaitForAssertion(() => harness.Dialog.FindAll(".exceptions-empty").Should()
            .NotBeEmpty("the last row is gone and the dialog falls back to its empty state"));

        AddReset(harness.Dialog).Should().BeNull(
            "R5: an empty list has nothing to cancel — the armed form's Reset does not outlive the row set "
            + "it was armed against");
        AddTrigger(harness.Dialog).Should().BeNull("and an empty list offers no trigger either");
        AssertFreeWindowFieldsGated(harness.Dialog, gated: false);
    }

    /// <summary>
    /// G4, the free-window half of F5 — a successful add CLEARS the entry fields it consumed (both
    /// range ends, the reason), returns the division to the default the dialog opens on, and puts the
    /// form back behind the trigger. The reader's next move is then a decision rather than a stray
    /// half-filled form.
    /// </summary>
    [TestMethod]
    public async Task SuccessfulAdd_ClearsTheRangeAndReason_AndReGatesTheForm()
    {
        var seeded = ExceptionJson(Guid.NewGuid(), TopicId, "None", new DateOnly(2027, 5, 1), new DateOnly(2027, 5, 14));
        var harness = await RenderDialogAsync(exceptionsJson: ExceptionsJson(seeded));

        var live = new List<Dictionary<string, object?>> { seeded };
        harness.Handler.MapDynamic("GET", GradeExceptionsUrl, HttpStatusCode.OK,
            () => ExceptionsJson(live.ToArray()));
        harness.Handler.MapDynamic("POST", $"{ExceptionsResource}/bulk", HttpStatusCode.Created, () =>
        {
            live.Add(ExceptionJson(Guid.NewGuid(), TopicId, "None", Term1Start, Term1End, reason: "Staffing"));
            return $"{{\"ids\":[\"{Guid.NewGuid():D}\"]}}";
        });

        ArmAddForm(harness.Dialog);

        // A reason is not a span: the write guard still wants one bound, so the reason alone leaves
        // the footer disabled. Loading the form with BOTH is what makes the clearing below readable.
        await SetReasonAsync(harness.Dialog, "Staffing");
        SubmitButton(harness.Dialog).GetAttribute("disabled").Should().NotBeNull(
            "a reason without a bound is not a write");
        await SetDateAsync(harness.Dialog, "exception-end-date", Term1End.ToDateTime(TimeOnly.MinValue));

        harness.Dialog.WaitForAssertion(() => SubmitButton(harness.Dialog).GetAttribute("disabled")
            .Should().BeNull("the armed form is a complete write now"));
        SubmitAdd(harness.Dialog);

        harness.Dialog.WaitForAssertion(() => harness.Dialog.FindAll(".exception-row").Should()
            .HaveCount(2, "the list re-reads the server after the write"));

        harness.Dialog.WaitForAssertion(() => ReasonFieldComponent(harness.Dialog).Value.Should()
            .BeNullOrEmpty("the reason was consumed by the write"));
        DatePicker(harness.Dialog, "exception-end-date").Value.Should().BeNull("so was the typed end");
        DatePicker(harness.Dialog, "exception-start-date").Value.Should().BeNull("both ends are cleared, not one");
        DivisionLabel(harness.Dialog).Should().Be("Any date",
            "F5 returns the division to the default the dialog opens on");

        harness.Dialog.WaitForAssertion(() => AssertFreeWindowFieldsGated(harness.Dialog, gated: true));
        AddTrigger(harness.Dialog).Should().NotBeNull("and the form is behind the trigger again");
        SubmitButton(harness.Dialog).GetAttribute("disabled").Should().NotBeNull(
            "with nothing left in the fields there is nothing to write");
    }

    /// <summary>
    /// G4, the position half of F5 — a ticked sequence is consumed by the write. A GROUP owner is the
    /// render where that is observable at all: FR-56 leaves it exactly one division, so the reset
    /// cannot take the ladder off screen, and the cleared ticks are what the reader sees.
    /// </summary>
    [TestMethod]
    public async Task SuccessfulAdd_ClearsTheTickedPositions_AndReGatesTheForm()
    {
        var seeded = ExceptionJson(Guid.NewGuid(), TopicId, "Terms", Term1Start, Term1End);
        var harness = await RenderDialogAsync(ownerType: "ActivityGroup", exceptionsJson: ExceptionsJson(seeded));

        var live = new List<Dictionary<string, object?>> { seeded };
        harness.Handler.MapDynamic("GET", GroupExceptionsUrl, HttpStatusCode.OK,
            () => ExceptionsJson(live.ToArray()));
        harness.Handler.MapDynamic("POST", $"{ExceptionsResource}/bulk", HttpStatusCode.Created, () =>
        {
            live.Add(ExceptionJson(Guid.NewGuid(), TopicId, "Terms", Term1Start, Term1End, ordinal: 1));
            return $"{{\"ids\":[\"{Guid.NewGuid():D}\"]}}";
        });

        ArmAddForm(harness.Dialog);
        await ChoosePositionAsync(harness.Dialog, 1);

        harness.Dialog.WaitForAssertion(() => SubmitButton(harness.Dialog).GetAttribute("disabled")
            .Should().BeNull("the 1st term backs that sequence, so the write is complete"));
        SubmitAdd(harness.Dialog);

        harness.Dialog.WaitForAssertion(() => harness.Dialog.FindAll(".exception-row").Should().HaveCount(2));

        PositionBoxes(harness.Dialog).Should().OnlyContain(b => !b.Instance.Value,
            "the ticked sequence was consumed by the write");
        DivisionLabel(harness.Dialog).Should().Be("Term",
            "for a group the reset is a no-op: FR-56 leaves exactly one part to fall back to");
        harness.Dialog.WaitForAssertion(() => PositionBoxes(harness.Dialog).Select(b => b.Instance.Disabled)
            .Should().OnlyContain(disabled => disabled,
                "and the form is gated again, so even the position v7 backs is disabled"));
        AddTrigger(harness.Dialog).Should().NotBeNull();
        SubmitButton(harness.Dialog).GetAttribute("disabled").Should().NotBeNull();
    }

    /// <summary>
    /// G4, the DIVISION half of F5 — the clause the free-window test above cannot carry. A grade
    /// opens on the divisions its periods back PLUS <c>Any date</c>, and <c>Any date</c> is the one
    /// in force until something chooses otherwise, so a render that never leaves it reads
    /// <c>Any date</c> whether or not F5 resets anything. This test MOVES the part to a real one
    /// first and ticks a sequence behind it, which leaves the default reachable only through that
    /// reset — and, because the default part renders no ladder at all, leaves no ticked position
    /// behind either.
    /// </summary>
    [TestMethod]
    public async Task SuccessfulAdd_ReturnsTheDivisionToItsDefault_AndLeavesNoTickedPosition()
    {
        var seeded = ExceptionJson(Guid.NewGuid(), TopicId, "None", new DateOnly(2027, 5, 1), new DateOnly(2027, 5, 14));
        var harness = await RenderDialogAsync(exceptionsJson: ExceptionsJson(seeded));

        var live = new List<Dictionary<string, object?>> { seeded };
        harness.Handler.MapDynamic("GET", GradeExceptionsUrl, HttpStatusCode.OK,
            () => ExceptionsJson(live.ToArray()));
        harness.Handler.MapDynamic("POST", $"{ExceptionsResource}/bulk", HttpStatusCode.Created, () =>
        {
            live.Add(ExceptionJson(Guid.NewGuid(), TopicId, "Terms", Term1Start, Term1End, ordinal: 1));
            return $"{{\"ids\":[\"{Guid.NewGuid():D}\"]}}";
        });

        ArmAddForm(harness.Dialog);
        await SelectDivisionAsync(harness.Dialog, AcademicYearDivision.Terms);
        await ChoosePositionAsync(harness.Dialog, 1);

        harness.Dialog.WaitForAssertion(() =>
        {
            DivisionLabel(harness.Dialog).Should().Be("Term",
                "the part is OFF its default before the write — without that move the reset below is unobservable");
            PositionBoxes(harness.Dialog)[0].Instance.Value.Should().BeTrue("and the 1st sequence is ticked");
        });

        harness.Dialog.WaitForAssertion(() => SubmitButton(harness.Dialog).GetAttribute("disabled")
            .Should().BeNull("the 1st term backs that sequence, so the write is complete"));
        SubmitAdd(harness.Dialog);

        harness.Dialog.WaitForAssertion(() => harness.Dialog.FindAll(".exception-row").Should().HaveCount(2));

        DivisionLabel(harness.Dialog).Should().Be("Any date",
            "F5 returns the division to the default the dialog opens on");
        PositionBoxes(harness.Dialog).Should().NotContain(b => b.Instance.Value,
            "and no ticked sequence survives the write — 'Any date' offers no sequence at all");
    }

    /// <summary>
    /// G6 — the two regions are visually separated, and the separator is a CSS <c>border-top</c>
    /// (dialog-ui §2), never a <c>FluentDivider</c> or an <c>&lt;hr&gt;</c>; the add region's heading
    /// no longer repeats the subject, which the dialog's own scope title already names (F6).
    /// </summary>
    [TestMethod]
    public async Task AddRegion_CarriesTheSeparator_AndItsHeadingOmitsTheSubject()
    {
        var harness = await RenderDialogAsync(exceptionsJson: SeededExceptionJson());

        harness.Dialog.WaitForAssertion(() => harness.Dialog.FindAll(".exception-row").Should().HaveCount(1));

        harness.Dialog.FindAll(".exceptions-list").Should().HaveCount(1, "the list is one region");
        harness.Dialog.FindAll(".add-panel").Should().HaveCount(1, "and the add form is the other");
        harness.Dialog.FindAll("fluent-divider").Should().BeEmpty(
            "the separator is a CSS border-top, never a FluentDivider");
        harness.Dialog.FindAll("hr").Should().BeEmpty("and never an <hr>");

        CssRuleFor(".add-panel").Should().Contain("border-top",
            "the add region's separator is .add-panel's own border-top (dialog-ui §2)");
        CssRuleFor(".add-panel").Should().Contain("padding-top",
            "and it is spaced off the list it is separated from");

        var heading = harness.Dialog.Find(".add-title").TextContent.Trim();
        heading.Should().Be("Add exception", "the heading names the region");
        heading.Should().NotContain(SubjectName,
            "the subject is the dialog's title and its list heading — repeating it here says nothing");
    }

    // ── Reading the dialog's own source (scoped CSS is not in the render tree) ──

    /// <summary>
    /// Reads a repo file by its repo-relative path: the test assembly runs from
    /// tests/&lt;project&gt;/bin/&lt;config&gt;/&lt;tfm&gt;, so five levels up is the repo root — the
    /// same resolution <c>EnrollStudentDialogFeatureFlagTests</c> uses.
    /// </summary>
    private static string Load(string repoRelativePath)
    {
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var fullPath = Path.GetFullPath(Path.Combine(
            asmDir, "..", "..", "..", "..", "..", repoRelativePath));
        File.Exists(fullPath).Should().BeTrue(
            $"{repoRelativePath} should exist at '{fullPath}' — check the path resolution");
        return File.ReadAllText(fullPath);
    }

    /// <summary>
    /// One scoped-CSS rule, from its selector to its closing brace. A separator drawn in CSS has no
    /// markup-level footprint, and the build does not warn when a rule loses its element
    /// (dialog-ui §3), so this is the only place the convention can be asserted.
    /// </summary>
    private static string CssRuleFor(string selector)
    {
        var css = Load(
            "src/Students/SchoolCollab.Students.Application/Components/Students/EnrollmentExceptionsDialog.razor.css");
        var start = css.IndexOf($"{selector} {{", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"{selector} should have a rule in the dialog's scoped CSS");
        return css[start..css.IndexOf('}', start)];
    }

    /// <summary>
    /// One method's source, from a marker inside it to the first closing brace at method
    /// indentation. This is the only way to assert WHERE a behaviour lives — that the post-add clear
    /// and the Reset's clear are ONE implementation rather than two copies that can drift.
    /// </summary>
    private static string MethodSource(string source, string marker)
    {
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"`{marker}` should appear in the dialog's source");
        var end = source.IndexOf("\n    }", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, $"`{marker}` should sit in a method body that closes at method indentation");
        return source[start..end];
    }

    /// <summary>
    /// F7/H7 — the clear exists ONCE. The write that consumed the entry fields and the Reset that
    /// abandons them both call <c>ClearAddForm()</c>, and the two statements a PARTIAL copy is most
    /// likely to carry — the untick and the reason — appear nowhere but inside it: the reason is
    /// counted across the whole component, and each caller's own body is ruled out for the untick.
    /// A copy that inlines EITHER of those two fails here; the clear's remaining statements are
    /// pinned behaviourally (H2/H3), not by whole-file text.
    /// </summary>
    [TestMethod]
    public void Reset_AndTheSuccessPath_ShareTheOneClearImplementation()
    {
        var source = Load(
            "src/Students/SchoolCollab.Students.Application/Components/Students/EnrollmentExceptionsDialog.razor");

        MethodSource(source, "private void ClearAddForm()").Should()
            .Contain("_positions.Clear()").And.Contain("_startDate = null").And.Contain("_endDate = null")
            .And.Contain("_reason = null").And.Contain("_chosenDivision = AcademicYearDivision.None");

        MethodSource(source, "private void OnAddReset()").Should().Contain("ClearAddForm()",
                "Reset abandons the entry fields through the ONE clear")
            .And.NotContain("_positions.Clear()", "and carries none of the clear's statements itself");
        MethodSource(source, "await Api.CreateSubjectEnrollmentExceptionsAsync").Should()
            .Contain("ClearAddForm()", "and the write that consumed them calls the same method, not a copy")
            .And.NotContain("_reason = null", "which is why the success path no longer carries the statements")
            .And.NotContain("_positions.Clear()", "and none of the others either");

        source.Split("_reason = null;").Should().HaveCount(2,
            "the reason is cleared in exactly one place in the whole component: the shared method");
    }

    // ── Accessibility: the states that change under the reader ────────────

    /// <summary>
    /// The prose that changes under the reader's feet is announced, not merely redrawn, and the check
    /// transfers to the group that names the range: the ends it labels must sit inside it, each
    /// carrying its own label beneath its input, and the openness must be named in the placeholders
    /// rather than left to a button's absence.
    /// </summary>
    [TestMethod]
    public async Task ScopedEmptyState_AndRangeGroup_CarryTheirAccessibleNames()
    {
        var harness = await RenderDialogAsync(exceptionsJson: "[]");

        harness.Dialog.WaitForAssertion(() => harness.Dialog.Markup.Should()
            .Contain("No exceptions for Mathematics — this subject is offered on every date."));

        AssertLiveRegion(harness.Dialog.Find(".exceptions-empty"));

        harness.Dialog.FindAll("[aria-label='Position']").Should().BeEmpty("Any date has no position to name");
        var range = harness.Dialog.Find(".span-group");
        range.GetAttribute("role").Should().Be("group");
        range.GetAttribute("aria-label").Should().Be("Not offered",
            "the group names the range its two ends belong to — and says what the range means");
        range.QuerySelectorAll(".form-row").Length.Should().Be(2,
            "both ends are members of the named group");
        range.QuerySelectorAll(".form-row-label").Select(l => l.TextContent.Trim()).Should()
            .BeEquivalentTo(new[] { "Not offered from", "Not offered to" },
                "v5.3: the range's header is COMBINED into each end's label — one label line "
                + "instead of two — and each sits BENEATH its own input, like every other label here");
        range.QuerySelectorAll(".clear-date").Should().BeEmpty(
            "v4 decision 12: an open end is the placeholder, not a button's absence");

        // The add section is the house primitive with every label BENEATH its input, and the part its
        // rows name is the same vocabulary the LIST rows use.
        harness.Dialog.FindAll(".add-form .form-row-label").Select(l => l.TextContent.Trim()).Should()
            .BeEquivalentTo(new[] { "Fill from", "Not offered from", "Not offered to", "Reason (optional)" },
                "the add section is Fill from / the two range ends / Reason before a real part is "
                + "chosen — and the range's own header is combined into its ends, not a third label");
        DivisionLabel(harness.Dialog).Should().Be("Any date",
            "and the part is spelled the same way the list rows spell it — one vocabulary, two surfaces");
    }
}
