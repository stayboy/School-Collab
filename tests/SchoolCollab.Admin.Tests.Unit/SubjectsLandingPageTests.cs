using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Reflection;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Admin.Shared.Components;
using SchoolCollab.Admin.Shared.Components.Dialogs;
using SchoolCollab.Admin.Shared.Components.Landing;
using SchoolCollab.Core.Features;
using SchoolCollab.Students.Application.Components.Pages.Students.Subjects;
using SchoolCollab.Students.Application.Services;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit regression tests for the Subjects (Topics) landing page
/// (<c>/students/subjects</c>).
///
/// <para><b>The bug.</b> <c>Subjects.razor</c> loaded activity groups inline in
/// <c>OnInitializedAsync</c>, between the grade-level load and the topics load.
/// The <c>/activity-groups</c> route group is only mapped when
/// <c>FEATURE:EnableActivityGroups</c> is on (dark-launched, default OFF), so for
/// every non-pilot tenant the request 404s, the exception escaped to the page-level
/// catch, and <c>_items</c> was left <c>null</c>. Because <c>Loading</c> is bound to
/// <c>_items is null</c>, the page spun forever and never rendered a single topic —
/// an unrelated dark-launched feature silently broke the topics list.</para>
///
/// <para><b>The fix.</b> Activity groups are now loaded after the topics list, in
/// their own isolated method, and only when the flag is on. They feed nothing but the
/// optional "Activity Group" owner filter, so they can no longer gate topic loading.
/// The page-level catch also assigns <c>_items = []</c> and the captured
/// <c>_error</c> is finally surfaced through <c>LandingPage</c>'s <c>Error</c>
/// parameter, so a genuine failure shows a message instead of a permanent spinner.</para>
/// </summary>
[TestClass]
public class SubjectsLandingPageTests : BunitContext
{
    private const string GradeLevelsUrl = "/students/grade-levels";
    private const string ActivityGroupsUrl = "/activity-groups";
    private const string CodedValuesRoleUrl = "/api/coded-values/by-parent?parentCode=SUBJECT";

    /// <summary>Fixed span ends so the badge/span fixtures read at a glance.</summary>
    private static readonly DateOnly SpanStart = new(2027, 1, 1);
    private static readonly DateOnly SpanEnd = new(2027, 3, 31);

    public SubjectsLandingPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    // ── Fixtures ───────────────────────────────────────────────────────────

    private static string GradeLevelsJson(params (Guid Id, string Name, int Level)[] grades) =>
        JsonSerializer.Serialize(grades.Select(g => new Dictionary<string, object?>
        {
            ["id"] = g.Id,
            ["codedValueId"] = Guid.NewGuid(),
            ["level"] = g.Level,
            ["name"] = g.Name,
            ["displayOrder"] = g.Level,
            ["topicCount"] = 0,
            ["studentCount"] = 0,
            ["createdAt"] = DateTimeOffset.UnixEpoch,
            ["updatedAt"] = DateTimeOffset.UnixEpoch,
        }).ToArray());

    private static string TopicsJson(params (Guid Id, Guid CodedValueId, string Code, string Name)[] topics) =>
        JsonSerializer.Serialize(topics.Select(t => new Dictionary<string, object?>
        {
            ["id"] = t.Id,
            ["codedValueId"] = t.CodedValueId,
            ["code"] = t.Code,
            ["name"] = t.Name,
            ["displayOrder"] = 0,
            ["isOverridden"] = false,
            ["createdAt"] = DateTimeOffset.UnixEpoch,
            ["updatedAt"] = DateTimeOffset.UnixEpoch,
        }).ToArray());

    private static string ActivityGroupsJson(params (Guid Id, string Name)[] groups) =>
        JsonSerializer.Serialize(groups.Select(g => new Dictionary<string, object?>
        {
            ["id"] = g.Id,
            ["name"] = g.Name,
            ["description"] = (string?)null,
            ["category"] = (string?)null,
            ["capacity"] = (int?)null,
            ["isActive"] = true,
            ["span"] = "Rollover",
            ["enrollmentStartDate"] = (string?)null,
            ["enrollmentEndDate"] = (string?)null,
            ["autoRenewDefault"] = false,
            ["eligibleGradeIds"] = Array.Empty<Guid>(),
            ["activeMemberCount"] = 0,
            ["createdAt"] = DateTimeOffset.UnixEpoch,
            ["updatedAt"] = DateTimeOffset.UnixEpoch,
        }).ToArray());

