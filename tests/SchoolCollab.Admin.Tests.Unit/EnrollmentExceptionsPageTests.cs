using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Core.Features;
using SchoolCollab.Students.Application.Components.Pages.Students;
using SchoolCollab.Students.Application.Services;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for the <c>EnrollmentExceptions</c> management page
/// (<c>/students/enrollment-exceptions</c>, subject-period-exception-model.md v5 §5.1)
/// — AC-17.
///
/// <para>The page is the ONLY place an exception is edited, so these tests pin what a
/// management surface must not get wrong: zero exceptions is the normal state (no
/// warning), a term-shaped and a plain-window row both render their part and their span,
/// open bounds read <c>from …</c> / <c>to …</c>, the last exception stays removable, an
/// incomplete add writes NOTHING, the page offers no Save control at all (writes are
/// immediate), a server 409 surfaces the server's own message, the group owner side is
/// gated by <c>FEATURE:EnableActivityGroups</c>, the add section's period PART is CHOSEN
/// as a division and only where the tenant can write it (v5: decision 13 reversed), the
/// POSITION is structural rather than a period pick (Q1/Q5), and the entry point's query
/// string lands with owner and subject pre-selected (§5.2 row 1 / N4) while the page's own
/// landing pre-selects NOTHING (Q4).</para>
///
/// <para>Rendered through <see cref="Router"/>, not <c>Render&lt;T&gt;</c>: the page reads
/// its pre-selection from <c>[SupplyParameterFromQuery]</c> properties, which the
/// framework supplies via the router's query-parameter supplier — a directly rendered
/// component receives no query string at all, so the N4 assertions need the router. The
/// <c>AppAssembly</c> is the page's own assembly (Students.Application) and <c>Found</c>
/// opens the page component directly, without a layout.</para>
/// </summary>
[TestClass]
public class EnrollmentExceptionsPageTests : BunitContext
{
    private const string GradeLevelsUrl = "/students/grade-levels";
    private const string ActivityGroupsUrl = "/activity-groups";
    private const string PeriodsUrl = "/students/periods";
    private const string ExceptionsUrl = "/students/enrollment-exceptions";
    private const string PageUrl = "/students/enrollment-exceptions";

    private static readonly Guid GradeId = Guid.Parse("5b000000-0000-0000-0000-000000000001");
    private static readonly Guid GradeBId = Guid.Parse("5b000000-0000-0000-0000-000000000006");
    private static readonly Guid GroupId = Guid.Parse("5b000000-0000-0000-0000-000000000002");
    private static readonly Guid TopicId = Guid.Parse("5b000000-0000-0000-0000-000000000003");
    private static readonly Guid TopicBId = Guid.Parse("5b000000-0000-0000-0000-000000000007");
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

    public EnrollmentExceptionsPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    // ── Fixtures ───────────────────────────────────────────────────────────

    private static string GradeLevelsJson() =>
        JsonSerializer.Serialize(new[]
        {
            GradeLevelJson(GradeId, 5, "Grade 5"),
            GradeLevelJson(GradeBId, 6, "Grade 6"),
        });

    private static Dictionary<string, object?> GradeLevelJson(Guid id, int level, string name) => new()
    {
        ["id"] = id,
        ["codedValueId"] = Guid.NewGuid(),
        ["level"] = level,
        ["name"] = name,
        ["displayOrder"] = level,
        ["topicCount"] = 1,
        ["studentCount"] = 0,
        ["createdAt"] = DateTimeOffset.UnixEpoch,
        ["updatedAt"] = DateTimeOffset.UnixEpoch,
    };

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

    private static string SubjectsJson() =>
        JsonSerializer.Serialize(new[]
        {
            SubjectJson(TopicId, "MATH", "Mathematics"),
        });

    /// <summary>Grade 5's subjects when a test needs TWO — the subject filter's
    /// discriminator: a filtered list has to be a strict subset of the owner's rows, which
    /// a one-subject fixture cannot show.</summary>
    private static string TwoSubjectsJson() =>
        JsonSerializer.Serialize(new[]
        {
            SubjectJson(TopicId, "MATH", "Mathematics"),
            SubjectJson(TopicBId, "BIO", "Biology"),
        });

    /// <summary>Grade 6's subjects — deliberately NOT the grade-5 subject, so a query
    /// change to this owner is a topic the PREVIOUS owner's list did not contain (the
    /// P2-1 case).</summary>
    private static string SubjectsBJson() =>
        JsonSerializer.Serialize(new[]
        {
            SubjectJson(TopicBId, "BIO", "Biology"),
        });

    private static Dictionary<string, object?> SubjectJson(Guid id, string code, string name) => new()
    {
        ["id"] = id,
        ["codedValueId"] = Guid.NewGuid(),
        ["code"] = code,
        ["name"] = name,
        ["displayOrder"] = 0,
        ["isOverridden"] = false,
        ["createdAt"] = DateTimeOffset.UnixEpoch,
        ["updatedAt"] = DateTimeOffset.UnixEpoch,
    };

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
    /// from. Term 2/3/4 deliberately do NOT exist, which is what Q1 is about.</summary>
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

    /// <summary>A tenant with a 5TH term declared — Q5's extension case: the position ladder
    /// must reach 5th, even though the row is about a term that is not the 5th.</summary>
    private static string PeriodsWithFifthTermJson() => JsonSerializer.Serialize(new[]
    {
        PeriodJson(YearId, "2027 Academic Year", null, "None", "2027-01-01", "2027-12-31"),
        PeriodJson(Term1Id, "Term 1", YearId, "Terms", "2027-03-01", "2027-06-30", sequence: 1),
        PeriodJson(Term5Id, "Term 5", YearId, "Terms", "2027-11-01", "2027-12-20", sequence: 5),
    });

    /// <summary>Two years that BOTH have a positioned Term 1 — the pick the page has to make
    /// deterministically: the active year's wins, not an arbitrary one.</summary>
    private static string PeriodsWithTwoYearsJson() => JsonSerializer.Serialize(new[]
    {
        PeriodJson(YearId, "2027 Academic Year", null, "None", "2027-01-01", "2027-12-31", status: "Completed"),
        PeriodJson(Term1Id, "Term 1", YearId, "Terms", "2027-03-01", "2027-06-30", status: "Completed", sequence: 1),
        PeriodJson(YearBId, "2028 Academic Year", null, "None", "2028-01-01", "2028-12-31", status: "Active"),
        PeriodJson(Term1BId, "Term 1", YearBId, "Terms", "2028-03-01", "2028-06-30", sequence: 1),
    });

    /// <summary>A tenant with plain, undivided years — OOD-1's case: the part picker must
    /// never offer an empty Term/Semester list.</summary>
    private static string PeriodsWithoutDivisionsJson() => JsonSerializer.Serialize(new[]
    {
        PeriodJson(YearId, "2027 Academic Year", null, "None", "2027-01-01", "2027-12-31"),
    });

    /// <summary>One enrollment exception row: a period PART plus the span the subject is
    /// NOT offered in, and the descriptive ordinal it was written as (v5 §0 decision 15).
    /// <c>division</c> crosses the DTO boundary as its name (§2.1).</summary>
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

    private static string ExceptionsJson(params Dictionary<string, object?>[] rows) =>
        JsonSerializer.Serialize(rows);

    /// <summary>One exception row for EACH of <see cref="TopicId"/> and
    /// <see cref="TopicBId"/> under the SAME owner, so a filter that does not bite is
    /// visible as a second row (and the count badge is never 1 by accident).</summary>
    private static string TwoTopicExceptionsJson() => ExceptionsJson(
        ExceptionJson(Guid.NewGuid(), TopicId, "Terms", Term1Start, Term1End, reason: "Staffing"),
        ExceptionJson(Guid.NewGuid(), TopicBId, "None", new DateOnly(2027, 5, 1), new DateOnly(2027, 5, 14)));

    private static string GradeExceptionsUrl => $"{ExceptionsUrl}?gradeLevelId={GradeId:D}";

    /// <summary>
    /// The query string an entry point actually sends (spec §5.2 row 1): the OWNER, and
    /// usually the subject too. Q4 removed the page's own grade-level pre-selection — the owner
    /// KIND is the only thing selected on arrival — so every test that needs a scope has to name
    /// one, exactly as a landing-page kebab does.
    /// </summary>
    private static string GradeOwnerQuery => $"?gradeLevelId={GradeId:D}";

    private static string GroupExceptionsUrl => $"{ExceptionsUrl}?activityGroupId={GroupId:D}";

    // ── Harness ────────────────────────────────────────────────────────────

