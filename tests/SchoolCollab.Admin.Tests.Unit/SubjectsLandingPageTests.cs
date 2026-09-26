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
using SchoolCollab.Students.Application.Components.Students;
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
    private const string PeriodsUrl = "/students/periods";

    /// <summary>Fixed ids so the period column can be asserted on by name.</summary>
    private static readonly Guid Term1Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Term2Id = Guid.Parse("22222222-2222-2222-2222-222222222222");

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
    /// One bridge row (<c>GradeTopicAssignment</c>). A subject running in two terms
    /// produces TWO of these for the same topic — which is why the page groups them
    /// instead of keying by <c>topicId</c>.
    /// </summary>
    private static Dictionary<string, object?> AssignmentJson(Guid assignmentId, Guid topicId, Guid gradeId) =>
        new()
        {
            ["id"] = assignmentId,
            ["audience"] = "grade",
            ["gradeLevelId"] = gradeId,
            ["activityGroupId"] = (Guid?)null,
            ["topicId"] = topicId,
            ["startDate"] = "2026-02-01",
            ["endDate"] = (string?)null,
            ["topicStrandId"] = (Guid?)null,
            ["periodId"] = (Guid?)null,
            ["createdAt"] = DateTimeOffset.UnixEpoch,
            ["updatedAt"] = DateTimeOffset.UnixEpoch,
        };

    private static Dictionary<string, object?> WithPeriod(Dictionary<string, object?> a, Guid? periodId)
    {
        a["periodId"] = periodId;
        return a;
    }

    private static string PeriodsJson() =>
        JsonSerializer.Serialize(new[]
        {
            new Dictionary<string, object?>
            {
                ["id"] = Term1Id, ["name"] = "Term 1", ["startDate"] = "2026-02-01", ["endDate"] = "2026-06-30",
                ["status"] = "Active", ["parentPeriodId"] = (Guid?)null, ["nextPeriodId"] = (Guid?)null,
                ["division"] = "Terms", ["createdAt"] = DateTimeOffset.UnixEpoch, ["updatedAt"] = DateTimeOffset.UnixEpoch,
            },
            new Dictionary<string, object?>
            {
                ["id"] = Term2Id, ["name"] = "Term 2", ["startDate"] = "2026-07-01", ["endDate"] = "2026-12-20",
                ["status"] = "Active", ["parentPeriodId"] = (Guid?)null, ["nextPeriodId"] = (Guid?)null,
                ["division"] = "Terms", ["createdAt"] = DateTimeOffset.UnixEpoch, ["updatedAt"] = DateTimeOffset.UnixEpoch,
            },
        });

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
        string? assignmentsJson = null,
        string? periodsJson = null,
        Mock<IDialogService>? dialogMock = null)
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
            // Delivery periods. Left UNMAPPED by default (404) so a test can prove
            // the page degrades — the topics list must survive either way.
            .Map($"/students/topic-assignments/by-grade/{gradeId}",
                assignmentsJson is null ? HttpStatusCode.NotFound : HttpStatusCode.OK,
                assignmentsJson ?? """{"message":"No route matches"}""")
            .Map(PeriodsUrl,
                periodsJson is null ? HttpStatusCode.NotFound : HttpStatusCode.OK,
                periodsJson ?? """{"message":"No route matches"}""");

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

        // Registered after AddFluentUIComponents (in the ctor) so the mock wins
        // resolution for the "Edit periods" dialog.
        if (dialogMock is not null)
        {
            Services.AddSingleton(dialogMock.Object);
        }

        return new Harness
        {
            Cut = Render<Subjects>(),
            Handler = handler,
            GradeId = gradeId,
            TopicId = topicId,
        };
    }

    /// <summary>Builds an <see cref="IDialogService"/> mock that captures the shell
    /// model and reports a cancel (so the page takes its post-dialog reload path).</summary>
    private static Mock<IDialogService> CreateCapturingDialogMock(
        Action<DialogShellData<TopicPeriodsEditDialog.TopicPeriodsEditModel>> capture)
    {
        var dialogRef = new Mock<IDialogReference>();
        dialogRef.SetupGet(r => r.Result).Returns(Task.FromResult(DialogResult.Cancel()));

        var mock = new Mock<IDialogService>();
        mock.Setup(d => d.ShowDialogAsync<
                TopicPeriodsEditDialog,
                DialogShellData<TopicPeriodsEditDialog.TopicPeriodsEditModel>>(
                It.IsAny<DialogShellData<TopicPeriodsEditDialog.TopicPeriodsEditModel>>(),
                It.IsAny<DialogParameters>()))
            .Callback<DialogShellData<TopicPeriodsEditDialog.TopicPeriodsEditModel>, DialogParameters>(
                (data, _) => capture(data))
            .ReturnsAsync(dialogRef.Object);
        return mock;
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

    // ── Delivery-period authoring (item 6) ──────────────────────────────────

    /// <summary>
    /// A subject delivered in two terms has TWO bridge rows. The grid must show
    /// both period NAMES (not GUID prefixes) rather than collapsing to one row.
    /// </summary>
    [TestMethod]
    public void GradeOwner_SubjectInTwoTerms_ShowsBothPeriodNamesInTheColumn()
    {
        var gradeId = Guid.NewGuid();
        var topicId = Guid.NewGuid();

        var harness = RenderSubjects(
            activityGroupsEnabled: false,
            gradeIdOverride: gradeId,
            topicIdOverride: topicId,
            assignmentsJson: JsonSerializer.Serialize(new[]
            {
                WithPeriod(AssignmentJson(Guid.NewGuid(), topicId, gradeId), Term1Id),
                WithPeriod(AssignmentJson(Guid.NewGuid(), topicId, gradeId), Term2Id),
            }),
            periodsJson: PeriodsJson());

        harness.Cut.Markup.Should().Contain("Delivery periods", "the grid gained a period column");
        harness.Cut.Markup.Should().Contain("Term 1");
        harness.Cut.Markup.Should().Contain("Term 2");
    }

    /// <summary>
    /// The subject edit dialog has no period field by design (the period is on the
    /// bridge, and one subject can run in several terms), so "Edit periods" on the
    /// landing is the ONLY way to reach period editing from here.
    /// </summary>
    [TestMethod]
    public void GradeOwner_RowOffersEditPeriodsAction()
    {
        var gradeId = Guid.NewGuid();
        var topicId = Guid.NewGuid();

        var harness = RenderSubjects(
            activityGroupsEnabled: false,
            gradeIdOverride: gradeId,
            topicIdOverride: topicId,
            assignmentsJson: JsonSerializer.Serialize(new[]
            {
                WithPeriod(AssignmentJson(Guid.NewGuid(), topicId, gradeId), Term1Id),
            }),
            periodsJson: PeriodsJson());

        SingleRowActions(harness.Cut).Select(a => a.Label).Should().Contain("Edit periods");
    }

    /// <summary>
    /// Gate the control, not just the call: with no bridge rows loaded there is no
    /// period set to edit, so the action must not be rendered at all. Before this
    /// the landing offered no period path at all — and a naive fix that added the
    /// action unconditionally would offer a dead control instead.
    /// </summary>
    [TestMethod]
    public void NoBridgeRows_RowOmitsEditPeriodsRatherThanOfferingADeadControl()
    {
        var harness = RenderSubjects(activityGroupsEnabled: false);

        harness.Cut.Markup.Should().Contain("Mathematics", "topics still load");
        SingleRowActions(harness.Cut).Select(a => a.Label).Should()
            .BeEquivalentTo(new[] { "Edit", "Delete" },
                "with no bridge rows there is no period set to edit, so the action is not rendered");
    }

    /// <summary>
    /// THE regression guard. The editor is seeded with ONE ROW PER BRIDGE ROW, not
    /// one row per topic. A subject running in two terms must open with both terms
    /// present and editable — keying by topicId (ToDictionary) would either throw
    /// on the duplicate or silently drop a term.
    /// </summary>
    [TestMethod]
    public async Task EditPeriods_OpensEditorWithOneRowPerBridgeRow()
    {
        var gradeId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        var firstAssignment = Guid.NewGuid();
        var secondAssignment = Guid.NewGuid();

        DialogShellData<TopicPeriodsEditDialog.TopicPeriodsEditModel>? captured = null;
        var dialogMock = CreateCapturingDialogMock(data => captured = data);

        var harness = RenderSubjects(
            activityGroupsEnabled: false,
            gradeIdOverride: gradeId,
            topicIdOverride: topicId,
            assignmentsJson: JsonSerializer.Serialize(new[]
            {
                WithPeriod(AssignmentJson(firstAssignment, topicId, gradeId), Term1Id),
                WithPeriod(AssignmentJson(secondAssignment, topicId, gradeId), Term2Id),
            }),
            periodsJson: PeriodsJson(),
            dialogMock: dialogMock);

        var action = SingleRowActions(harness.Cut).First(a => a.Label == "Edit periods");
        action.OnClick.Should().NotBeNull();
        // InvokeAsync: the handler ends in StateHasChanged, which asserts dispatcher
        // access. A real click runs on the renderer; a direct call from the test
        // thread does not.
        await harness.Cut.InvokeAsync(() => action.OnClick!());

        harness.Cut.WaitForAssertion(() => captured.Should().NotBeNull(
            "invoking 'Edit periods' must open the topic-scoped editor"));

        var model = captured!.Model;
        model.TopicId.Should().Be(topicId);
        model.GradeLevelId.Should().Be(gradeId);
        model.Rows.Should().HaveCount(2,
            "a subject in two terms is two bridge rows — both must be editable, not one");
        model.Rows.Select(r => r.AssignmentId).Should()
            .BeEquivalentTo(new[] { (Guid?)firstAssignment, secondAssignment },
                "each row must carry its own assignment id so save can target it");
        model.Rows.Select(r => r.PeriodId).Should()
            .BeEquivalentTo(new[] { (Guid?)Term1Id, Term2Id });
    }

    /// <summary>
    /// The period load is best-effort: it runs after the topics list, so a 404 must
    /// degrade the column and drop the action without failing the page.
    /// </summary>
    [TestMethod]
    public void DeliveryPeriodLoadFails_TopicsStillLoadAndColumnFallsBack()
    {
        // assignmentsJson / periodsJson left null → both routes 404.
        var harness = RenderSubjects(activityGroupsEnabled: false);

        harness.Cut.Markup.Should().Contain("Mathematics", "topics must survive a period-load failure");
        harness.Cut.Markup.Should().Contain("Whole academic year", "null PeriodId = year-spanning");
        SingleRowActions(harness.Cut).Select(a => a.Label).Should().NotContain("Edit periods");
        harness.Cut.FindComponents<FluentMessageBar>().Should().BeEmpty(
            "a period-load failure is not a page-level error");
    }
}