    private static string CodedValuesJson(params (Guid Id, string Name)[] values) =>
        JsonSerializer.Serialize(values.Select(v => new Dictionary<string, object?>
        {
            ["id"] = v.Id,
            ["code"] = v.Name.ToUpperInvariant(),
            ["name"] = v.Name,
            ["parentId"] = (Guid?)null,
            ["parentCode"] = "SUBJECT",
            ["isDisabled"] = false,
            ["displayOrder"] = 0,
            ["createdAt"] = DateTimeOffset.UnixEpoch,
            ["updatedAt"] = DateTimeOffset.UnixEpoch,
            ["attributes"] = Array.Empty<object>(),
            ["attributeDefinitions"] = Array.Empty<object>(),
        }).ToArray());

    /// <summary>
    /// One enrollment exception (<c>SubjectEnrollmentException</c>): a period part plus
    /// the date span the subject is NOT offered for. A subject with two exceptions is TWO
    /// of these — which is why the page counts them per topic instead of keying by
    /// <c>topicId</c>. The retired whitelist premise (one bridge row per delivery period)
    /// is unreachable: the filtered unique index
    /// <c>ix_topic_assignments_tenant_grade_topic_unique</c> allows at most ONE
    /// bridge row per (tenant, grade, topic).
    /// </summary>
    private static Dictionary<string, object?> ExceptionJson(
        Guid topicId, string division, DateOnly? startDate, DateOnly? endDate) =>
        new()
        {
            ["id"] = Guid.NewGuid(),
            ["gradeLevelId"] = (Guid?)null,
            ["activityGroupId"] = (Guid?)null,
            ["topicId"] = topicId,
            ["division"] = division,
            ["startDate"] = startDate?.ToString("yyyy-MM-dd"),
            ["endDate"] = endDate?.ToString("yyyy-MM-dd"),
            ["reason"] = (string?)null,
            ["createdAt"] = DateTimeOffset.UnixEpoch,
            ["updatedAt"] = DateTimeOffset.UnixEpoch,
        };

    /// <summary>The grade-scoped exception list the landing reads once with the grid.</summary>
    private static string ExceptionsUrl(Guid gradeId) => $"/students/enrollment-exceptions?gradeLevelId={gradeId:D}";

    // ── Harness ────────────────────────────────────────────────────────────

    /// <summary>
    /// Routes only the endpoints the topics page actually calls. Anything else
    /// returns 404, which mirrors the real API when a feature flag gates the route
    /// group — that is the exact condition the regression depends on.
    /// </summary>
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public readonly List<string> Calls = new();
        private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _responses = new();