    /// <summary>
    /// Scripted HTTP stubs keyed on (method, url): exact matches win, a prefix map models
    /// the pickers' <c>/check</c> (whose query string the test should not have to
    /// reproduce character for character), and a dynamic map lets a test model a
    /// server-side mutation. HTTP is never mocked with Moq — the page gets a real
    /// <see cref="StudentsApiClient"/> over this stubbed handler. Unmapped routes 404,
    /// exactly as the real API does for a route group that a feature flag has not mapped.
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
        public required IRenderedComponent<EnrollmentExceptions> Page { get; init; }
        public required ScriptedHandler Handler { get; init; }
    }

    private static AuthenticationStateProvider AuthProvider()
    {
        var claims = new[]
        {
            new Claim("tenant_id", Guid.NewGuid().ToString()),
            new Claim("tenant_name", "Hydeson"),
        };
        var provider = new Mock<AuthenticationStateProvider>();
        provider.Setup(p => p.GetAuthenticationStateAsync())
                .Returns(Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(claims, "TestScheme")))));
        return provider.Object;
    }

    private void RegisterClient(ScriptedHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        var codedValues = new CodedValuesApiClient(http);
        Services.AddSingleton(codedValues);
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, codedValues));
        Services.AddSingleton(new VisibleTenantService(AuthProvider(), NullLogger<VisibleTenantService>.Instance));
    }

    private void RegisterFlags(bool activityGroupsEnabled)
    {
        var flags = new Mock<IFeatureFlagService>();
        flags.Setup(f => f.IsEnabledAsync(FeatureFlagKeys.EnableActivityGroups, It.IsAny<CancellationToken>()))
             .ReturnsAsync(activityGroupsEnabled);
        flags.Setup(f => f.IsEnabled(FeatureFlagKeys.EnableActivityGroups))
             .Returns(activityGroupsEnabled);
        Services.AddSingleton(flags.Object);
    }

    /// <summary>
    /// Renders the page through the router at <paramref name="query"/> and returns the
    /// page component.
    /// </summary>
    private Harness RenderPage(
        string query = "",
        bool activityGroupsEnabled = false,
        string? exceptionsJson = null,
        string? periodsJson = null,
        HttpStatusCode addStatus = HttpStatusCode.Created,
        string? addBody = null,
        string checkJson = "{\"excepted\":false}",
        string groupSpan = "Termly",
        string? gradeSubjectsJson = null)
    {
        var handler = new ScriptedHandler()
            .Map("GET", GradeLevelsUrl, HttpStatusCode.OK, GradeLevelsJson())
            .Map("GET", PeriodsUrl, HttpStatusCode.OK, periodsJson ?? PeriodsWithTermJson())
            .Map("GET", $"/students/subjects/by-grade/{GradeId}", HttpStatusCode.OK, gradeSubjectsJson ?? SubjectsJson())
            .Map("GET", $"/students/subjects/by-grade/{GradeBId}", HttpStatusCode.OK, SubjectsBJson())
            .Map("GET", GradeExceptionsUrl, HttpStatusCode.OK, exceptionsJson ?? "[]")
            .Map("GET", $"{ExceptionsUrl}?gradeLevelId={GradeBId:D}", HttpStatusCode.OK, "[]")
            .MapPrefix("GET", $"{ExceptionsUrl}/check", HttpStatusCode.OK, () => checkJson)
            // v6 §11.3: the page writes through the BULK route (one row per chosen sequence, one
            // transaction), and the client reads the created ids back. A canned response on the
            // old single-item URL would match nothing, which is not a failure the page can
            // distinguish from a server error — hence the exact URL and the ids shape here.
            .Map("POST", $"{ExceptionsUrl}/bulk", addStatus, addBody ?? $"{{\"ids\":[\"{Guid.NewGuid():D}\"]}}");

        if (activityGroupsEnabled)
        {
            handler
                .Map("GET", ActivityGroupsUrl, HttpStatusCode.OK, ActivityGroupsJson(groupSpan))
                .Map("GET", $"/students/subjects/by-group/{GroupId}", HttpStatusCode.OK, SubjectsJson())
                .Map("GET", GroupExceptionsUrl, HttpStatusCode.OK, "[]");
        }

        RegisterFlags(activityGroupsEnabled);
        RegisterClient(handler);

        Services.GetRequiredService<NavigationManager>().NavigateTo($"{PageUrl}{query}");

        var router = Render<Router>(p => p
            .Add(r => r.AppAssembly, typeof(EnrollmentExceptions).Assembly)
            .Add(r => r.Found, (RouteData routeData) => builder =>
            {
                builder.OpenComponent(0, routeData.PageType);
                builder.CloseComponent();
            })
            .Add(r => r.NotFound, (RenderFragment)(builder =>
                builder.AddMarkupContent(0, "<p id='page-not-found'>no route</p>"))));

        return new Harness
        {
            Page = router.FindComponent<EnrollmentExceptions>(),
            Handler = handler,
        };
    }

    // ── Driving the filter and the range ──────────────────────────────────

    /// <summary>
    /// Picks an option on a FluentUI select. The controls are web components whose options
    /// are NOT materialised in markup while closed, so selection is driven through the
    /// component's own bound callback — the same shape the v1 exceptions-dialog tests
    /// used. <c>InvokeAsync</c> puts the change on the renderer, which is also what makes
    /// the page's <c>:after</c> side-effects run.
    /// </summary>
    private static Task SelectAsync<TItem>(IRenderedComponent<EnrollmentExceptions> page, TItem item)
        where TItem : class
        => page.InvokeAsync(() => page.FindComponent<FluentSelect<TItem>>()
            .Instance.SelectedOptionChanged.InvokeAsync(item));

    /// <summary>
    /// Waits until the owner's subjects have loaded — the SUBJECT FILTER's readiness signal
    /// (the list may legitimately be empty). Asserted on the bound items, because a
    /// FluentSelect does not materialise its options while closed.
    /// </summary>
    private static void AwaitSubjectFilter(IRenderedComponent<EnrollmentExceptions> page) =>
        page.WaitForAssertion(() => FilterPicker(page).Items.Should().HaveCountGreaterThan(1,
            "the filter needs 'All subjects' PLUS the owner's own subjects"));

    /// <summary>
    /// The page's ONE subject control (v4 decision 11) — the filter's picker. Its options
    /// are <c>SubjectOption</c> records, so the owner's subjects arrive as
    /// <c>(id, name)</c> pairs behind a synthetic "All subjects". v3 had a
    /// <c>FluentSelect&lt;SubjectDto&gt;</c> in the add section as well; these tests must
    /// never reach for one, and <see cref="ThereIsExactlyOneSubjectControl"/> proves it.
    /// </summary>
    private static FluentSelect<EnrollmentExceptions.SubjectOption> FilterPicker(
        IRenderedComponent<EnrollmentExceptions> page) =>
        page.FindComponent<FluentSelect<EnrollmentExceptions.SubjectOption>>().Instance;

    /// <summary>The "All subjects" option — the page's whole-owner scope, and its reset.</summary>
    private static EnrollmentExceptions.SubjectOption AllSubjectsOption(
        IRenderedComponent<EnrollmentExceptions> page) =>
        FilterPicker(page).Items!.Single(o => o.Value == Guid.Empty);

    /// <summary>Scopes the view to one subject, the way a reader or an entry point would.</summary>
    private static Task ScopeToSubjectAsync(IRenderedComponent<EnrollmentExceptions> page, Guid topicId) =>
        SelectAsync(page, FilterPicker(page).Items!.Single(o => o.Value == topicId));

    /// <summary>Widens back to the owner's whole set — the only reset the page offers.</summary>
    private static Task ScopeToAllSubjectsAsync(IRenderedComponent<EnrollmentExceptions> page) =>
        SelectAsync(page, AllSubjectsOption(page));

    /// <summary>What the filter currently says the view is scoped to. "All subjects" is a
    /// RESOLVED selection, not the absence of one — the whole point of the sentinel key,
    /// and the observable difference from v3's null-valued filter.</summary>
    private static string ScopeLabel(IRenderedComponent<EnrollmentExceptions> page) =>
        FilterPicker(page).SelectedOption?.Label ?? "<no selection>";

    /// <summary>
    /// The add section's "Fill from" control — the CHOSEN period part (v5: decision 13
    /// reversed). v4's version of this file drove a <c>FluentSelect&lt;PeriodDto&gt;</c> period
    /// shortcut here and read the part off a read-only badge; both are gone, and with them the
    /// period-instance picker entirely.
    /// </summary>
    private static FluentSelect<EnrollmentExceptions.DivisionOption> DivisionPicker(
        IRenderedComponent<EnrollmentExceptions> page) =>
        page.FindComponent<FluentSelect<EnrollmentExceptions.DivisionOption>>().Instance;

    /// <summary>What the add section says the exception's part is.</summary>
    private static string DivisionLabel(IRenderedComponent<EnrollmentExceptions> page) =>
        DivisionPicker(page).SelectedOption?.Label ?? "<no selection>";

    /// <summary>Chooses the part the exception will be expressed in, waiting for the
    /// tenant's periods to have widened the options first.</summary>
    private static Task SelectDivisionAsync(
        IRenderedComponent<EnrollmentExceptions> page, AcademicYearDivision division)
    {
        EnrollmentExceptions.DivisionOption? option = null;
        page.WaitForAssertion(() =>
        {
            option = DivisionPicker(page).Items?.FirstOrDefault(o => o.Value == division);
            option.Should().NotBeNull($"the add section offers the {division} part it can write");
        });
        return SelectAsync(page, option!);
    }

    /// <summary>
    /// The position choices (v6 §11.3: a MULTI-select — one FluentCheckbox per offered sequence),
    /// in DOM order. That order IS the ladder order (1st, 2nd, 3rd …), so the Nth box is the Nth
    /// sequence, which is what lets a test drive one by position without also depending on the
    /// label formatter. The binding pair is Value/ValueChanged — <c>FluentCheckbox :
    /// FluentInputBase&lt;bool&gt;</c> — and NOT CheckState, which is the THREE-state parameter and
    /// throws unless ThreeState is true.
    /// </summary>
    private static IReadOnlyList<IRenderedComponent<FluentCheckbox>> PositionBoxes(
        IRenderedComponent<EnrollmentExceptions> page) =>
        page.FindComponents<FluentCheckbox>();

    /// <summary>The positions ON OFFER, as the reader sees them — read off the rendered
    /// checkboxes rather than a bound list, because the option set IS the behaviour under test
    /// here (1st–4th plus any higher declared position, Q5).</summary>
    private static string[] PositionLabels(IRenderedComponent<EnrollmentExceptions> page) =>
        [.. page.FindAll("fluent-checkbox").Select(r => r.TextContent.Trim())];

    /// <summary>Ticks the <paramref name="position"/>-th offered sequence (1-based) through its
    /// own ValueChanged — the same shape every other control in this file is driven with.</summary>
    private static Task ChoosePositionAsync(IRenderedComponent<EnrollmentExceptions> page, int position) =>
        SetPositionAsync(page, position, ticked: true);

    /// <summary>Un-ticks that sequence. A checkbox is the one control in this section that can be
    /// un-picked, which is what lets a test move from a DERIVED span back to a typed one without
    /// re-picking the part.</summary>
    private static Task UntickPositionAsync(IRenderedComponent<EnrollmentExceptions> page, int position) =>
        SetPositionAsync(page, position, ticked: false);

    private static Task SetPositionAsync(IRenderedComponent<EnrollmentExceptions> page, int position, bool ticked)
    {
        var boxes = PositionBoxes(page);
        var index = position - 1;
        index.Should().BeInRange(0, boxes.Count - 1,
            $"the part in force offers at least {position} sequence(s)");
        return page.InvokeAsync(() => boxes[index].Instance.ValueChanged.InvokeAsync(ticked));
    }

    /// <summary>Waits for the period list to have loaded, which is what puts positions on
    /// offer at all.</summary>
    private static void AwaitPositions(IRenderedComponent<EnrollmentExceptions> page) =>
        page.WaitForAssertion(() => PositionLabels(page).Should().NotBeEmpty(
            "a real part puts the position choices on offer"));

    /// <summary>Drives the whole add section to a complete write: scope to the only subject,
    /// choose the Term part, and choose the 1st position — whose period EXISTS in the fixture,
    /// so the range arrives filled.</summary>
    private static async Task FillAddSectionAsync(IRenderedComponent<EnrollmentExceptions> page)
    {
        await ScopeToSubjectAsync(page, TopicId);
        await SelectDivisionAsync(page, AcademicYearDivision.Terms);
        await ChoosePositionAsync(page, 1);
    }

    /// <summary>Switches the owner to the flag-gated group side and picks the group.</summary>
    private static async Task SelectGroupOwnerAsync(IRenderedComponent<EnrollmentExceptions> page)
    {
        await SelectAsync(page, page.FindComponent<FluentSelect<EnrollmentExceptions.OwnerTypeOption>>()
            .Instance.Items!.Single(o => o.Value == "ActivityGroup"));
        await SelectAsync(page, page.FindComponent<FluentSelect<ActivityGroupDto>>().Instance.Items!.Single());
    }

    /// <summary>Drives a range end's bound value directly — the same bound-callback shape
    /// <see cref="SelectAsync"/> uses for a select. <c>null</c> opens that end, which v4
    /// decision 12 expresses in the picker's own placeholder rather than with a Clear button.</summary>
    private static Task SetDateAsync(IRenderedComponent<EnrollmentExceptions> page, string pickerId, DateTime? value) =>
        page.InvokeAsync(() => DatePicker(page, pickerId).ValueChanged.InvokeAsync(value));

    /// <summary>One of the two ends of the range, by its own <c>Id</c> — the page renders two,
    /// so "the first picker" is an assumption no test here should make.</summary>
    private static FluentDatePicker DatePicker(IRenderedComponent<EnrollmentExceptions> page, string id) =>
        page.FindComponents<FluentDatePicker>().Single(p => p.Instance.Id == id).Instance;

    /// <summary>The add panel's one free-text field (the optional reason), found in the DOM by its
    /// placeholder. NOT by component type: each <c>FluentDatePicker</c> renders an INTERNAL
    /// <c>FluentTextField</c> of its own, so a <c>FindComponent&lt;FluentTextField&gt;</c> lookup returns a
    /// date picker's inner field rather than this one. The element returned is the input itself, so
    /// <c>Change</c> / <c>GetAttribute("value")</c> drive and read the bound reason.</summary>
    private static IElement ReasonField(IRenderedComponent<EnrollmentExceptions> page) =>
        page.Find("fluent-text-field[placeholder='Why?']");

    /// <summary>Asserts the element is a polite live region — the shape every state the page changes
    /// without a navigation has to carry, so a filter change or a post-write empty result is
    /// announced instead of silently redrawn.</summary>
    private static void AssertLiveRegion(IElement element)
    {
        element.GetAttribute("role").Should().Be("status");
        element.GetAttribute("aria-live").Should().Be("polite");
    }

    // ── AC-17: the list ────────────────────────────────────────────────────

    /// <summary>
    /// FR-ED-14 is dissolved: an owner with no exceptions is the NORMAL state, so the page
    /// says so in plain prose and raises no warning at all.
    /// </summary>
    [TestMethod]
    public void EmptyList_IsTheNormalState_WithNoWarning()
    {
        var harness = RenderPage(query: GradeOwnerQuery, exceptionsJson: "[]");

        harness.Page.WaitForAssertion(() => harness.Page.Markup.Should()
            .Contain("No exceptions — every subject is offered on every date."));

        harness.Page.FindComponents<FluentMessageBar>().Should().BeEmpty(
            "zero exceptions is the expected state, not a warning");
        harness.Page.FindAll(".exception-remove").Should().BeEmpty("there are no rows to remove");
        harness.Handler.Calls.Should().Contain(c => c.Method == "GET" && c.Url == GradeExceptionsUrl,
            "the page reads the selected owner's exceptions");
    }

    /// <summary>
    /// The two shapes an exception comes in: a term-scoped one (part + the term's dates)
    /// and a plain date window (no part). Both render their part AND their span, and the
    /// count badge counts ROWS — never a period list (§5.2). v3 spelled the second shape
    /// "—", which is a dash, not a word; v4 decision 14 gives every part ONE spelling that
    /// the picker, the derived badge and this row all share.
    /// </summary>
    [TestMethod]
    public void TermShapedAndPlainWindowRows_RenderTheirPartAndSpan()
    {
        var harness = RenderPage(query: GradeOwnerQuery, exceptionsJson: ExceptionsJson(
            ExceptionJson(Guid.NewGuid(), TopicId, "Terms", Term1Start, Term1End, reason: "Staffing"),
            ExceptionJson(Guid.NewGuid(), TopicId, "None", new DateOnly(2027, 5, 1), new DateOnly(2027, 5, 14))));

        harness.Page.WaitForAssertion(() => harness.Page.Markup.Should().Contain("1 Mar 2027 – 30 Jun 2027"));

        var markup = harness.Page.Markup;
        markup.Should().Contain("Mathematics", "the row names the subject the exception belongs to");
        harness.Page.FindAll(".exception-row fluent-badge").Select(b => b.TextContent.Trim()).Should()
            .BeEquivalentTo(new[] { "Term", "Any date" },
                "each row names its period part, and a date-only row says so in words");
        markup.Should().NotContain("Not term-scoped",
            "v3 spelled this concept two more ways on the same page; v4 has one");
        markup.Should().Contain("1–14 May 2027", "the plain window renders its own span");
        markup.Should().Contain("2 exceptions", "the badge counts exceptions, not periods");
        markup.Should().Contain("Staffing", "the optional reason is shown when one is set");
        harness.Page.FindAll(".exception-remove").Should().HaveCount(2);
    }

    /// <summary>
    /// Open bounds are first-class (§0 decision 8): a null start reads <c>from …</c> and a
    /// null end reads <c>to …</c> — a reader must never be shown an empty span.
    /// </summary>
    [TestMethod]
    public void OpenBounds_RenderFromAndTo()
    {
        var harness = RenderPage(query: GradeOwnerQuery, exceptionsJson: ExceptionsJson(
            ExceptionJson(Guid.NewGuid(), TopicId, "None", new DateOnly(2027, 6, 1), null),
            ExceptionJson(Guid.NewGuid(), TopicId, "None", null, new DateOnly(2027, 6, 30))));

        harness.Page.WaitForAssertion(() => harness.Page.Markup.Should().Contain("from 1 Jun 2027"));
        harness.Page.Markup.Should().Contain("to 30 Jun 2027");
    }

    /// <summary>
    /// Removing the LAST exception must work: there is no "the set may not be emptied" rule
    /// (FR-ED-14 dissolved) — the row is deleted on the server and the page falls back to
    /// the normal empty state.
    /// </summary>
    [TestMethod]
    public void RemovingTheLastException_Succeeds_AndReturnsToTheEmptyState()
    {
        var exceptionId = Guid.NewGuid();
        var harness = RenderPage(query: GradeOwnerQuery, exceptionsJson: ExceptionsJson(
            ExceptionJson(exceptionId, TopicId, "Terms", Term1Start, Term1End)));

        harness.Page.WaitForAssertion(() => harness.Page.FindAll(".exception-remove").Should().HaveCount(1));

        var removed = false;
        harness.Handler.MapDynamic("GET", GradeExceptionsUrl, HttpStatusCode.OK,
            () => removed ? "[]" : ExceptionsJson(ExceptionJson(exceptionId, TopicId, "Terms", Term1Start, Term1End)));
        harness.Handler.MapDynamic("DELETE", $"{ExceptionsUrl}/{exceptionId:D}", HttpStatusCode.NoContent,
            () => { removed = true; return ""; });

        harness.Page.Find(".exception-remove").Click();

        harness.Page.WaitForAssertion(() => harness.Handler.Calls.Should()
            .Contain(c => c.Method == "DELETE" && c.Url == $"{ExceptionsUrl}/{exceptionId:D}",
                "removing an exception reaches the API immediately"));
        harness.Page.WaitForAssertion(() => harness.Page.Markup.Should()
            .Contain("No exceptions — every subject is offered on every date."));
    }

    // ── AC-17: the add path ────────────────────────────────────────────────

    /// <summary>
    /// REPLACES v3's <c>AddWithNothingSelected_WritesNothing</c> (spec §9's map). "All
    /// subjects" is the owner's whole set, not a subject, so the add section is not merely
    /// disabled — it renders a prompt naming the control that fixes it, and offers no write
    /// affordance to press at all. v3 had no such state: its add panel carried its own
    /// subject picker, so a live form was shown with nothing behind it.
    /// </summary>
    [TestMethod]
    public void AllSubjects_DisablesTheAddSection_WithAPrompt_AndWritesNothing()
    {
        var harness = RenderPage(query: GradeOwnerQuery, exceptionsJson: "[]");

        AwaitSubjectFilter(harness.Page);
        ScopeLabel(harness.Page).Should().Be("All subjects", "the page lands on the owner's whole set");

        harness.Page.Find(".add-prompt").TextContent.Should()
            .Be("Choose a subject above to add an exception for.");
        AssertLiveRegion(harness.Page.Find(".add-prompt"));
        harness.Page.FindAll(".add-exception").Should().BeEmpty(
            "with no subject in scope there is nothing to write, so there is no affordance to press");
        harness.Handler.Calls.Should().NotContain(c => c.Method == "POST",
            "an add with no subject in scope must not reach the API");
    }

    /// <summary>
    /// The write is immediate and carries exactly the chosen shape: owner form, subject,
    /// part and the resolved span — no period id anywhere (§0 decision 7). The list is then
    /// re-read so it shows the server's answer rather than a hopeful local append.
    /// </summary>
    [TestMethod]
    public async Task Add_WritesTheExceptionImmediately_AndRereadsTheList()
    {
        var createdId = Guid.NewGuid();
        var harness = RenderPage(query: GradeOwnerQuery, exceptionsJson: "[]");

        AwaitSubjectFilter(harness.Page);

        var live = new List<Dictionary<string, object?>>();
        harness.Handler.MapDynamic("GET", GradeExceptionsUrl, HttpStatusCode.OK, () => ExceptionsJson(live.ToArray()));
        // v6 §11.3: a SECOND registration for the old single-item URL would intercept nothing, so
        // the page's /bulk write would never land in `live` and the re-read below could not show
        // it. The body is the ids shape the client now reads.
        harness.Handler.MapDynamic("POST", $"{ExceptionsUrl}/bulk", HttpStatusCode.Created, () =>
        {
            live.Add(ExceptionJson(createdId, TopicId, "Terms", Term1Start, Term1End));
            return $"{{\"ids\":[\"{createdId:D}\"]}}";
        });

        await FillAddSectionAsync(harness.Page);

        harness.Page.WaitForAssertion(() => harness.Page.Find(".add-exception").GetAttribute("disabled").Should()
            .BeNull("a subject plus a chosen part, position and resolved span is a complete write"));

        harness.Page.Find(".add-exception").Click();

        harness.Page.WaitForAssertion(() => harness.Handler.Calls.Should()
            .Contain(c => c.Method == "POST" && c.Url == $"{ExceptionsUrl}/bulk"));

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

        harness.Page.WaitForAssertion(() => harness.Page.Markup.Should()
            .Contain("1 Mar 2027 – 30 Jun 2027", "the list re-reads the server after the write"));
    }

    /// <summary>
    /// REPLACES v3's <c>ClearDate_UnbindsItsOwnDate_…</c> (spec §9's map) — v4 has no Clear
    /// button to fire, because v4 decision 12 makes an open end part of the range rather
    /// than something a button beside it has to undo. What the two share is the promise
    /// underneath: an unbound end reads as open IN WORDS, and the write affordance follows
    /// the same rule v3 tested (at least one bound, or there is nothing to write — neither
    /// bound means "never offered", a different concept, §2.2).
    /// </summary>
    [TestMethod]
    public async Task OpenEnds_ReadAsAnyStartAndAnyDate_AndGateTheWrite()
    {
        var harness = RenderPage(query: GradeOwnerQuery, exceptionsJson: "[]");

        AwaitSubjectFilter(harness.Page);
        await ScopeToSubjectAsync(harness.Page, TopicId);

        // The open state is NAMED, not implied by a missing value or a button's absence.
        DatePicker(harness.Page, "exception-start-date").Placeholder.Should().Be("(any start)");
        DatePicker(harness.Page, "exception-end-date").Placeholder.Should().Be("(any end)");
        harness.Page.FindAll(".clear-date").Should().BeEmpty(
            "v4 decision 12 removed the Clear buttons: an open end is the picker's own empty state");

        // A subject in scope but no bound at all: still nothing to write.
        harness.Page.Find(".add-exception").GetAttribute("disabled").Should().NotBeNull(
            "a subject alone is not a write — the range needs at least one bound");

        // A chosen sequence whose period EXISTS derives the whole span, so the pickers are
        // REMOVED ENTIRELY — no derived-span text either (v6 §11.2 decision 17: the owner's
        // "keep span fully silent"). Nothing is typed, so the write is armed on the choice
        // alone; the derived dates are asserted where they are WRITTEN, not shown here.
        await SelectDivisionAsync(harness.Page, AcademicYearDivision.Terms);
        await ChoosePositionAsync(harness.Page, 1);
        harness.Page.WaitForAssertion(() => harness.Page.Find(".add-exception").GetAttribute("disabled")
            .Should().BeNull("a subject plus a sequence whose span is derived is a complete write"));
        harness.Page.FindAll(".span-group").Should().BeEmpty(
            "the span is derived, so the range inputs are silent — not shown read-only");

        // A sequence with NO period behind it is the documented flow (Q1) AND the guard that keeps
        // this form reachable: that item's span falls back to typed bounds, so the pickers COME BACK
        // and the write stays disarmed until one is typed. The fixture has no 4th-term period.
        await UntickPositionAsync(harness.Page, 1);
        await ChoosePositionAsync(harness.Page, 4);
        harness.Page.WaitForAssertion(() => harness.Page.FindAll(".span-group").Should().NotBeEmpty(
            "one sequence with no period puts the typed range back — the dead-end guard"));
        harness.Page.Find(".add-exception").GetAttribute("disabled").Should().NotBeNull(
            "and the write waits for the bound that sequence needs");

        // Typing ONE end is enough: an open end is a legitimate span rather than a hole in one.
        await SetDateAsync(harness.Page, "exception-end-date", Term1End.ToDateTime(TimeOnly.MinValue));
        harness.Page.WaitForAssertion(() => harness.Page.Find(".add-exception").GetAttribute("disabled")
            .Should().BeNull("one bound is enough"));
        DatePicker(harness.Page, "exception-start-date").Value.Should().BeNull(
            "the open start stays open — decision 12's placeholder, not a Clear button");
    }

    /// <summary>
    /// The server is the gate: a 409 (the same owner + subject + part + span already
    /// exists) must surface the SERVER's own message rather than being swallowed or
    /// replaced by a generic one.
    /// </summary>
    [TestMethod]
    public async Task DuplicateRejectedByTheServer_SurfacesTheServerMessage()
    {
        const string serverMessage = "This subject is already excepted for that part and span.";

        var harness = RenderPage(
            query: GradeOwnerQuery,
            exceptionsJson: "[]",
            addStatus: HttpStatusCode.Conflict,
            addBody: $"{{\"message\":\"{serverMessage}\"}}");

        AwaitSubjectFilter(harness.Page);
        await FillAddSectionAsync(harness.Page);

        harness.Page.Find(".add-exception").Click();

        harness.Page.WaitForAssertion(() => harness.Page.Markup.Should().Contain(serverMessage,
            "the server's 409 message reaches the user verbatim"));
        harness.Page.Markup.Should().NotContain("\"message\"",
            "the user reads the server's sentence, not the JSON envelope it arrived in");
        harness.Page.Markup.Should().NotContain("CreateSubjectEnrollmentException failed",
            "the client's diagnostic prefix is not an answer to show a user");
    }

    /// <summary>
    /// Immediate write means there is nothing to submit: the page holds no form and offers
    /// no Save control at all (§5.1).
    /// </summary>
    [TestMethod]
    public void PageOffersNoSaveControl()
    {
        var harness = RenderPage(query: GradeOwnerQuery, exceptionsJson: "[]");

        harness.Page.WaitForAssertion(() => harness.Page.Markup.Should()
            .Contain("No exceptions — every subject is offered on every date."));

        harness.Page.FindAll("form").Should().BeEmpty("the page is not a form — every write is immediate");
        harness.Page.Markup.Should().NotContain("Save", "a Save control would either lie or persist something else");
    }

    // ── AC-17: the owner toggle, the part picker, the entry point ──────────

    /// <summary>
    /// The group owner side is gated by <c>FEATURE:EnableActivityGroups</c>: with the flag
    /// off the owner-type picker offers a grade level only, and the flag-gated route is
    /// never called. Asserted on the bound items — a FluentSelect does not materialise its
    /// options while closed.
    /// </summary>
    [TestMethod]
    public void OwnerToggle_FlagOff_OmitsTheGroupSide_AndNeverCallsIt()
    {
        var harness = RenderPage(query: GradeOwnerQuery, activityGroupsEnabled: false, exceptionsJson: "[]");

        AwaitSubjectFilter(harness.Page);

        harness.Page.FindComponent<FluentSelect<EnrollmentExceptions.OwnerTypeOption>>()
            .Instance.Items!.Select(o => o.Value).Should()
            .BeEquivalentTo(new[] { "GradeLevel" },
                "an off flag means the group side does not exist — offering it would be a dead end");
        harness.Handler.Calls.Should().NotContain(
            c => c.Url.StartsWith(ActivityGroupsUrl, StringComparison.OrdinalIgnoreCase),
            "the route is unmapped while the flag is off, so calling it is pure noise");
    }

    /// <summary>With the flag on the group side is offered, and the group's exceptions can be
    /// listed exactly like a grade's (Q1 widened the owner gate).</summary>
    [TestMethod]
    public async Task OwnerToggle_FlagOn_OffersTheGroupSide()
    {
        var harness = RenderPage(query: GradeOwnerQuery, activityGroupsEnabled: true, exceptionsJson: "[]");

        AwaitSubjectFilter(harness.Page);

        harness.Page.FindComponent<FluentSelect<EnrollmentExceptions.OwnerTypeOption>>()
            .Instance.Items!.Select(o => o.Value).Should()
            .BeEquivalentTo(new[] { "GradeLevel", "ActivityGroup" });
        harness.Handler.Calls.Should().Contain(
            c => c.Url.StartsWith(ActivityGroupsUrl, StringComparison.OrdinalIgnoreCase),
            "an enabled flag must actually load the groups");

        var groupOwner = harness.Page
            .FindComponent<FluentSelect<EnrollmentExceptions.OwnerTypeOption>>()
            .Instance.Items!.Single(o => o.Value == "ActivityGroup");
        await SelectAsync(harness.Page, groupOwner);
        await SelectAsync(harness.Page, harness.Page
            .FindComponent<FluentSelect<ActivityGroupDto>>().Instance.Items!.Single());

        harness.Page.WaitForAssertion(() => harness.Handler.Calls.Should()
            .Contain(c => c.Url.StartsWith(GroupExceptionsUrl, StringComparison.OrdinalIgnoreCase),
                "the group owner lists the group's exceptions"));
    }

    /// <summary>
    /// A different owner means a different set of exceptions AND a different set of subjects, so a
    /// half-filled write must not travel with the switch (ClearAddSelection). In v4 the scope IS
    /// the add section's subject, so clearing it CLOSES the section — a stronger reset than v3's
    /// field-by-field clearing, and the field-level assertions had to move: a closed section has
    /// no fields to read. So the proof is staged — assert the close, then re-scope to a subject
    /// the NEW owner lists and show the span, the reason and the period all came back empty.
    /// </summary>
    [TestMethod]
    public async Task ChangingOwner_ResetsTheAddSection()
    {
        var harness = RenderPage(query: GradeOwnerQuery, exceptionsJson: "[]");

        AwaitSubjectFilter(harness.Page);
        await FillAddSectionAsync(harness.Page);
        ReasonField(harness.Page).Change("Staffing");

        harness.Page.WaitForAssertion(() => harness.Page.Find(".add-exception").GetAttribute("disabled").Should()
            .BeNull("the section is armed for the owner that is about to change"));
        ReasonField(harness.Page).GetAttribute("value").Should().Be("Staffing");
        harness.Page.FindAll(".span-group").Should().BeEmpty(
            "the 1st term's span is DERIVED, so the section is armed with no typed range at all "
            + "(v6 §11.2 decision 17)");

        // Owner → the OTHER grade level: an owner change, not just a subject change.
        await SelectAsync(harness.Page, harness.Page
            .FindComponent<FluentSelect<GradeLevelDto>>().Instance.Items!.Single(g => g.Id == GradeBId));

        harness.Page.WaitForAssertion(() => harness.Handler.Calls.Should()
            .Contain(c => c.Url == $"{ExceptionsUrl}?gradeLevelId={GradeBId:D}",
                "the new owner's exceptions and subjects are the ones that load"));

        // WaitForAssertion on purpose: the state is pushed to the child components by the
        // render that follows the load, which can land after the await on the picker returns.
        harness.Page.WaitForAssertion(() =>
        {
            ScopeLabel(harness.Page).Should().Be("All subjects",
                "the new owner may not even list the old owner's subject, so the scope widens");
            harness.Page.FindAll(".add-exception").Should().BeEmpty(
                "no subject in scope means the add section is CLOSED, not merely disarmed");
            harness.Page.Find(".add-prompt").Should().NotBeNull();
        });

        // Re-scope to a subject THIS owner lists: the half-filled write is gone.
        await ScopeToSubjectAsync(harness.Page, TopicBId);

        harness.Page.WaitForAssertion(() =>
        {
            ReasonField(harness.Page).GetAttribute("value").Should().BeNullOrEmpty(
                "the reason belonged to the previous owner's write");
            DatePicker(harness.Page, "exception-start-date").Value.Should().BeNull("the range's bounds go with it");
            DatePicker(harness.Page, "exception-end-date").Value.Should().BeNull();
            DivisionLabel(harness.Page).Should().Be("Any date",
                "the part reset to the free window, which every owner may write (FR-56 safe by construction)");
            harness.Page.FindAll("fluent-checkbox").Should().BeEmpty(
                "and the POSITION went with the range it belonged to: a free window has no position at all, "
                + "so the ordinal the previous owner was writing cannot travel");
        });
    }

    /// <summary>
    /// REPLACES v4's <c>DerivedPart_FlipsBetweenTermAndAnyDate_WithTheRangeSource</c> (spec §9's
    /// map) — the same promise against the reversed mechanism. v4 DERIVED the part from a picked
    /// period and rendered it read-only; v5 (decision 13 reversed) makes it a control, so the
    /// assertions invert: the part is CHOSEN, it is offered only where the tenant can write it, and
    /// the read-only badge is gone rather than merely re-labelled. What does NOT change is decision
    /// 14's one-spelling rule — the control's labels come from the SAME helper the list rows use.
    /// </summary>
    [TestMethod]
    public async Task ChosenPart_IsAControl_OfferedOnlyWhereTheTenantHasPeriods()
    {
        var harness = RenderPage(query: GradeOwnerQuery, exceptionsJson: "[]");

        AwaitSubjectFilter(harness.Page);
        await ScopeToSubjectAsync(harness.Page, TopicId);

        // The tenant's periods are the source: Terms has a period behind it, Semesters does not,
        // and a free window is always expressible.
        harness.Page.WaitForAssertion(() => DivisionPicker(harness.Page).Items!.Select(o => o.Label).Should()
            .BeEquivalentTo(new[] { "Term", "Any date" },
                "the divisions the tenant has periods FOR, plus the free window — never a part it cannot fill"));

        harness.Page.FindAll(".part-readonly").Should().BeEmpty(
            "v4's read-only part badge is gone: the part is a control now, so there is nothing to read it off");

        // Q3/Q7: every add-section row is the house primitive with its label BENEATH its input —
        // and the POSITION row is not among them yet, because a free window has no position.
        // Counted as DESCENDANTS, not children: the range's two ends are label-below rows too,
        // inside the group, and they are labelled FIELDS in their own right (v5.3).
        harness.Page.FindAll(".add-form .form-row--label-below").Should().HaveCount(4,
            "Fill from / Not offered from / Not offered to / Reason, each with its label under "
            + "its input");
        harness.Page.FindAll("fluent-checkbox").Should().BeEmpty("Any date has no position to choose");

        await SelectDivisionAsync(harness.Page, AcademicYearDivision.Terms);
        AwaitPositions(harness.Page);

        harness.Page.FindAll(".add-form .form-row--label-below").Should().HaveCount(5,
            "choosing a real part is what puts the position row on the form");
        harness.Page.FindAll("[aria-label='Position']").Should().HaveCount(1,
            "and the choices are a NAMED group of checkboxes, not anonymous ones");
    }

    /// <summary>
    /// ADDED by v5 — the write shape the round's slice 6 owed. The body carries the CHOSEN part
    /// and the CHOSEN position, not something derived from the range: a tenant whose term and
    /// semester sit at DIFFERENT positions is the discriminator, because no derivation from the
    /// filled dates could produce this pair.
    /// </summary>
    [TestMethod]
    public async Task Add_CarriesTheChosenPartAndPosition_ToTheServer()
    {
        var harness = RenderPage(
            query: GradeOwnerQuery,
            exceptionsJson: "[]",
            periodsJson: PeriodsWithSemesterJson());

        AwaitSubjectFilter(harness.Page);
        await ScopeToSubjectAsync(harness.Page, TopicId);
        await SelectDivisionAsync(harness.Page, AcademicYearDivision.Semesters);
        await ChoosePositionAsync(harness.Page, 2);

        harness.Page.WaitForAssertion(() => harness.Page.Find(".add-exception").GetAttribute("disabled").Should()
            .BeNull("the 2nd semester exists, so the position supplies the whole range"));

        harness.Page.Find(".add-exception").Click();

        harness.Page.WaitForAssertion(() => harness.Handler.Calls.Should()
            .Contain(c => c.Method == "POST" && c.Url == $"{ExceptionsUrl}/bulk"));

        var post = harness.Handler.Calls.Single(c => c.Method == "POST");
        post.Body.Should().Contain("\"division\":2", "the CHOSEN part travels — Semesters, as picked");
        post.Body.Should().Contain("\"ordinal\":2", "and the chosen position travels as the ordinal (§0 decision 15)");
        post.Body.Should().Contain($"\"startDate\":\"{Sem2Start:yyyy-MM-dd}\"");
        post.Body.Should().Contain($"\"endDate\":\"{Sem2End:yyyy-MM-dd}\"",
            "the 2nd semester's own period resolved the range");
    }

    /// <summary>
    /// REPLACES v4's <c>AllDateTenants_ReadAnyDate_BecauseNoPeriodDerivesAPart</c> (spec §9's map).
    /// The tenant is the same — plain undivided years — and so is the promise: no part this tenant
    /// cannot write is ever on offer. The mechanism changed, so the assertion does: v4's shortcut was
    /// absent and the part read "Any date"; v5's control offers exactly that one option, and a free
    /// window has no position to choose at all.
    /// </summary>
    [TestMethod]
    public async Task ChosenPart_TenantWithoutDivisions_OffersAnyDateOnly_AndNoPosition()
    {
        var harness = RenderPage(query: GradeOwnerQuery, exceptionsJson: "[]", periodsJson: PeriodsWithoutDivisionsJson());

        AwaitSubjectFilter(harness.Page);
        await ScopeToSubjectAsync(harness.Page, TopicId);

        harness.Page.WaitForAssertion(() => harness.Handler.Calls.Should()
            .Contain(c => c.Url == PeriodsUrl, "the period hierarchy is what decides which parts exist"));

        DivisionPicker(harness.Page).Items!.Select(o => o.Label).Should().BeEquivalentTo(new[] { "Any date" },
            "an undivided tenant has no term/semester period to fill from, so only the free window is offered");
        DivisionLabel(harness.Page).Should().Be("Any date");
        harness.Page.FindAll("fluent-checkbox").Should().BeEmpty(
            "a free window has no position in a run of terms or semesters, so none is offered — the server rejects an ordinal on one");
        harness.Page.Find(".add-panel").TextContent.Should().NotContain("Term",
            "and never the word of a part this tenant has no periods for");
    }

    /// <summary>
    /// REPLACES v4's <c>DerivedPart_GroupOwned_TermlyGroup_CanOnlyEverBeTerm</c> (spec §9's map).
    /// FR-56 is still enforced server-side; v5 enforces it at the control as well, and more bluntly
    /// than v4 did: a Termly group is offered Terms and NOTHING else, so an invalid part is not
    /// merely unreachable by derivation — it cannot be selected. The positions the division permits
    /// are offered and do travel, which is the group-side half of the ordinal path.
    /// </summary>
    [TestMethod]
    public async Task ChosenPart_GroupOwned_TermlyGroup_OffersTermOnly()
    {
        var harness = RenderPage(query: GradeOwnerQuery, activityGroupsEnabled: true, exceptionsJson: "[]");

        AwaitSubjectFilter(harness.Page);
        await SelectGroupOwnerAsync(harness.Page);
        await ScopeToSubjectAsync(harness.Page, TopicId);

        harness.Page.WaitForAssertion(() => DivisionPicker(harness.Page).Items!.Select(o => o.Label).Should()
            .BeEquivalentTo(new[] { "Term" },
                "FR-56 lets a Termly group write Terms and nothing else, so nothing else may be on offer"));
        DivisionLabel(harness.Page).Should().Be("Term", "and it is what the section is actually set to");

        await ChoosePositionAsync(harness.Page, 1);
        harness.Page.WaitForAssertion(() => harness.Page.Find(".add-exception").GetAttribute("disabled").Should()
            .BeNull("the 1st term exists, so the group's only legal part writes a complete row"));

        harness.Page.Find(".add-exception").Click();
        harness.Page.WaitForAssertion(() => harness.Handler.Calls.Should()
            .Contain(c => c.Method == "POST" && c.Url == $"{ExceptionsUrl}/bulk"));

        var post = harness.Handler.Calls.Single(c => c.Method == "POST");
        post.Body.Should().Contain($"\"activityGroupId\":\"{GroupId:D}\"");
        post.Body.Should().Contain("\"division\":1", "Terms — the only part FR-56 permits here");
        post.Body.Should().Contain("\"ordinal\":1");
    }

    /// <summary>
    /// The other half of FR-56, in v5's terms: a group whose span is not period-aligned may store
    /// <c>None</c> only, so its control offers "Any date" and no other part at all — and therefore no
    /// position either, which is exactly the ordinal the server would reject on a free window. The
    /// tenant's terms stay unreachable from this owner.
    /// </summary>
    [TestMethod]
    public async Task ChosenPart_GroupOwned_UnAlignedSpan_OffersAnyDateOnly_AndNoPosition()
    {
        var harness = RenderPage(
            query: GradeOwnerQuery,
            activityGroupsEnabled: true,
            exceptionsJson: "[]",
            groupSpan: "WholeAcademicYear");

        AwaitSubjectFilter(harness.Page);
        await SelectGroupOwnerAsync(harness.Page);
        await ScopeToSubjectAsync(harness.Page, TopicId);

        DivisionPicker(harness.Page).Items!.Select(o => o.Label).Should().BeEquivalentTo(new[] { "Any date" },
            "FR-56 lets a non-period-aligned group store None only, so no real part is on offer at all");
        harness.Page.FindAll("fluent-checkbox").Should().BeEmpty(
            "a free window carries no position — which is the ordinal the server would reject");
        harness.Page.Find(".add-panel").TextContent.Should().NotContain("Term",
            "the tenant's terms are unreachable from this owner, exactly as FR-56 requires");
    }

    /// <summary>
    /// Q5 and Q1 in one assertion. The ladder is 1st–4th ALWAYS, extended by any higher position the
    /// tenant has declared — here a 5th term — so a 5th appears the moment a tenant creates one. And
    /// it is deliberately NOT limited to the periods that exist: 2nd, 3rd and 4th are on offer even
    /// though the fixture has only 1st- and 5th-term periods, because the position is structural and
    /// the reader may type the dates for a term the calendar has not been built for yet.
    /// </summary>
    [TestMethod]
    public async Task Positions_AreOneToFourPlusAnyHigherDeclared_NotOnlyTheExistingPeriods()
    {
        var harness = RenderPage(
            query: GradeOwnerQuery,
            exceptionsJson: "[]",
            periodsJson: PeriodsWithFifthTermJson());

        AwaitSubjectFilter(harness.Page);
        await ScopeToSubjectAsync(harness.Page, TopicId);
        await SelectDivisionAsync(harness.Page, AcademicYearDivision.Terms);
        AwaitPositions(harness.Page);

        PositionLabels(harness.Page).Should().BeEquivalentTo(new[] { "1st", "2nd", "3rd", "4th", "5th" },
            "1st–4th always, plus the 5th the tenant declared (Q5) — and 2nd/3rd/4th although no such "
            + "period exists, which is what makes the position structural rather than a period pick (Q1)");
    }

    /// <summary>
    /// Q1's intended flow, which is NOT an error. A position either supplies the WHOLE span or none
    /// of it: the 1st term fills the range, and the 3rd — which this tenant has no period for —
    /// clears it, so no stale span can masquerade as the chosen one's. The write then stays disabled
    /// until the reader types a bound, and the body carries the position BESIDE those dates: the
    /// ordinal and the span are deliberately allowed to disagree (§0 decision 15).
    /// </summary>
    [TestMethod]
    public async Task PositionWithNoMatchingPeriod_LeavesTheRangeEmpty_AndTakesTheTypedDates()
    {
        var harness = RenderPage(query: GradeOwnerQuery, exceptionsJson: "[]");

        AwaitSubjectFilter(harness.Page);
        await ScopeToSubjectAsync(harness.Page, TopicId);
        await SelectDivisionAsync(harness.Page, AcademicYearDivision.Terms);

        // The 1st term HAS a period, so its span is DERIVED and the range inputs are SILENT
        // (v6 §11.2 decision 17) — nothing is shown and nothing needs typing.
        await ChoosePositionAsync(harness.Page, 1);
        harness.Page.WaitForAssertion(() => harness.Page.Find(".add-exception").GetAttribute("disabled")
            .Should().BeNull("a derived span is a complete write on the choice alone"));
        harness.Page.FindAll(".span-group").Should().BeEmpty(
            "the derived span is not shown at all — not even read-only");

        // Ticking the 3rd term ADDS to the selection (multi-select) and it has no period, so the
        // typed range comes BACK: that item's span falls back to bounds the reader supplies. That
        // is the half of this test that v6 keeps — and the guard that keeps the form reachable.
        await ChoosePositionAsync(harness.Page, 3);
        harness.Page.WaitForAssertion(() => harness.Page.FindAll(".span-group").Should().NotBeEmpty(
            "one sequence with no period puts the typed range back — the dead-end guard"));
        harness.Page.WaitForAssertion(() =>
        {
            DatePicker(harness.Page, "exception-start-date").Value.Should().BeNull(
                "nothing auto-fills: the typed bounds belong to the sequence that needs them, not to the one whose period supplied dates");
            DatePicker(harness.Page, "exception-end-date").Value.Should().BeNull();
            harness.Page.Find(".add-exception").GetAttribute("disabled").Should().NotBeNull(
                "and with that item unbounded there is nothing to write — that is the flow, not a failure");
        });

        await SetDateAsync(harness.Page, "exception-start-date", new DateTime(2027, 9, 1));
        harness.Page.WaitForAssertion(() => harness.Page.Find(".add-exception").GetAttribute("disabled").Should()
            .BeNull("one typed bound is enough"));
        harness.Page.Find(".add-exception").Click();

        harness.Page.WaitForAssertion(() => harness.Handler.Calls.Should()
            .Contain(c => c.Method == "POST" && c.Url == $"{ExceptionsUrl}/bulk"));

        var post = harness.Handler.Calls.Single(c => c.Method == "POST");
        post.Body.Should().Contain("\"division\":1");
        post.Body.Should().Contain("\"ordinal\":1",
            "v6 §11.3: the FIRST chosen sequence travels too — the selection is a SET written as one "
            + "row per sequence, not the last tick replacing the rest");
        post.Body.Should().Contain("\"ordinal\":3",
            "the position is descriptive: it travels even though no period backs it");
        post.Body.Should().Contain("\"startDate\":\"2027-09-01\"", "the dates the reader typed");
        post.Body.Should().Contain("\"endDate\":null", "and the open end stays a legitimate bound");
    }

    /// <summary>
    /// A position is per year (§0 decision 15), so two years may each hold a "1st term" — which makes
    /// "which period fills the range from 1st?" a real question the page has to answer
    /// deterministically rather than arbitrarily. The ACTIVE year's wins: it is the year the reader is
    /// administrating. The dates are a convenience either way — the span is what matches (§2.3) and
    /// the position is a label beside it — but not an arbitrary one.
    /// </summary>
    [TestMethod]
    public async Task Positions_TwoYearsBothTermOne_FillFromTheActiveYearsPeriod()
    {
        var harness = RenderPage(
            query: GradeOwnerQuery,
            exceptionsJson: "[]",
            periodsJson: PeriodsWithTwoYearsJson());

        AwaitSubjectFilter(harness.Page);
        await ScopeToSubjectAsync(harness.Page, TopicId);
        await SelectDivisionAsync(harness.Page, AcademicYearDivision.Terms);
        await ChoosePositionAsync(harness.Page, 1);

        // v6 §11.2 decision 17: the span is DERIVED from the period and kept SILENT, so which of
        // the two years supplies it is observable where it is WRITTEN rather than on screen. The
        // promise under test is unchanged — the ACTIVE year's Term 1 wins, deterministically — it
        // has simply moved from the pickers to the body.
        harness.Page.FindAll(".span-group").Should().BeEmpty(
            "the span is derived, so the range inputs are silent — not shown read-only");

        harness.Page.Find(".add-exception").Click();

        harness.Page.WaitForAssertion(() => harness.Handler.Calls.Should()
            .Contain(c => c.Method == "POST" && c.Url == $"{ExceptionsUrl}/bulk"));

        var post = harness.Handler.Calls.Single(c => c.Method == "POST");
        post.Body.Should().Contain("\"startDate\":\"2028-03-01\"",
            "the ACTIVE year's Term 1, not the completed year's");
        post.Body.Should().Contain("\"endDate\":\"2028-06-30\"", "and that period's end");
    }

    /// <summary>
    /// Q4: the owner KIND is the only pre-selection. Arriving with no query string — the page's own
    /// landing — must NOT scope the reader to a grade level (v4 picked the first one) and must not
    /// scope them to a subject either: nothing is listed, nothing is armed, and the page asks for the
    /// one thing it needs. The fallout this created is that every other test here now NAMES an owner
    /// in the query string, exactly as an entry point's kebab does.
    /// </summary>
    [TestMethod]
    public void LandingWithNoQuery_PreSelectsNoOwnerGradeOrSubject()
    {
        var harness = RenderPage(query: "", exceptionsJson: "[]");

        harness.Page.WaitForAssertion(() => harness.Page.Find(".owner-prompt").Should().NotBeNull(
            "with no owner there is nothing to manage, and the page says so instead of guessing"));

        harness.Page.FindComponent<FluentSelect<GradeLevelDto>>().Instance.SelectedOption.Should().BeNull(
            "no grade level is chosen for the reader");
        harness.Page.FindComponents<FluentSelect<EnrollmentExceptions.SubjectOption>>().Should().BeEmpty(
            "and no subject control exists yet — the subject filter belongs to an owner");
        harness.Page.FindAll(".exceptions-section").Should().BeEmpty("there is no owner to have a section for");
        harness.Handler.Calls.Should().NotContain(c => c.Method == "GET" && c.Url == GradeExceptionsUrl,
            "no owner is named, so there is no owner's exceptions to read");
    }


    /// <summary>
    /// N4 / §5.2 row 1: the landing's kebab navigates here with the owner AND the subject in
    /// the query string, and the page must LAND pre-selected — the owner whose exceptions are
    /// listed, and the subject the ONE control holding a subject is scoped to.
    /// </summary>
    [TestMethod]
    public void QueryString_PreSelectsGradeOwnerAndSubject()
    {
        var harness = RenderPage(
            query: $"?gradeLevelId={GradeId:D}&topicId={TopicId:D}",
            exceptionsJson: ExceptionsJson(ExceptionJson(Guid.NewGuid(), TopicId, "Terms", Term1Start, Term1End)));

        harness.Page.WaitForAssertion(() => harness.Page.Markup.Should().Contain("1 Mar 2027 – 30 Jun 2027"));
        harness.Handler.Calls.Should().Contain(c => c.Url == GradeExceptionsUrl,
            "the pre-selected owner is the one whose exceptions are listed");

        ScopeLabel(harness.Page).Should().Be("Mathematics",
            "the query's subject lands in the filter, and therefore in the add section as well");
        harness.Page.Find(".add-title").TextContent.Should().Contain("Add exception for Mathematics");
    }

    /// <summary>
    /// The group-owned half of the same pre-selection (Q1: group-owned rows get the
    /// affordance too, so the page must land on the GROUP owner when the query string names
    /// one).
    /// </summary>
    [TestMethod]
    public void QueryString_PreSelectsGroupOwner()
    {
        var harness = RenderPage(
            query: $"?activityGroupId={GroupId:D}&topicId={TopicId:D}",
            activityGroupsEnabled: true,
            exceptionsJson: "[]");

        harness.Page.WaitForAssertion(() => harness.Handler.Calls.Should()
            .Contain(c => c.Url.StartsWith(GroupExceptionsUrl, StringComparison.OrdinalIgnoreCase),
                "the group named by the query string is the selected owner"));
        harness.Handler.Calls.Should().NotContain(
            c => c.Url.StartsWith(GradeExceptionsUrl, StringComparison.OrdinalIgnoreCase),
            "the group form of the entry point must not fall back to a grade");
    }

    /// <summary>
    /// §5.2 row 1 / N4 on the OTHER entry path: the same page instance reused for a new
    /// query string must still land pre-selected — REGRESSION GUARD, not a discriminator. The
    /// pending field this page applies after the owner-data load exists for the case below (a
    /// subject the owner does not list); this scenario also passes against an eager
    /// implementation, because the dropdown's reset path needs a render BETWEEN the stale
    /// options being rebuilt and the reload arriving.
    /// </summary>
    [TestMethod]
    public void QueryStringChange_KeepsThePreSelection_WhenTheTopicIsNotInThePreviousOwnersList()
    {
        var harness = RenderPage(
            query: $"?gradeLevelId={GradeId:D}&topicId={TopicId:D}",
            exceptionsJson: "[]");

        harness.Page.WaitForAssertion(() => ScopeLabel(harness.Page).Should().Be("Mathematics",
            "the first load pre-selects the query string's subject"));

        // The SAME page instance is reused for a different query string whose subject is
        // absent from grade 5's list — the shape the review flagged as losing the
        // pre-selection, kept as the guard that the load path stays render-free.
        Services.GetRequiredService<NavigationManager>()
            .NavigateTo($"{PageUrl}?gradeLevelId={GradeBId:D}&topicId={TopicBId:D}");

        harness.Page.WaitForAssertion(() => harness.Handler.Calls.Should()
            .Contain(c => c.Url == $"{ExceptionsUrl}?gradeLevelId={GradeBId:D}",
                "the in-page query change must actually switch the owner"));
        harness.Page.WaitForAssertion(() => ScopeLabel(harness.Page).Should().Be("Biology",
            "the pre-selection survives the new owner's subject list loading"));
    }

    // ── §5.2 row 1: the entry point's subject filter ────────────────────────

    /// <summary>
    /// §5.2 row 1 is PER-SUBJECT: the kebab that lands here names ONE subject, so the list
    /// shows that subject's rows rather than the owner's whole set with a subject picked
    /// somewhere else on the page. The owner still HAS both rows — the badge stays the
    /// owner's, and under a scope it is what says the rows are a subset — and the page
    /// names the scope in exactly ONE place: the control that set it.
    /// </summary>
    [TestMethod]
    public void QueryString_FiltersTheListToThePreSelectedSubject()
    {
        var harness = RenderPage(
            query: $"?gradeLevelId={GradeId:D}&topicId={TopicId:D}",
            exceptionsJson: TwoTopicExceptionsJson(),
            gradeSubjectsJson: TwoSubjectsJson());

        harness.Page.WaitForAssertion(() => harness.Page.FindAll(".exception-row").Should().HaveCount(1));

        ScopeLabel(harness.Page).Should().Be("Mathematics",
            "the entry point's subject IS the filter's selection — one control, not a query plus a picker");
        harness.Page.FindAll(".exception-topic").Should().BeEmpty(
            "and the row must not repeat what the filter already says (v4 decision 11)");
        harness.Page.FindAll(".exception-remove").Should().HaveCount(1,
            "only the listed subject's rows are removable from this view");
        harness.Page.Markup.Should().Contain("2 exceptions",
            "the badge counts the OWNER's set — the scoped rows are a subset of it");
        harness.Page.Find(".add-title").TextContent.Should().Contain("Add exception for Mathematics",
            "the add section is defaulted to the filter, so it names the subject it will write");
    }

    /// <summary>
    /// REPLACES v3's <c>ShowAll_ClearsTheFilter_…</c> (spec §9's map). The reset is not a
    /// button beside the list — it is the filter's own "All subjects" option, so a filter is
    /// reset by changing the filter and there is no second thing to learn. Two things follow
    /// that v3 could not assert: the subject column RETURNS (nothing in the row names the
    /// subject once the scope is gone), and the add section closes (there is no subject to
    /// write for).
    /// </summary>
    [TestMethod]
    public async Task AllSubjects_RestoresTheOwnersWholeSet()
    {
        var harness = RenderPage(
            query: $"?gradeLevelId={GradeId:D}&topicId={TopicId:D}",
            exceptionsJson: TwoTopicExceptionsJson(),
            gradeSubjectsJson: TwoSubjectsJson());

        AwaitSubjectFilter(harness.Page);
        harness.Page.WaitForAssertion(() => harness.Page.FindAll(".exception-row").Should().HaveCount(1));
        harness.Page.FindAll(".exception-topic").Should().BeEmpty("scoped, so the column is omitted");

        await ScopeToAllSubjectsAsync(harness.Page);

        harness.Page.WaitForAssertion(() => harness.Page.FindAll(".exception-row").Should().HaveCount(2,
            "choosing All subjects in the filter lists every subject's exceptions again"));
        harness.Page.FindAll(".exception-topic").Select(t => t.TextContent.Trim()).Should()
            .BeEquivalentTo(new[] { "Mathematics", "Biology" },
                "the subject column is BACK, because no single control names the subject any more");
        ScopeLabel(harness.Page).Should().Be("All subjects");
        harness.Page.Find(".add-prompt").Should().NotBeNull(
            "and with no subject in scope the add section says so instead of arming a write");
        harness.Page.FindAll(".show-all").Should().BeEmpty("there is no separate reset control left to press");
    }

    /// <summary>
    /// The empty states are different sentences because the SCOPES are. Scoped to a subject
    /// with no exception of its own, "every subject is offered on every date" would be FALSE —
    /// the owner does have an exception, just not for this subject. Both stay plain
    /// non-warning prose (AC-17 / FR-ED-14 dissolved).
    /// </summary>
    [TestMethod]
    public void FilteredEmpty_SaysTheSubjectHasNone_NeverThePageDefaultEmptyState()
    {
        var harness = RenderPage(
            query: $"?gradeLevelId={GradeId:D}&topicId={TopicId:D}",
            exceptionsJson: ExceptionsJson(ExceptionJson(
                Guid.NewGuid(), TopicBId, "None", new DateOnly(2027, 5, 1), new DateOnly(2027, 5, 14))),
            gradeSubjectsJson: TwoSubjectsJson());

        harness.Page.WaitForAssertion(() => harness.Page.Markup.Should()
            .Contain("No exceptions for Mathematics — this subject is offered on every date."));

        harness.Page.Markup.Should().NotContain("No exceptions — every subject is offered on every date.",
            "the owner HAS an exception, so the page's default empty state would be a lie here");
        harness.Page.FindAll(".exception-row").Should().BeEmpty();
        harness.Page.FindComponents<FluentMessageBar>().Should().BeEmpty(
            "one subject without an exception is normal, never a warning");
        ScopeLabel(harness.Page).Should().Be("Mathematics",
            "and the scope is still reachable, so the empty view is never a dead end");
        harness.Page.Markup.Should().Contain("1 exception",
            "the badge still counts the owner's set: the scope hides rows, it does not remove them");
    }

    /// <summary>
    /// REGRESSION GUARD for the default path: reached with no subject in the query string
    /// (the page's own landing, and the state "All subjects" produces), the list is the
    /// owner's full set — and the filter SAYS it, because "All subjects" is a resolved
    /// selection rather than a blank. That is the observable difference from v3, where the
    /// unfiltered state was the absence of a filter and the page had to be read to find out.
    /// </summary>
    [TestMethod]
    public void NoTopicInTheQuery_ListsEverySubjectsExceptions()
    {
        var harness = RenderPage(
            query: $"?gradeLevelId={GradeId:D}",
            exceptionsJson: TwoTopicExceptionsJson(),
            gradeSubjectsJson: TwoSubjectsJson());

        harness.Page.WaitForAssertion(() => harness.Page.FindAll(".exception-row").Should().HaveCount(2));

        harness.Page.FindAll(".exception-topic").Select(t => t.TextContent.Trim()).Should()
            .BeEquivalentTo(new[] { "Mathematics", "Biology" });
        ScopeLabel(harness.Page).Should().Be("All subjects",
            "no query subject means the whole set, and the control names that state explicitly");
    }

    /// <summary>
    /// P2-1, on ONE state. v3 proved this twice — once that the add panel armed nothing, once
    /// that the list stayed unfiltered — because the subject lived in two fields that could
    /// disagree. With one field the two proofs are the same proof, and this is it: a query
    /// naming a subject this owner does not list leaves the view on the owner's whole set
    /// (scoping to the raw query value would blank a list that has rows, under a subject the
    /// page cannot even name), and the closed add section is the observable proof that nothing
    /// is armed behind it.
    /// </summary>
    [TestMethod]
    public void QueryString_TopicTheOwnerDoesNotList_LeavesTheScopeUnchanged_AndArmsNothing()
    {
        // Grade 5 lists Mathematics only; the query names grade 6's Biology.
        var harness = RenderPage(
            query: $"?gradeLevelId={GradeId:D}&topicId={TopicBId:D}",
            exceptionsJson: TwoTopicExceptionsJson());

        harness.Page.WaitForAssertion(() => harness.Page.FindAll(".exception-row").Should().HaveCount(2,
            "an unresolvable query subject is no scope at all — the rows stay"));

        ScopeLabel(harness.Page).Should().Be("All subjects",
            "the page cannot scope to a subject this owner does not list, so it does not claim to");
        harness.Page.Find(".add-prompt").Should().NotBeNull(
            "and nothing is armed behind the scenes either — there is no second subject state to arm");
        harness.Page.FindAll(".add-exception").Should().BeEmpty("so there is no write affordance to press");
    }

    /// <summary>
    /// REPLACES v3's <c>AddForAnotherSubject_ClearsTheFilter_…</c> (spec §9's map), and is
    /// the round's own guard on the thing it exists to fix. v3 needed a workaround — clear
    /// the filter after a successful write to a NON-scoped subject, or the re-read would show
    /// a list the new row was not in — and that workaround existed only because the add
    /// panel could name a different subject than the list showed. With one subject control
    /// the case cannot arise, so the test is no longer a workaround's regression guard: it is
    /// the assertion that there is nothing to work around.
    /// </summary>
    [TestMethod]
    public async Task ThereIsExactlyOneSubjectControl_AndNoSeparateReset()
    {
        var harness = RenderPage(
            query: $"?gradeLevelId={GradeId:D}&topicId={TopicId:D}",
            exceptionsJson: TwoTopicExceptionsJson(),
            gradeSubjectsJson: TwoSubjectsJson());

        AwaitSubjectFilter(harness.Page);
        harness.Page.WaitForAssertion(() => harness.Page.FindAll(".exception-row").Should().HaveCount(1));

        // EXACTLY ONE control on the page holds a subject, and it is the filter's.
        harness.Page.FindComponents<FluentSelect<EnrollmentExceptions.SubjectOption>>().Should().HaveCount(1);
        harness.Page.FindComponents<FluentSelect<SubjectDto>>().Should().BeEmpty(
            "v3's add panel carried a SECOND subject picker over the owner's SubjectDto list — the duplication");
        harness.Page.FindComponents<FluentSelect<GradeLevelDto>>().Should().HaveCount(1,
            "the page's other pickers are the owner and the owner KIND — neither holds a subject");

        // No separate reset control, and no line naming the subset: the filter is both.
        harness.Page.FindAll(".show-all").Should().BeEmpty("a filter is reset by changing the filter");
        harness.Page.FindAll(".filter-note").Should().BeEmpty(
            "and no second line restates the scope the control already shows");

        // A write can only ever target the scoped subject — the filter's.
        var addPanel = harness.Page.Find(".add-panel").TextContent;
        addPanel.Should().Contain("Add exception for Mathematics");
        addPanel.Should().NotContain("Pick a subject", "the add section has no subject picker of its own");

        await FillAddSectionAsync(harness.Page);
        harness.Page.Find(".add-exception").Click();
        harness.Page.WaitForAssertion(() => harness.Handler.Calls.Should()
            .Contain(c => c.Method == "POST" && c.Url == $"{ExceptionsUrl}/bulk"));
        harness.Handler.Calls.Single(c => c.Method == "POST").Body.Should()
            .Contain($"\"topicId\":\"{TopicId:D}\"",
                "the only subject the page can write is the one the list is showing");
    }

    /// <summary>
    /// REPLACES v3's <c>FailedAdd_LeavesTheFilterAlone</c> (spec §9's map). The promise is
    /// unchanged and now stronger: a REJECTED write changes only what the server rejected.
    /// The scope survives, so the reader stays on the subject they came for, still has the
    /// span and the reason they typed, and the server's message is the only new thing on
    /// screen — a failure must not also move them somewhere else.
    /// </summary>
    [TestMethod]
    public async Task FailedAdd_LeavesTheScopeUnchanged()
    {
        const string serverMessage = "This subject is already excepted for that part and span.";

        var harness = RenderPage(
            query: $"?gradeLevelId={GradeId:D}&topicId={TopicId:D}",
            exceptionsJson: ExceptionsJson(
                ExceptionJson(Guid.NewGuid(), TopicId, "Terms", Term1Start, Term1End, reason: "Staffing")),
            gradeSubjectsJson: TwoSubjectsJson(),
            addStatus: HttpStatusCode.Conflict,
            addBody: $"{{\"message\":\"{serverMessage}\"}}");

        AwaitSubjectFilter(harness.Page);
        harness.Page.WaitForAssertion(() => harness.Page.FindAll(".exception-row").Should().HaveCount(1));

        await FillAddSectionAsync(harness.Page);
        harness.Page.Find(".add-exception").Click();

        harness.Page.WaitForAssertion(() => harness.Page.Markup.Should().Contain(serverMessage,
            "the rejected write surfaces the server's message"));
        ScopeLabel(harness.Page).Should().Be("Mathematics", "a failed write is no reason to change the view");
        harness.Page.FindAll(".exception-row").Should().HaveCount(1,
            "the list is still the scoped one the user entered on");
        harness.Page.FindAll(".span-group").Should().BeEmpty(
            "the 1st term's span is DERIVED, so what survives the rejection is the CHOSEN SEQUENCE, "
            + "not a typed range (v6 §11.2 decision 17)");
        PositionBoxes(harness.Page).Should().Contain(b => b.Instance.Value,
            "and the choice survives a rejection, so the retry is one click, not seven fields");
    }

    /// <summary>
    /// The pickers' <c>/check</c> is a courtesy, not the gate: when the picked subject is
    /// already excepted on the span's anchor date the page says so before the write, and the
    /// server stays the authority (a duplicate is still a 409).
    /// </summary>
    [TestMethod]
    public async Task PickersCheck_ShowsTheHintWhenTheDateIsAlreadyExcepted()
    {
        var harness = RenderPage(query: GradeOwnerQuery, exceptionsJson: "[]", checkJson: "{\"excepted\":true}");

        AwaitSubjectFilter(harness.Page);
        await FillAddSectionAsync(harness.Page);

        harness.Page.WaitForAssertion(() => harness.Page.Markup.Should()
            .Contain("already excepted on 1 Mar 2027"));
        harness.Handler.Calls.Should().Contain(
            c => c.Method == "GET" && c.Url.StartsWith($"{ExceptionsUrl}/check", StringComparison.Ordinal),
            "the check goes to the dedicated containment route");
    }

    // ── Accessibility: the states that change under the reader ────────────

    /// <summary>
    /// The prose that changes under the reader's feet is announced, not merely redrawn, and
    /// the check transfers to the group that names the range: the ends it labels must sit
    /// inside it, each carrying its own label beneath its input, and the openness must be
    /// named in the placeholders rather than left to a button's absence. (The closed-section
    /// prompt's own live region is asserted by
    /// <c>AllSubjects_DisablesTheAddSection_WithAPrompt_AndWritesNothing</c>, which is the
    /// test that renders it — a scoped view has an OPEN add section, so there is no prompt here.)
    /// </summary>
    [TestMethod]
    public void ScopedEmptyState_AndRangeGroup_CarryTheirAccessibleNames()
    {
        // Scoped to a subject, and this owner has no exceptions at all: the SCOPED empty
        // sentence, with the add section open beside it.
        var harness = RenderPage(
            query: $"?gradeLevelId={GradeId:D}&topicId={TopicId:D}",
            exceptionsJson: "[]",
            gradeSubjectsJson: TwoSubjectsJson());

        harness.Page.WaitForAssertion(() => harness.Page.Markup.Should()
            .Contain("No exceptions for Mathematics — this subject is offered on every date."));

        AssertLiveRegion(harness.Page.Find(".exceptions-empty"));
        harness.Page.FindAll(".add-prompt").Should().BeEmpty(
            "a scoped view has an open add section — the prompt belongs to the unscoped one");

        harness.Page.FindAll("[aria-label='Position']").Should().BeEmpty("Any date has no position to name");
        var range = harness.Page.Find(".span-group");
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

        // The add section is the house primitive with every label BENEATH its input (Q3/Q7),
        // and the part its rows name is the same vocabulary the LIST rows use.
        harness.Page.FindAll(".add-form .form-row-label").Select(l => l.TextContent.Trim()).Should()
            .BeEquivalentTo(new[] { "Fill from", "Not offered from", "Not offered to", "Reason (optional)" },
                "the add section is Fill from / the two range ends / Reason before a real part is "
                + "chosen — and the range's own header is combined into its ends, not a third label");
        DivisionLabel(harness.Page).Should().Be("Any date",
            "and the part is spelled the same way the list rows spell it — one vocabulary, two surfaces");
    }
}
