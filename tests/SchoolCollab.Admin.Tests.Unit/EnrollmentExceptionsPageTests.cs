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

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for the <c>EnrollmentExceptions</c> management page
/// (<c>/students/enrollment-exceptions</c>, subject-period-exception-model.md v3 §5.1)
/// — AC-17.
///
/// <para>The page is the ONLY place an exception is edited, so these tests pin what a
/// management surface must not get wrong: zero exceptions is the normal state (no
/// warning), a term-shaped and a plain-window row both render their part and their span,
/// open bounds read <c>from …</c> / <c>to …</c>, the last exception stays removable, an
/// incomplete add writes NOTHING, the page offers no Save control at all (writes are
/// immediate), a server 409 surfaces the server's own message, the group owner side is
/// gated by <c>FEATURE:EnableActivityGroups</c>, the period-part picker offers only the
/// divisions the tenant has (OOD-1), and the entry point's query string lands with owner
/// and subject pre-selected (§5.2 row 1 / N4).</para>
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

    /// <summary>Term 1's dates — the span a term pick fills (§5.1).</summary>
    private static readonly DateOnly Term1Start = new(2027, 3, 1);
    private static readonly DateOnly Term1End = new(2027, 6, 30);

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
        Guid id, string name, Guid? parentPeriodId, string division, string start, string end) => new()
    {
        ["id"] = id,
        ["name"] = name,
        ["startDate"] = start,
        ["endDate"] = end,
        ["status"] = "Active",
        ["parentPeriodId"] = parentPeriodId,
        ["nextPeriodId"] = (Guid?)null,
        ["division"] = division,
        ["activationToleranceDays"] = (int?)null,
        ["createdAt"] = DateTimeOffset.UnixEpoch,
        ["updatedAt"] = DateTimeOffset.UnixEpoch,
    };

    /// <summary>A tenant whose year is divided into terms AND semesters.</summary>
    private static string PeriodsWithTermJson() => JsonSerializer.Serialize(new[]
    {
        PeriodJson(YearId, "2027 Academic Year", null, "None", "2027-01-01", "2027-12-31"),
        PeriodJson(Term1Id, "Term 1", YearId, "Terms", "2027-03-01", "2027-06-30"),
    });

    /// <summary>A tenant with plain, undivided years — OOD-1's case: the part picker must
    /// never offer an empty Term/Semester list.</summary>
    private static string PeriodsWithoutDivisionsJson() => JsonSerializer.Serialize(new[]
    {
        PeriodJson(YearId, "2027 Academic Year", null, "None", "2027-01-01", "2027-12-31"),
    });

    /// <summary>One enrollment exception row: a period PART plus the span the subject is
    /// NOT offered in. <c>division</c> crosses the DTO boundary as its name (§2.1).</summary>
    private static Dictionary<string, object?> ExceptionJson(
        Guid id, Guid topicId, string division, DateOnly? start, DateOnly? end, string? reason = null) => new()
    {
        ["id"] = id,
        ["gradeLevelId"] = GradeId,
        ["activityGroupId"] = (Guid?)null,
        ["topicId"] = topicId,
        ["division"] = division,
        ["startDate"] = start?.ToString("yyyy-MM-dd"),
        ["endDate"] = end?.ToString("yyyy-MM-dd"),
        ["reason"] = reason,
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
            .Map("POST", ExceptionsUrl, addStatus, addBody ?? $"{{\"id\":\"{Guid.NewGuid():D}\"}}");

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

    // ── Driving the pickers ────────────────────────────────────────────────

    /// <summary>
    /// Picks an option on a FluentUI select. The pickers are web components whose options
    /// are NOT materialised in markup while closed, so selection is driven through the
    /// component's own bound callback — the same shape the v1 exceptions-dialog tests
    /// used. <c>InvokeAsync</c> puts the change on the renderer, which is also what makes
    /// the page's <c>:after</c> side-effects run.
    /// </summary>
    private static Task SelectAsync<TItem>(IRenderedComponent<EnrollmentExceptions> page, TItem item)
        where TItem : class
        => page.InvokeAsync(() => page.FindComponent<FluentSelect<TItem>>()
            .Instance.SelectedOptionChanged.InvokeAsync(item));

    /// <summary>Waits until the owner's subjects have loaded — the add panel's readiness
    /// signal (the list may legitimately be empty).</summary>
    private static void AwaitAddPanel(IRenderedComponent<EnrollmentExceptions> page) =>
        page.WaitForAssertion(() =>
        {
            var select = page.FindComponents<FluentSelect<SubjectDto>>().FirstOrDefault();
            (select?.Instance.Items?.Any() ?? false).Should()
                .BeTrue("the owner's subjects must load before the add panel is usable");
        });

    /// <summary>Waits for a period-part option to exist — the picker's options follow the
    /// tenant's period hierarchy (OOD-1), which loads after the subjects.</summary>
    private static EnrollmentExceptions.DivisionOption AwaitDivisionOption(
        IRenderedComponent<EnrollmentExceptions> page, string label)
    {
        EnrollmentExceptions.DivisionOption? option = null;
        page.WaitForAssertion(() =>
        {
            var select = page.FindComponents<FluentSelect<EnrollmentExceptions.DivisionOption>>().FirstOrDefault();
            option = select?.Instance.Items?.FirstOrDefault(o => o.Label == label);
            option.Should().NotBeNull($"the part picker must offer '{label}' for this tenant");
        });
        return option!;
    }

    /// <summary>Waits for the term/semester picker to offer the given period.</summary>
    private static PeriodDto AwaitSpanPeriod(IRenderedComponent<EnrollmentExceptions> page, Guid periodId)
    {
        PeriodDto? period = null;
        page.WaitForAssertion(() =>
        {
            var select = page.FindComponents<FluentSelect<PeriodDto>>().FirstOrDefault();
            period = select?.Instance.Items?.FirstOrDefault(p => p.Id == periodId);
            period.Should().NotBeNull("the span picker offers this part's periods");
        });
        return period!;
    }

    /// <summary>Drives the whole add form: part, term (which resolves the dates), subject.</summary>
    private static async Task FillAddFormAsync(IRenderedComponent<EnrollmentExceptions> page)
    {
        await SelectAsync(page, AwaitDivisionOption(page, "Term"));
        await SelectAsync(page, AwaitSpanPeriod(page, Term1Id));
        await SelectAsync(page, page.FindComponent<FluentSelect<SubjectDto>>().Instance.Items!.Single());
    }

    /// <summary>The add panel's subject picker — the one that shows the query string's
    /// subject.</summary>
    private static FluentSelect<SubjectDto> SubjectPicker(IRenderedComponent<EnrollmentExceptions> page) =>
        page.FindComponent<FluentSelect<SubjectDto>>().Instance;

    /// <summary>Switches the owner to the flag-gated group side and picks the group.</summary>
    private static async Task SelectGroupOwnerAsync(IRenderedComponent<EnrollmentExceptions> page)
    {
        await SelectAsync(page, page.FindComponent<FluentSelect<EnrollmentExceptions.OwnerTypeOption>>()
            .Instance.Items!.Single(o => o.Value == "ActivityGroup"));
        await SelectAsync(page, page.FindComponent<FluentSelect<ActivityGroupDto>>().Instance.Items!.Single());
    }

    /// <summary>One of the two date pickers, by its own <c>Id</c> — the page renders two, and
    /// "the first picker" is exactly the assumption a Clear-button test must not make.</summary>
    private static FluentDatePicker DatePicker(IRenderedComponent<EnrollmentExceptions> page, string id) =>
        page.FindComponents<FluentDatePicker>().Single(p => p.Instance.Id == id).Instance;

    /// <summary>The Clear button of one date field, found INSIDE that field's markup: a field is
    /// <c>[picker][Clear]</c>, so the button belongs to that date rather than to "the first Clear
    /// on the page". Fields are indexed in markup order — 0 is the span's start bound, 1 its end.</summary>
    private static IElement ClearButton(IRenderedComponent<EnrollmentExceptions> page, int fieldIndex)
    {
        var button = page.FindAll(".date-field")[fieldIndex].QuerySelector("fluent-button.clear-date");
        button.Should().NotBeNull("each date field carries its own Clear button");
        return button!;
    }

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
        var harness = RenderPage(exceptionsJson: "[]");

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
    /// and a plain window (no part). Both render their part AND their span, and the count
    /// badge counts ROWS — never a period list (§5.2).
    /// </summary>
    [TestMethod]
    public void TermShapedAndPlainWindowRows_RenderTheirPartAndSpan()
    {
        var harness = RenderPage(exceptionsJson: ExceptionsJson(
            ExceptionJson(Guid.NewGuid(), TopicId, "Terms", Term1Start, Term1End, reason: "Staffing"),
            ExceptionJson(Guid.NewGuid(), TopicId, "None", new DateOnly(2027, 5, 1), new DateOnly(2027, 5, 14))));

        harness.Page.WaitForAssertion(() => harness.Page.Markup.Should().Contain("1 Mar 2027 – 30 Jun 2027"));

        var markup = harness.Page.Markup;
        markup.Should().Contain("Mathematics", "the row names the subject the exception belongs to");
        harness.Page.FindAll(".exception-row fluent-badge").Select(b => b.TextContent.Trim()).Should()
            .BeEquivalentTo(new[] { "Term", "—" },
                "each row names its period part, and a not-term-scoped row gets an honest placeholder");
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
        var harness = RenderPage(exceptionsJson: ExceptionsJson(
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
        var harness = RenderPage(exceptionsJson: ExceptionsJson(
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
    /// An incomplete add writes NOTHING: with no subject and no span the affordance is
    /// disabled and firing it posts nothing — an exception with neither bound would mean
    /// "never offered", which is a different concept (§2.2).
    /// </summary>
    [TestMethod]
    public void AddWithNothingSelected_WritesNothing()
    {
        var harness = RenderPage(exceptionsJson: "[]");

        AwaitAddPanel(harness.Page);

        harness.Page.Find(".add-exception").GetAttribute("disabled").Should().NotBeNull(
            "the write affordance stays disabled until a subject and at least one bound are picked");
        harness.Page.Find(".add-exception").Click();

        harness.Handler.Calls.Should().NotContain(c => c.Method == "POST",
            "an incomplete add must not reach the API");
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
        var harness = RenderPage(exceptionsJson: "[]");

        AwaitAddPanel(harness.Page);

        var live = new List<Dictionary<string, object?>>();
        harness.Handler.MapDynamic("GET", GradeExceptionsUrl, HttpStatusCode.OK, () => ExceptionsJson(live.ToArray()));
        harness.Handler.MapDynamic("POST", ExceptionsUrl, HttpStatusCode.Created, () =>
        {
            live.Add(ExceptionJson(createdId, TopicId, "Terms", Term1Start, Term1End));
            return $"{{\"id\":\"{createdId:D}\"}}";
        });

        await FillAddFormAsync(harness.Page);

        harness.Page.WaitForAssertion(() => harness.Page.Find(".add-exception").GetAttribute("disabled").Should()
            .BeNull("a subject plus a resolved span is a complete write"));

        harness.Page.Find(".add-exception").Click();

        harness.Page.WaitForAssertion(() => harness.Handler.Calls.Should()
            .Contain(c => c.Method == "POST" && c.Url == ExceptionsUrl));

        var post = harness.Handler.Calls.Single(c => c.Method == "POST");
        post.Body.Should().Contain($"\"gradeLevelId\":\"{GradeId:D}\"");
        post.Body.Should().Contain("\"activityGroupId\":null", "a grade-owned exception names no group");
        post.Body.Should().Contain($"\"topicId\":\"{TopicId:D}\"");
        post.Body.Should().Contain("\"division\":1", "the part travels as the Terms enum value");
        post.Body.Should().Contain($"\"startDate\":\"{Term1Start:yyyy-MM-dd}\"");
        post.Body.Should().Contain($"\"endDate\":\"{Term1End:yyyy-MM-dd}\"",
            "the term picker resolved to that period's dates");
        post.Body.Should().NotContain("periodId", "the body carries a period PART, never a period instance");

        harness.Page.WaitForAssertion(() => harness.Page.Markup.Should()
            .Contain("1 Mar 2027 – 30 Jun 2027", "the list re-reads the server after the write"));
    }

    /// <summary>
    /// The Clear affordances are real controls, not decoration: firing one unbinds ITS OWN date
    /// (the picker stops showing it) and leaves the other bound, and the write affordance follows —
    /// clearing the last bound re-closes Add, because an exception with neither bound would mean
    /// "never offered", a different concept (§2.2).
    /// </summary>
    [TestMethod]
    public async Task ClearDate_UnbindsItsOwnDate_AndRecomputesTheWriteAffordance()
    {
        var harness = RenderPage(exceptionsJson: "[]");

        AwaitAddPanel(harness.Page);
        await FillAddFormAsync(harness.Page);

        harness.Page.WaitForAssertion(() => harness.Page.Find(".add-exception").GetAttribute("disabled").Should()
            .BeNull("a subject plus the term's resolved span is a complete write"));
        DatePicker(harness.Page, "exception-start-date").Value.Should()
            .Be(Term1Start.ToDateTime(TimeOnly.MinValue), "the term pick resolved the start bound");
        DatePicker(harness.Page, "exception-end-date").Value.Should()
            .Be(Term1End.ToDateTime(TimeOnly.MinValue), "and the end bound");

        ClearButton(harness.Page, 0).Click();

        harness.Page.WaitForAssertion(() => DatePicker(harness.Page, "exception-start-date").Value.Should()
            .BeNull("Clear unbinds the date of the field it sits in"));
        DatePicker(harness.Page, "exception-end-date").Value.Should()
            .Be(Term1End.ToDateTime(TimeOnly.MinValue), "each Clear button clears its own bound only");
        harness.Page.Find(".add-exception").GetAttribute("disabled").Should().BeNull(
            "the end bound is still set, so the write stays open");

        ClearButton(harness.Page, 1).Click();

        harness.Page.WaitForAssertion(() => DatePicker(harness.Page, "exception-end-date").Value.Should().BeNull());
        harness.Page.WaitForAssertion(() => harness.Page.Find(".add-exception").GetAttribute("disabled").Should()
            .NotBeNull("with no bound left there is nothing to write — neither bound means \"never offered\""));
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
            exceptionsJson: "[]",
            addStatus: HttpStatusCode.Conflict,
            addBody: $"{{\"message\":\"{serverMessage}\"}}");

        AwaitAddPanel(harness.Page);
        await FillAddFormAsync(harness.Page);

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
        var harness = RenderPage(exceptionsJson: "[]");

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
        var harness = RenderPage(activityGroupsEnabled: false, exceptionsJson: "[]");

        AwaitAddPanel(harness.Page);

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
        var harness = RenderPage(activityGroupsEnabled: true, exceptionsJson: "[]");

        AwaitAddPanel(harness.Page);

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
    /// half-filled write must not travel with the switch: the span's bounds, the reason and the
    /// resolved period are cleared along with the subject (ClearAddSelection) — not just the subject
    /// the new owner's list cannot resolve, which the reload would have dropped anyway.
    /// </summary>
    [TestMethod]
    public async Task ChangingOwner_ResetsTheAddPanel()
    {
        var harness = RenderPage(exceptionsJson: "[]");

        AwaitAddPanel(harness.Page);
        await FillAddFormAsync(harness.Page);
        ReasonField(harness.Page).Change("Staffing");

        harness.Page.WaitForAssertion(() => harness.Page.Find(".add-exception").GetAttribute("disabled").Should()
            .BeNull("the panel is armed for the owner that is about to change"));
        ReasonField(harness.Page).GetAttribute("value").Should().Be("Staffing");
        DatePicker(harness.Page, "exception-start-date").Value.Should().NotBeNull();

        // Owner → the OTHER grade level: an owner change, not just a subject change.
        await SelectAsync(harness.Page, harness.Page
            .FindComponent<FluentSelect<GradeLevelDto>>().Instance.Items!.Single(g => g.Id == GradeBId));

        harness.Page.WaitForAssertion(() => harness.Handler.Calls.Should()
            .Contain(c => c.Url == $"{ExceptionsUrl}?gradeLevelId={GradeBId:D}",
                "the new owner's exceptions and subjects are the ones that load"));

        // WaitForAssertion on purpose: the panel state is pushed to the child components by the
        // render that follows the load, which can land after the await on the picker returns.
        harness.Page.WaitForAssertion(() =>
        {
            ReasonField(harness.Page).GetAttribute("value").Should().BeNullOrEmpty(
                "the reason belonged to the previous owner's write");
            DatePicker(harness.Page, "exception-start-date").Value.Should().BeNull("the span's bounds go with it");
            DatePicker(harness.Page, "exception-end-date").Value.Should().BeNull();
            harness.Page.FindComponent<FluentSelect<PeriodDto>>().Instance.SelectedOption.Should().BeNull(
                "the resolved period is no longer the picked one");
            SubjectPicker(harness.Page).SelectedOption.Should().BeNull("and no subject is armed");
            harness.Page.Find(".add-exception").GetAttribute("disabled").Should().NotBeNull(
                "nothing is armed for the new owner, so there is nothing to write");
        });
    }

    /// <summary>
    /// OOD-1 (settled): the part picker offers only the divisions the tenant actually has,
    /// plus an explicit "not term-scoped" option — never an empty Term/Semester list, and
    /// never a magic-looking <c>None</c>.
    /// </summary>
    [TestMethod]
    public void PartPicker_OffersTheDivisionsTheTenantHas_PlusNotTermScoped()
    {
        var harness = RenderPage(exceptionsJson: "[]");

        AwaitDivisionOption(harness.Page, "Term");

        harness.Page.FindComponent<FluentSelect<EnrollmentExceptions.DivisionOption>>()
            .Instance.Items!.Select(o => o.Label).Should()
            .BeEquivalentTo(new[] { "Term", "Not term-scoped" });
    }

    /// <summary>The same rule on the other tenant shape: years with no divisions yield only
    /// the honest "not term-scoped" option, and the span is free dates.</summary>
    [TestMethod]
    public void PartPicker_TenantWithoutDivisions_OffersOnlyNotTermScoped()
    {
        var harness = RenderPage(exceptionsJson: "[]", periodsJson: PeriodsWithoutDivisionsJson());

        AwaitAddPanel(harness.Page);
        harness.Page.WaitForAssertion(() => harness.Handler.Calls.Should()
            .Contain(c => c.Url == PeriodsUrl, "the period hierarchy is what decides the parts"));

        harness.Page.FindComponent<FluentSelect<EnrollmentExceptions.DivisionOption>>()
            .Instance.Items!.Select(o => o.Label).Should()
            .BeEquivalentTo(new[] { "Not term-scoped" });
        harness.Page.FindComponents<FluentSelect<PeriodDto>>().Should().BeEmpty(
            "there is no term to pick when the tenant has none");
    }

    /// <summary>
    /// P2-2 / §5.1 "filtered by the owner's rule (§4.2)": a group-owned exception must
    /// match the group's <c>EnrollmentSpan</c> (FR-56), so a <c>Termly</c> group is
    /// offered the Term part ONLY — the tenant has terms, but that tenant rule does not
    /// let this owner store "not term-scoped", and offering it would buy a 422.
    /// </summary>
    [TestMethod]
    public async Task PartPicker_GroupOwned_TermlyGroup_OffersOnlyTheSpansRequiredPart()
    {
        var harness = RenderPage(activityGroupsEnabled: true, exceptionsJson: "[]");

        AwaitAddPanel(harness.Page);
        await SelectGroupOwnerAsync(harness.Page);

        harness.Page.WaitForAssertion(() =>
            harness.Page.FindComponent<FluentSelect<EnrollmentExceptions.DivisionOption>>()
                .Instance.Items!.Select(o => o.Label).Should()
                .BeEquivalentTo(new[] { "Term" },
                    "FR-56 lets a Termly group store Terms and nothing else"));
    }

    /// <summary>
    /// The other half of FR-56: a group whose span is not period-aligned may store
    /// <c>None</c> only, so "not term-scoped" is the ONE part on offer — the tenant's
    /// terms are not.
    /// </summary>
    [TestMethod]
    public async Task PartPicker_GroupOwned_UnAlignedSpan_OffersOnlyNotTermScoped()
    {
        var harness = RenderPage(
            activityGroupsEnabled: true,
            exceptionsJson: "[]",
            groupSpan: "WholeAcademicYear");

        AwaitAddPanel(harness.Page);
        await SelectGroupOwnerAsync(harness.Page);

        harness.Page.WaitForAssertion(() =>
            harness.Page.FindComponent<FluentSelect<EnrollmentExceptions.DivisionOption>>()
                .Instance.Items!.Select(o => o.Label).Should()
                .BeEquivalentTo(new[] { "Not term-scoped" },
                    "FR-56 lets a non-period-aligned group store None only"));
    }

    /// <summary>
    /// N4 / §5.2 row 1: the landing's kebab navigates here with the owner AND the subject in
    /// the query string, and the page must LAND pre-selected — the owner whose exceptions are
    /// listed, and the subject the add panel points at.
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

        var subjectPicker = harness.Page.FindComponent<FluentSelect<SubjectDto>>().Instance;
        subjectPicker.SelectedOption.Should().NotBeNull(
            "the subject from the query string is pre-selected in the add panel");
        subjectPicker.SelectedOption!.Id.Should().Be(TopicId);
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
    /// query string must still land pre-selected — REGRESSION GUARD, not a
    /// discriminator. The pending field this page applies after the owner-data load was
    /// introduced for the case below (a subject the owner does not list); this scenario
    /// also passes against the eager version, because the picker's reset path needs a
    /// render BETWEEN the stale list being cleared and the reload arriving, and Blazor
    /// renders this page only once <c>OnParametersSetAsync</c> has completed.
    /// </summary>
    [TestMethod]
    public void QueryStringChange_KeepsThePreSelection_WhenTheTopicIsNotInThePreviousOwnersList()
    {
        var harness = RenderPage(
            query: $"?gradeLevelId={GradeId:D}&topicId={TopicId:D}",
            exceptionsJson: "[]");

        harness.Page.WaitForAssertion(() => SubjectPicker(harness.Page).SelectedOption?.Id.Should()
            .Be(TopicId, "the first load pre-selects the query string's subject"));

        // The SAME page instance is reused for a different query string whose subject is
        // absent from grade 5's list — the shape the review flagged as losing the
        // pre-selection, kept as the guard that the load path stays render-free.
        Services.GetRequiredService<NavigationManager>()
            .NavigateTo($"{PageUrl}?gradeLevelId={GradeBId:D}&topicId={TopicBId:D}");

        harness.Page.WaitForAssertion(() => harness.Handler.Calls.Should()
            .Contain(c => c.Url == $"{ExceptionsUrl}?gradeLevelId={GradeBId:D}",
                "the in-page query change must actually switch the owner"));
        harness.Page.WaitForAssertion(() => SubjectPicker(harness.Page).SelectedOption?.Id.Should()
            .Be(TopicBId, "the pre-selection survives the new owner's subject list loading"));
    }

    /// <summary>
    /// P2-1's DISCRIMINATOR: the query string's subject lands only when this owner
    /// actually lists it. A topic the picker cannot show must not leave a hidden subject
    /// armed, or a complete-looking add panel could write a subject the user never saw —
    /// measured against the eager implementation, where the Add affordance came up
    /// ENABLED with the unlisted subject behind it. (The picker is fed by date-effective
    /// listings, so a subject whose bridge window starts later is exactly this case.)
    /// </summary>
    [TestMethod]
    public async Task QueryString_TopicTheOwnerDoesNotList_LeavesNothingArmed()
    {
        var harness = RenderPage(
            query: $"?gradeLevelId={GradeId:D}&topicId={TopicBId:D}",
            exceptionsJson: "[]");

        AwaitAddPanel(harness.Page);

        // Everything else the write needs — a part and the span it resolves to.
        await SelectAsync(harness.Page, AwaitDivisionOption(harness.Page, "Term"));
        await SelectAsync(harness.Page, AwaitSpanPeriod(harness.Page, Term1Id));

        harness.Page.Find(".add-exception").GetAttribute("disabled").Should().NotBeNull(
            "grade 5 does not list that subject, so nothing is pre-selected and the write stays closed");
    }

    // ── §5.2 row 1: the entry point's subject filter ────────────────────────

    /// <summary>
    /// §5.2 row 1 is PER-SUBJECT: the kebab that lands here names ONE subject, so the list
    /// shows that subject's rows — not the owner's whole set with a subject merely picked in
    /// the add panel beside it. The owner still HAS both rows (the badge is the owner's, and
    /// stays the owner's: under a filter it is what says the rows are a subset), and the
    /// page says which subset it is showing.
    /// </summary>
    [TestMethod]
    public void QueryString_FiltersTheListToThePreSelectedSubject()
    {
        var harness = RenderPage(
            query: $"?gradeLevelId={GradeId:D}&topicId={TopicId:D}",
            exceptionsJson: TwoTopicExceptionsJson(),
            gradeSubjectsJson: TwoSubjectsJson());

        harness.Page.WaitForAssertion(() => harness.Page.FindAll(".exception-row").Should().HaveCount(1));

        harness.Page.FindAll(".exception-topic").Select(t => t.TextContent.Trim()).Should()
            .BeEquivalentTo(new[] { "Mathematics" },
                "the rows listed are the subject the entry point named, and nothing else");
        harness.Page.FindAll(".exception-remove").Should().HaveCount(1,
            "only the listed subject's rows are removable from this view");
        harness.Page.Markup.Should().Contain("2 exceptions",
            "the badge counts the OWNER's set — the filtered rows are a subset of it");
        harness.Page.Find(".filter-note").TextContent.Should()
            .Be("Showing exceptions for Mathematics only.",
                "a filtered list must say so, in the filtered view's own line");
        harness.Page.Find(".show-all").Should().NotBeNull("a filtered view is never a dead end");

        // The add panel is untouched by the filter (P2-1): the query's subject is still what
        // is armed there, so "Add" targets the subject the user came for.
        SubjectPicker(harness.Page).SelectedOption?.Id.Should().Be(TopicId);
    }

    /// <summary>
    /// The way back out: "Show all" lists the owner's whole set again — including the rows
    /// the filter was hiding — and takes its own line and control with it.
    /// </summary>
    [TestMethod]
    public void ShowAll_ClearsTheFilter_AndRestoresTheOwnersWholeSet()
    {
        var harness = RenderPage(
            query: $"?gradeLevelId={GradeId:D}&topicId={TopicId:D}",
            exceptionsJson: TwoTopicExceptionsJson(),
            gradeSubjectsJson: TwoSubjectsJson());

        harness.Page.WaitForAssertion(() => harness.Page.FindAll(".exception-row").Should().HaveCount(1));

        harness.Page.Find(".show-all").Click();

        harness.Page.WaitForAssertion(() => harness.Page.FindAll(".exception-row").Should().HaveCount(2,
            "clearing the filter lists every subject's exceptions again"));
        harness.Page.FindAll(".exception-topic").Select(t => t.TextContent.Trim()).Should()
            .BeEquivalentTo(new[] { "Mathematics", "Biology" });
        harness.Page.FindAll(".filter-note").Should().BeEmpty(
            "the line that named the subset goes with the subset");
        harness.Page.FindAll(".show-all").Should().BeEmpty("there is nothing left to clear");
    }

    /// <summary>
    /// The TWO empty states are different sentences. Filtered to a subject with no exception
    /// of its own, "every subject is offered on every date" would be FALSE — the owner does
    /// have an exception, just not for this subject. Both stay plain non-warning prose
    /// (AC-17 / FR-ED-14 dissolved).
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
        harness.Page.Find(".show-all").Should().NotBeNull(
            "the way back out is offered even when the filtered view is empty");
        harness.Page.Markup.Should().Contain("1 exception",
            "the badge still counts the owner's set: the subject filter hides rows, it does not remove them");
    }

    /// <summary>
    /// REGRESSION GUARD for the default path: reached with no subject in the query string
    /// (the page's own landing, and the state "Show all" produces), the list is the owner's
    /// full set and nothing on the page claims to be filtered.
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
        harness.Page.FindAll(".filter-note").Should().BeEmpty("nothing is filtered, so nothing says so");
        harness.Page.FindAll(".show-all").Should().BeEmpty("there is no filter to clear");
        SubjectPicker(harness.Page).SelectedOption.Should().BeNull(
            "no subject was named by the entry point, so the add panel arms nothing");
    }

    /// <summary>
    /// The filter is resolved against the owner's OWN subject list, exactly as the add
    /// panel's pre-selection is (P2-1). A query naming a subject this owner does not list
    /// leaves the list showing EVERYTHING: filtering on the raw query value would blank a
    /// list that has rows, under a subject the page cannot even name.
    /// </summary>
    [TestMethod]
    public void QueryString_TopicTheOwnerDoesNotList_LeavesTheListUnfiltered()
    {
        // Grade 5 lists Mathematics only; the query names grade 6's Biology.
        var harness = RenderPage(
            query: $"?gradeLevelId={GradeId:D}&topicId={TopicBId:D}",
            exceptionsJson: TwoTopicExceptionsJson());

        harness.Page.WaitForAssertion(() => harness.Page.FindAll(".exception-row").Should().HaveCount(2,
            "an unresolvable query subject is no filter at all — the rows stay"));

        harness.Page.FindAll(".filter-note").Should().BeEmpty(
            "the page cannot name a subject this owner does not list, so it does not claim to filter by it");
        SubjectPicker(harness.Page).SelectedOption.Should().BeNull(
            "the same unresolved subject arms nothing in the add panel (P2-1)");
    }

    /// <summary>
    /// Q2(a), owner-decided: the filter must not swallow a write. Adding an exception for a subject
    /// OTHER than the one the list is filtered to places the new row OUTSIDE the filter, so the
    /// re-read that follows a successful write would show a list the new row is not in — a
    /// successful write that reads as a no-op. A successful add of a non-filtered subject therefore
    /// drops the filter, and the row the user just wrote is the row they see.
    /// </summary>
    [TestMethod]
    public async Task AddForAnotherSubject_ClearsTheFilter_SoTheWrittenRowIsVisible()
    {
        var createdId = Guid.NewGuid();
        var harness = RenderPage(
            query: $"?gradeLevelId={GradeId:D}&topicId={TopicId:D}",
            exceptionsJson: ExceptionsJson(
                ExceptionJson(Guid.NewGuid(), TopicId, "Terms", Term1Start, Term1End, reason: "Staffing")),
            gradeSubjectsJson: TwoSubjectsJson());

        AwaitAddPanel(harness.Page);
        harness.Page.WaitForAssertion(() => harness.Page.FindAll(".exception-row").Should().HaveCount(1,
            "the list is filtered to the subject the entry point named"));

        var live = new List<Dictionary<string, object?>>
        {
            ExceptionJson(Guid.NewGuid(), TopicId, "Terms", Term1Start, Term1End, reason: "Staffing"),
        };
        harness.Handler.MapDynamic("GET", GradeExceptionsUrl, HttpStatusCode.OK,
            () => ExceptionsJson(live.ToArray()));
        harness.Handler.MapDynamic("POST", ExceptionsUrl, HttpStatusCode.Created, () =>
        {
            live.Add(ExceptionJson(createdId, TopicBId, "Terms", Term1Start, Term1End, reason: "Trip"));
            return $"{{\"id\":\"{createdId:D}\"}}";
        });

        // Arm the panel with the OTHER subject: the filter is on Mathematics, this write is Biology.
        await SelectAsync(harness.Page, AwaitDivisionOption(harness.Page, "Term"));
        await SelectAsync(harness.Page, AwaitSpanPeriod(harness.Page, Term1Id));
        await SelectAsync(harness.Page, SubjectPicker(harness.Page).Items!.Single(t => t.Id == TopicBId));

        harness.Page.WaitForAssertion(() => harness.Page.Find(".add-exception").GetAttribute("disabled").Should()
            .BeNull());
        harness.Page.Find(".add-exception").Click();

        harness.Page.WaitForAssertion(() => harness.Handler.Calls.Should()
            .Contain(c => c.Method == "POST" && c.Url == ExceptionsUrl));
        harness.Handler.Calls.Single(c => c.Method == "POST").Body.Should()
            .Contain($"\"topicId\":\"{TopicBId:D}\"", "the write is for the subject the list was NOT filtered to");

        harness.Page.WaitForAssertion(() => harness.Page.FindAll(".exception-topic")
            .Select(t => t.TextContent.Trim()).Should()
            .BeEquivalentTo(new[] { "Mathematics", "Biology" },
                "the row just written is visible: the successful add dropped the filter"));
        harness.Page.FindAll(".filter-note").Should().BeEmpty(
            "nothing is filtered any more, so nothing claims to be");
    }

    /// <summary>
    /// The other half of Q2(a): a REJECTED write clears nothing. The user is still on the subject
    /// the entry point named, the filter still says so, and the server's message is what changed —
    /// dropping the filter on a 409 would silently move the reader to a different view than the
    /// one they entered on, in response to a failure they still have to act on.
    /// </summary>
    [TestMethod]
    public async Task FailedAdd_LeavesTheFilterAlone()
    {
        const string serverMessage = "This subject is already excepted for that part and span.";

        var harness = RenderPage(
            query: $"?gradeLevelId={GradeId:D}&topicId={TopicId:D}",
            exceptionsJson: ExceptionsJson(
                ExceptionJson(Guid.NewGuid(), TopicId, "Terms", Term1Start, Term1End, reason: "Staffing")),
            gradeSubjectsJson: TwoSubjectsJson(),
            addStatus: HttpStatusCode.Conflict,
            addBody: $"{{\"message\":\"{serverMessage}\"}}");

        AwaitAddPanel(harness.Page);
        harness.Page.WaitForAssertion(() => harness.Page.FindAll(".exception-row").Should().HaveCount(1));

        await SelectAsync(harness.Page, AwaitDivisionOption(harness.Page, "Term"));
        await SelectAsync(harness.Page, AwaitSpanPeriod(harness.Page, Term1Id));
        await SelectAsync(harness.Page, SubjectPicker(harness.Page).Items!.Single(t => t.Id == TopicBId));

        harness.Page.Find(".add-exception").Click();

        harness.Page.WaitForAssertion(() => harness.Page.Markup.Should().Contain(serverMessage,
            "the rejected write surfaces the server's message"));
        harness.Page.Find(".filter-note").TextContent.Should()
            .Be("Showing exceptions for Mathematics only.", "a failed write is no reason to change the view");
        harness.Page.FindAll(".exception-row").Should().HaveCount(1,
            "the list is still the filtered one the user entered on");
        harness.Page.Find(".show-all").Should().NotBeNull("the way back out is still offered");
    }

    /// <summary>
    /// The pickers' <c>/check</c> is a courtesy, not the gate: when the picked subject is
    /// already excepted on the span's anchor date the page says so before the write, and the
    /// server stays the authority (a duplicate is still a 409).
    /// </summary>
    [TestMethod]
    public async Task PickersCheck_ShowsTheHintWhenTheDateIsAlreadyExcepted()
    {
        var harness = RenderPage(exceptionsJson: "[]", checkJson: "{\"excepted\":true}");

        AwaitAddPanel(harness.Page);
        await FillAddFormAsync(harness.Page);

        harness.Page.WaitForAssertion(() => harness.Page.Markup.Should()
            .Contain("already excepted on 1 Mar 2027"));
        harness.Handler.Calls.Should().Contain(
            c => c.Method == "GET" && c.Url.StartsWith($"{ExceptionsUrl}/check", StringComparison.Ordinal),
            "the check goes to the dedicated containment route");
    }

    // ── Accessibility: the states that change under the reader ────────────

    /// <summary>
    /// The prose that changes under the reader's feet — the filter line and BOTH empty states — is
    /// announced, not merely redrawn (the <c>FluentMessageBar</c> states were already live regions).
    /// And the "Span" header NAMES the group holding the two date fields it labels, instead of
    /// standing beside them as orphaned text.
    /// </summary>
    [TestMethod]
    public void FilterLine_EmptyState_AndSpanGroup_CarryTheirAccessibleNames()
    {
        // Filtered, and this owner has no exceptions at all: the FILTERED empty sentence.
        var harness = RenderPage(
            query: $"?gradeLevelId={GradeId:D}&topicId={TopicId:D}",
            exceptionsJson: "[]",
            gradeSubjectsJson: TwoSubjectsJson());

        harness.Page.WaitForAssertion(() => harness.Page.Markup.Should()
            .Contain("No exceptions for Mathematics — this subject is offered on every date."));

        AssertLiveRegion(harness.Page.Find(".filter-note"));
        AssertLiveRegion(harness.Page.Find(".exceptions-empty"));

        var spanGroup = harness.Page.Find(".span-group");
        spanGroup.GetAttribute("role").Should().Be("group");
        spanGroup.GetAttribute("aria-label").Should().Be("Span",
            "the visible Span header names the group of date fields it labels");
        spanGroup.QuerySelectorAll(".date-field").Length.Should().Be(2,
            "both date fields are members of the named group");

        // Out of the filter, the list is empty for the WHOLE owner — the page's own empty sentence,
        // which is the other half of the same promise.
        harness.Page.Find(".show-all").Click();

        harness.Page.WaitForAssertion(() => harness.Page.Markup.Should()
            .Contain("No exceptions — every subject is offered on every date."));
        AssertLiveRegion(harness.Page.Find(".exceptions-empty"));
    }
}