        public ScriptedHandler Map(string url, HttpStatusCode status, string body)
        {
            _responses[url] = (status, body);
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.PathAndQuery;
            Calls.Add(url);

            foreach (var (key, value) in _responses)
            {
                if (url.Equals(key, StringComparison.OrdinalIgnoreCase)
                    || url.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(new HttpResponseMessage(value.Status)
                    {
                        Content = new StringContent(value.Body, Encoding.UTF8, "application/json"),
                    });
                }
            }

            // Unmapped routes are genuinely absent — e.g. /activity-groups when
            // FEATURE:EnableActivityGroups is off.
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent(
                    $"{{\"message\":\"No route matches {request.Method.Method} {url}\"}}",
                    Encoding.UTF8,
                    "application/json"),
            });
        }
    }

    private static string ReadSubjectsSource()
    {
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var srcPath = Path.GetFullPath(Path.Combine(
            asmDir, "..", "..", "..", "..", "..",
            "src", "Students", "SchoolCollab.Students.Application",
            "Components", "Pages", "Students", "Subjects", "Subjects.razor"));
        File.Exists(srcPath).Should().BeTrue($"Subjects.razor should exist at '{srcPath}'");
        return File.ReadAllText(srcPath);
    }

    private static ClaimsPrincipal CreateUser(bool realTenant)
    {
        var tenantId = realTenant ? Guid.NewGuid() : Guid.Empty;
        var claims = new[]
        {
            new Claim("tenant_id", tenantId.ToString()),
            new Claim("tenant_name", realTenant ? "Hydeson" : "System"),
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestScheme"));
    }

    private static AuthenticationStateProvider CreateAuthProvider(bool realTenant)
    {
        var provider = new Mock<AuthenticationStateProvider>();
        var user = CreateUser(realTenant);
        provider.Setup(p => p.GetAuthenticationStateAsync())
                .Returns(Task.FromResult(new AuthenticationState(user)));
        return provider.Object;
    }

    private sealed class Harness
    {
        public required IRenderedComponent<Subjects> Cut { get; init; }
        public required ScriptedHandler Handler { get; init; }
        public required Guid GradeId { get; init; }
        public required Guid TopicId { get; init; }
    }

    /// <summary>
    /// Registers the page's dependency graph and renders <c>Subjects</c>.
    /// <paramref name="activityGroupsStatus"/> controls the <c>/activity-groups</c>
    /// response so a test can reproduce the dark-launched (404) and enabled (200)
    /// cases; pass <c>null</c> to leave the route unmapped entirely.
    /// </summary>
    private Harness RenderSubjects(
        bool activityGroupsEnabled,
        HttpStatusCode? activityGroupsStatus = HttpStatusCode.OK,
        string? topicsJson = null,
        HttpStatusCode topicsStatus = HttpStatusCode.OK,
        Guid? gradeIdOverride = null,
        Guid? topicIdOverride = null,
        string? exceptionsJson = null)
    {
        var gradeId = gradeIdOverride ?? Guid.NewGuid();
        var topicId = topicIdOverride ?? Guid.NewGuid();
        var codedValueId = Guid.NewGuid();

        topicsJson ??= TopicsJson((topicId, codedValueId, "MATH", "Mathematics"));

        var handler = new ScriptedHandler()
            .Map(GradeLevelsUrl, HttpStatusCode.OK, GradeLevelsJson((gradeId, "Grade 5", 5)))
            .Map($"/students/subjects/by-grade/{gradeId}", topicsStatus, topicsJson)
            .Map($"/api/coded-values/by-ids?ids={codedValueId}", HttpStatusCode.OK, CodedValuesJson((codedValueId, "Mathematics")))
            .Map(CodedValuesRoleUrl, HttpStatusCode.OK, "[]")
            // Enrollment exceptions. Left UNMAPPED by default (404) so a test can
            // prove the page degrades — the topics list must survive either way.
            .Map(ExceptionsUrl(gradeId),
                exceptionsJson is null ? HttpStatusCode.NotFound : HttpStatusCode.OK,
                exceptionsJson ?? """{"message":"No route matches"}""");

        if (activityGroupsStatus.HasValue)
        {
            handler.Map(ActivityGroupsUrl, activityGroupsStatus.Value,
                activityGroupsStatus.Value == HttpStatusCode.OK
                    ? ActivityGroupsJson((Guid.NewGuid(), "Robotics Club"))
                    : """{"message":"Not found"}""");
        }

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        var auth = CreateAuthProvider(realTenant: true);

        var codedValuesClient = new CodedValuesApiClient(http);
        Services.AddSingleton(codedValuesClient);
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, codedValuesClient));
        Services.AddSingleton(new VisibleTenantService(auth, NullLogger<VisibleTenantService>.Instance));

        var flags = new Mock<IFeatureFlagService>();
        flags.Setup(f => f.IsEnabledAsync(FeatureFlagKeys.EnableActivityGroups, It.IsAny<CancellationToken>()))
             .ReturnsAsync(activityGroupsEnabled);
        flags.Setup(f => f.IsEnabled(FeatureFlagKeys.EnableActivityGroups))
             .Returns(activityGroupsEnabled);
        Services.AddSingleton(flags.Object);

        return new Harness
        {
            Cut = Render<Subjects>(),
            Handler = handler,
            GradeId = gradeId,
            TopicId = topicId,
        };
    }

    /// <summary>
    /// The kebab actions for the single rendered row, read from the page's own
    /// delegate. <c>FluentMenu</c> does NOT materialise its items while closed —
    /// the same trap as <c>FluentSelect</c>'s option children — so asserting on
    /// menu markup would test nothing. <c>LandingPage.RowActions</c> is public.
    /// </summary>
    private static IReadOnlyList<RowAction> SingleRowActions(IRenderedComponent<Subjects> cut)
    {
        var landing = cut.FindComponent<LandingPage<SubjectDto>>().Instance;
        landing.Items.Should().ContainSingle("the grid should render exactly one subject row");
        landing.RowActions.Should().NotBeNull();
        return landing.RowActions!(landing.Items!.Single());
    }

    // ── Regression tests ───────────────────────────────────────────────────

    /// <summary>
    /// THE regression test. With <c>FEATURE:EnableActivityGroups</c> off the
    /// <c>/activity-groups</c> route is unmapped (404). Topics must still load and
    /// the page must leave its loading state. Before the fix this rendered a
    /// permanent spinner and zero topics.
    /// </summary>
    [TestMethod]
    public void ActivityGroupsFlagOff_TopicsStillLoadAndPageLeavesLoadingState()
    {
        var harness = RenderSubjects(
            activityGroupsEnabled: false,
            activityGroupsStatus: HttpStatusCode.NotFound);

        var cut = harness.Cut;
        var markup = cut.Markup;

        // The topic is on screen.
        markup.Should().Contain("Mathematics", "topics must load regardless of the activity-groups flag");

        // Not stuck loading, and no error surfaced for a normal, expected state.
        cut.FindComponents<FluentProgressRing>().Should().BeEmpty(
            "the page must not spin forever when an optional feature is dark-launched");
        cut.FindComponents<FluentMessageBar>().Should().BeEmpty(
            "an off flag is a normal state, not an error to report to the user");
    }

    /// <summary>
    /// The off flag must short-circuit the request rather than firing a call that
    /// 404s — the page should not depend on a route it knows is absent.
    /// </summary>
    [TestMethod]
    public void ActivityGroupsFlagOff_DoesNotRequestTheActivityGroupsRoute()
    {
        var harness = RenderSubjects(
            activityGroupsEnabled: false,
            activityGroupsStatus: HttpStatusCode.NotFound);

        harness.Handler.Calls.Should().NotContain(
            url => url.StartsWith(ActivityGroupsUrl, StringComparison.OrdinalIgnoreCase),
            "the route is unmapped while the flag is off, so calling it is pure noise");
    }

    /// <summary>
    /// With the flag off the "Activity Group" owner option must not be offered —
    /// selecting a filter that cannot list anything is a dead end.
    /// </summary>
    [TestMethod]
    public void ActivityGroupsFlagOff_OwnerSelectOmitsTheActivityGroupOption()
    {
        // FluentSelect does not materialise its FluentOption children while the
        // popup is closed, so the conditional option cannot be observed in the
        // rendered tree. Assert the guard at the source instead, mirroring how
        // GradeLevelDetailPageTests covers markup-invisible wiring.
        var source = ReadSubjectsSource();

        source.Should().MatchRegex(
            @"@if \(_activityGroupsEnabled\)\s*\{\s*<FluentOption Value=""ActivityGroup"">",
            "the Activity Group owner option must be gated on the feature flag");
    }

    /// <summary>
    /// Happy path preserved: with the flag on, the option is offered and the route
    /// is called.
    /// </summary>
    [TestMethod]
    public void ActivityGroupsFlagOn_LoadsGroupsAndOffersTheActivityGroupOption()
    {
        var harness = RenderSubjects(
            activityGroupsEnabled: true,
            activityGroupsStatus: HttpStatusCode.OK);

        var cut = harness.Cut;

        harness.Handler.Calls.Should().Contain(
            url => url.StartsWith(ActivityGroupsUrl, StringComparison.OrdinalIgnoreCase),
            "an enabled flag must actually load the groups");

        cut.Markup.Should().Contain("Mathematics", "topics still load alongside the groups");
        cut.FindComponents<FluentProgressRing>().Should().BeEmpty();
    }

    /// <summary>
    /// A failing <c>/activity-groups</c> (500) must stay contained: topics still
    /// load and the page still leaves its loading state. The unusable filter is
    /// withdrawn rather than left selectable (gated in source — see the flag-off
    /// test, since FluentSelect does not materialise its options while closed).
    /// </summary>
    [TestMethod]
    public void ActivityGroupsRequestFails_TopicsStillLoadAndPageStaysUsable()
    {
        var harness = RenderSubjects(
            activityGroupsEnabled: true,
            activityGroupsStatus: HttpStatusCode.InternalServerError);

        var cut = harness.Cut;

        harness.Handler.Calls.Should().Contain(
            url => url.StartsWith(ActivityGroupsUrl, StringComparison.OrdinalIgnoreCase),
            "the flag was on, so the groups route was attempted");

        cut.Markup.Should().Contain("Mathematics",
            "a failing optional filter must not prevent topics from rendering");
        cut.FindComponents<FluentProgressRing>().Should().BeEmpty(
            "the page must not be left spinning by a failed optional fetch");
    }

    /// <summary>
    /// A genuine topic-load failure must now surface: the page leaves the loading
    /// state and shows the error. Before the fix <c>_error</c> was write-only
    /// (never passed to <c>LandingPage</c>) and <c>_items</c> stayed <c>null</c>,
    /// so the failure was both invisible and a permanent spinner.
    /// </summary>
    [TestMethod]
    public void TopicLoadFails_ShowsErrorAndLeavesLoadingState()
    {
        var harness = RenderSubjects(
            activityGroupsEnabled: false,
            activityGroupsStatus: HttpStatusCode.NotFound,
            topicsJson: """{"message":"boom"}""",
            topicsStatus: HttpStatusCode.InternalServerError);

        var cut = harness.Cut;
        cut.FindComponents<FluentProgressRing>().Should().BeEmpty(
            "_items must be reset so Loading is no longer true after a failure");

        var messageBars = cut.FindComponents<FluentMessageBar>();
        messageBars.Should().NotBeEmpty("the captured error must reach LandingPage's Error parameter");
        messageBars.Should().Contain(bar =>
            bar.Markup.Contains("500", StringComparison.OrdinalIgnoreCase),
            "the failing status should be visible to the user");
    }

    /// <summary>
    /// A non-real tenant (no <c>tenant_id</c> claim) must short-circuit cleanly and
    /// not issue any API calls — this path runs before the flag resolution.
    /// </summary>
    [TestMethod]
    public void NonRealTenant_ShowsTenantPromptAndCallsNoApis()
    {
        var gradeId = Guid.NewGuid();
        var handler = new ScriptedHandler()
            .Map(GradeLevelsUrl, HttpStatusCode.OK, GradeLevelsJson((gradeId, "Grade 5", 5)));

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        var codedValuesClient = new CodedValuesApiClient(http);
        Services.AddSingleton(codedValuesClient);
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, codedValuesClient));
        Services.AddSingleton(new VisibleTenantService(
            CreateAuthProvider(realTenant: false), NullLogger<VisibleTenantService>.Instance));

        var flags = new Mock<IFeatureFlagService>();
        Services.AddSingleton(flags.Object);

        var cut = Render<Subjects>();

        cut.Markup.Should().Contain("Select a tenant to manage topics.");
        handler.Calls.Should().BeEmpty("no tenant means no data to request");
    }

    // ── Enrollment exceptions (subject-period-exception-model.md v3 §5.2) ───

    /// <summary>
    /// A subject with two exceptions is TWO rows for the same topic — not two bridge
    /// rows. The retired whitelist premise (N bridge rows for N delivery periods) is
    /// unreachable: the filtered unique index
    /// <c>ix_topic_assignments_tenant_grade_topic_unique</c> allows at most ONE bridge row
    /// per (tenant, grade, topic).
    ///
    /// <para><b>Replaces</b> v1's
    /// <c>GradeOwner_SubjectBlockedInTwoTerms_ShowsBothPeriodNamesInTheColumn</c>: the
    /// per-period label dissolved with the period vocabulary, and the column is now a
    /// COUNT badge — "Offered in every period" (and its badge) is the absence of an
    /// exception, which is the normal state.</para>
    /// </summary>
    [TestMethod]
    public void GradeOwner_SubjectWithTwoExceptions_ShowsTheCountBadge()
    {
        var gradeId = Guid.NewGuid();
        var topicId = Guid.NewGuid();

        var harness = RenderSubjects(
            activityGroupsEnabled: false,
            gradeIdOverride: gradeId,
            topicIdOverride: topicId,
            exceptionsJson: JsonSerializer.Serialize(new[]
            {
                ExceptionJson(topicId, "Terms", SpanStart, SpanEnd),
                ExceptionJson(topicId, "None", SpanStart.AddDays(60), null),
            }));

        harness.Cut.Markup.Should().Contain("Enrollment exceptions",
            "the grid column uses the exception model's vocabulary");
        harness.Cut.Markup.Should().Contain("2 exceptions",
            "the column is a COUNT badge derived from the exceptions");
        harness.Cut.Markup.Should().NotContain("Blocked in",
            "the per-period label is gone — the badge counts, it does not list");
        harness.Cut.Markup.Should().NotContain("Offered in every period",
            "the absence of a badge is the normal state (spec §5.2)");
    }

    /// <summary>
    /// The subject edit dialog has no period field by design, so "Enrollment exceptions"
    /// on the landing is the only way to reach exception management from here.
    /// </summary>
    [TestMethod]
    public void GradeOwner_RowOffersEnrollmentExceptionsAction()
    {
        var gradeId = Guid.NewGuid();
        var topicId = Guid.NewGuid();

        var harness = RenderSubjects(
            activityGroupsEnabled: false,
            gradeIdOverride: gradeId,
            topicIdOverride: topicId,
            exceptionsJson: JsonSerializer.Serialize(new[]
            {
                ExceptionJson(topicId, "Terms", SpanStart, SpanEnd),
            }));

        SingleRowActions(harness.Cut).Select(a => a.Label).Should()
            .BeEquivalentTo(new[] { "Edit", "Enrollment exceptions", "Delete" });
    }

    /// <summary>
    /// <b>Q1 (owner decision 2026-09-26) — the gate is WIDENED, and this test replaces
    /// v1's <c>NonGradeOwner_RowOmitsEnrollmentExceptionsRatherThanOfferingADeadControl</c>
    /// which pinned the OPPOSITE.</b> An activity group owns exceptions just as a grade
    /// does (spec §5.2 row 1), so a group-owned row must OFFER the action. The v1 test
    /// asserted the action was gated to <c>_ownerType == "GradeLevel"</c>; that gate is
    /// now void — asserted at the source, like the flag-gated owner option above, because
    /// bUnit cannot switch the owner filter (the toolbar's group option is backed by a
    /// private nested type) and FluentSelect does not materialise its children while
    /// closed.
    /// </summary>
    [TestMethod]
    public void GroupOwner_RowOffersEnrollmentExceptionsRatherThanOmittingIt()
    {
        var source = ReadSubjectsSource();

        source.Should().Contain("OwnerType = \"ActivityGroup\"",
            "Q1: a group-owned row opens the dialog scoped to the GROUP owner — the action is not grade-only");
        source.Should().Contain("RowAction.Callback(\"Enrollment exceptions\"",
            "the affordance opens the management dialog instead of navigating to a page");
        source.Should().MatchRegex(
            @"if \(EnrollmentExceptionsScope\(row\) is not null\)\s*\{\s*actions\.Add\(RowAction\.Callback\(",
            "Q1: the action is gated on a RESOLVABLE OWNER, never on the owner type — a group-owned row gets it too");
    }

    /// <summary>
    /// <b>Replaces</b> v1's <c>EnrollmentExceptionsAction_OpensTheBlocksDialogForThatRow</c>.
    /// Invoking the action must take the user to the exception editor FOR THAT ROW: owner and
    /// topic travel as the dialog's locked scope, so the dialog lands on exactly that subject
    /// (spec §5.2, v8 §5.1). Nothing opens inline — the action is a
    /// <see cref="RowAction.Callback"/>, which carries no href at all, so the retired page's
    /// URL cannot come back.
    /// </summary>
    [TestMethod]
    public void EnrollmentExceptionsAction_OpensTheDialogForThatRow()
    {
        var gradeId = Guid.NewGuid();
        var topicId = Guid.NewGuid();

        var harness = RenderSubjects(
            activityGroupsEnabled: false,
            gradeIdOverride: gradeId,
            topicIdOverride: topicId,
            exceptionsJson: JsonSerializer.Serialize(new[]
            {
                ExceptionJson(topicId, "Terms", SpanStart, SpanEnd),
            }));

        var action = SingleRowActions(harness.Cut).First(a => a.Label == "Enrollment exceptions");

        action.Href.Should().BeNull(
            "the affordance no longer navigates — the /students/enrollment-exceptions page was retired (D1)");
        action.OnClick.Should().NotBeNull(
            "the kebab OPENS the EnrollmentExceptionsDialog scoped to this owner + subject (D7)");

        ReadSubjectsSource().Should().Contain("EnrollmentExceptionsDialog",
            "the call site opens the dialog rather than pointing at a URL");
    }

    /// <summary>
    /// The exception load is best-effort: it runs after the topics list, so a 404 must
    /// drop the badge without failing the page. (v1's fallback text "Offered in every
    /// period" is gone — no badge IS the fallback now.)
    /// </summary>
    [TestMethod]
    public void ExceptionLoadFails_TopicsStillLoadAndTheBadgeIsAbsent()
    {
        // exceptionsJson left null → the route 404s.
        var harness = RenderSubjects(activityGroupsEnabled: false);

        harness.Cut.Markup.Should().Contain("Mathematics", "topics must survive an exception-load failure");
        harness.Cut.Markup.Should().NotContain("1 exception",
            "no exceptions loaded = no badge");
        harness.Cut.Markup.Should().NotContain("Offered in every period",
            "the v1 fallback label is retired");
        harness.Cut.FindComponents<FluentMessageBar>().Should().BeEmpty(
            "an exception-load failure is not a page-level error");
    }
}
