using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Core.Features;
using SchoolCollab.Students.Application.Components.Pages.Students.GradeLevels;
using SchoolCollab.Students.Application.Services;
using SchoolCollab.Students.Core.Contracts;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for the Grade-Level Detail page (grade-level-detail-view-plan.md §5):
/// Overview card + the three equal section cards (Topics / Teachers / Students)
/// with top-15 preview lists, count chips, and "View all" anchors. The full
/// management grids moved into <c>GradeTopicsDialog</c> / <c>GradeTeachersDialog</c>,
/// which are covered separately in <c>GradeDialogsBunitTests</c>.
/// </summary>
[TestClass]
public class GradeLevelDetailPageTests : BunitContext
{
    private const string RoleParentUrl = "/api/coded-values/by-parent?parentCode=TCHROLES";

    public GradeLevelDetailPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private static string ReadDetailSource()
    {
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var srcPath = Path.GetFullPath(Path.Combine(
            asmDir, "..", "..", "..", "..", "..",
            "src", "Students", "SchoolCollab.Students.Application",
            "Components", "Pages", "Students", "GradeLevels", "Detail.razor"));
        File.Exists(srcPath).Should().BeTrue($"Detail.razor should exist at '{srcPath}'");
        return File.ReadAllText(srcPath);
    }

    private static string ReadSectionCardSource()
    {
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var srcPath = Path.GetFullPath(Path.Combine(
            asmDir, "..", "..", "..", "..", "..",
            "src", "Students", "SchoolCollab.Students.Application",
            "Components", "Students", "SectionCard.razor"));
        File.Exists(srcPath).Should().BeTrue($"SectionCard.razor should exist at '{srcPath}'");
        return File.ReadAllText(srcPath);
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public readonly List<(string Method, string Url, string? Body)> Calls = new();
        private readonly Dictionary<(string Method, string Url), (HttpStatusCode Status, string Body)> _responses = new();
        private readonly Dictionary<(string Method, string Url), Func<string>> _dynamic = new();

        public ScriptedHandler Map(string method, string url, HttpStatusCode status, string body)
        {
            _responses[(method.ToUpperInvariant(), url)] = (status, body);
            return this;
        }
        public ScriptedHandler Map(string url, HttpStatusCode status, string body) => Map("ANY", url, status, body);

        /// <summary>
        /// Answers a URL from a delegate evaluated per request. Needed where the SAME
        /// url must answer differently before and after a mutation — a static Map keeps
        /// serving the stale body, which would hide a SectionCard that never refreshes.
        /// </summary>
        public ScriptedHandler MapDynamic(string method, string url, Func<string> body)
        {
            _dynamic[(method.ToUpperInvariant(), url)] = body;
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request.Method.Method, request.RequestUri!.PathAndQuery, body));
            var url = request.RequestUri.PathAndQuery;
            if (_dynamic.TryGetValue((request.Method.Method.ToUpperInvariant(), url), out var dynamicBody))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(dynamicBody(), Encoding.UTF8, "application/json"),
                };
            (HttpStatusCode Status, string Body)? found = null;
            if (_responses.TryGetValue((request.Method.Method.ToUpperInvariant(), url), out var exact))
                found = exact;
            else
            {
                foreach (var kv in _responses)
                {
                    if (kv.Key.Method != "ANY") continue;
                    if (url.Equals(kv.Key.Url, StringComparison.OrdinalIgnoreCase) ||
                        url.StartsWith(kv.Key.Url, StringComparison.OrdinalIgnoreCase))
                    {
                        found = kv.Value;
                        break;
                    }
                }
            }
            if (found is { } hit)
                return new HttpResponseMessage(hit.Status) { Content = new StringContent(hit.Body, Encoding.UTF8, "application/json") };
            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent($"Unexpected URL: {request.Method.Method} {url}", Encoding.UTF8, "application/json"),
            };
        }
    }

    private static ClaimsPrincipal CreateUser(bool realTenant)
    {
        var tenantId = realTenant ? Guid.NewGuid().ToString() : Guid.Empty.ToString();
        var claims = new[] { new Claim("tenant_id", tenantId), new Claim("tenant_name", realTenant ? "Hydeson" : "System") };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestScheme"));
    }

    private sealed class MutableAuthenticationStateProvider : AuthenticationStateProvider
    {
        private ClaimsPrincipal _user = new();
        public ClaimsPrincipal User { set { _user = value; NotifyAuthenticationStateChanged(GetAuthenticationStateAsync()); } }
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(_user));
    }

    /// <summary>
    /// Hosts a <see cref="FluentDialogProvider"/> alongside a child component so
    /// destructive row actions (which show a confirmation prompt via
    /// <c>IDialogService</c>) can render their dialog in the provider.
    /// </summary>
    private sealed class DialogHost : ComponentBase
    {
        [Parameter] public RenderFragment? ChildContent { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<FluentDialogProvider>(0);
            builder.CloseComponent();
            builder.AddContent(1, ChildContent);
        }
    }

    /// <summary>
    /// One Streams-card row: the bridge read's <c>GradeStreamDto</c>
    /// (assignment id + stream coded value id + resolved coded-value metadata), NOT
    /// a raw coded value — the card no longer reads the coded-value catalogue
    /// itself.
    /// </summary>
    private static Dictionary<string, object?> StreamJson(Guid assignmentId, Guid streamCodedValueId, Guid gradeId, string name, string code) =>
        new()
        {
            ["assignmentId"] = assignmentId,
            ["streamCodedValueId"] = streamCodedValueId,
            ["gradeLevelId"] = gradeId,
            ["code"] = code, ["name"] = name,
            ["nameOverride"] = (string?)null, ["description"] = (string?)null,
            ["streamVersion"] = "5A", ["isOverridden"] = false, ["isDisabled"] = false,
            ["displayOrder"] = 0,
        };

    private (ScriptedHandler Handler, Guid GradeId) Register(
        Guid gradeId,
        string gradeJson,
        string topicsCatalogJson = "[]",
        string teachersJson = "[]",
        string assignmentsJson = "[]",
        string studentsJson = "[]",
        string curriculumJson = "[]",
        string streamsJson = "[]",
        string exceptionsJson = "[]")
    {
        var auth = new MutableAuthenticationStateProvider { User = CreateUser(realTenant: true) };
        var handler = new ScriptedHandler();
        handler.Map("GET", $"/students/grade-levels/{gradeId}", HttpStatusCode.OK, gradeJson);
        handler.Map("GET", "/students/topics", HttpStatusCode.OK, topicsCatalogJson);
        handler.Map("GET", $"/students/grade-levels/{gradeId}/teachers", HttpStatusCode.OK, teachersJson);
        handler.Map("GET", "/teachers", HttpStatusCode.OK, teachersJson);
        handler.Map("GET", $"/students/topic-assignments/by-grade/{gradeId}", HttpStatusCode.OK, assignmentsJson);
        handler.Map("GET", $"/students/by-grade/{gradeId}", HttpStatusCode.OK, studentsJson);
        handler.Map("GET", $"/students/grade-levels/{gradeId}/curriculum", HttpStatusCode.OK, curriculumJson);
        // Enrollment exceptions for the Subjects card's count badge and the "View all
        // subjects" dialog (the span lives on the exception — the bridge row carries no
        // period meaning at all).
        handler.Map("/students/enrollment-exceptions", HttpStatusCode.OK, exceptionsJson);
        // Role dropdown (TCHROLES) parent lookup.
        handler.Map("GET", RoleParentUrl, HttpStatusCode.OK, "[]");
        // Grade streams for the Streams card (the grade<->stream BRIDGE, not the
        // GRSTREAMS catalogue filtered by the legacy `gradeLevel` attribute).
        handler.Map("GET", $"/students/grade-levels/{gradeId}/streams", HttpStatusCode.OK, streamsJson);
        // Bridge-row removal (DELETE .../streams/{assignmentId}): the assignment id
        // is unknown here, so match the prefix for any method. The exact GET above
        // still wins for the card read (exact match is checked before the ANY loop),
        // and MapDynamic overrides it where the test needs a before/after body.
        // Without this mapping the DELETE 404s, RemoveStreamAsync lands in its catch
        // and the card keeps the removed row (see
        // Detail_StreamsCard_Remove_RefreshesTheCard_SoTheRemovedStreamDisappears).
        handler.Map("ANY", $"/students/grade-levels/{gradeId}/streams/", HttpStatusCode.OK, "");
        // Notification &amp; Delivery editor: no tenant default / no grade override.
        handler.Map("GET", "/api/settings/notification-policy", HttpStatusCode.NoContent, "");
        handler.Map("GET", $"/students/grade-levels/{gradeId}/notification-policy", HttpStatusCode.NoContent, "");
        handler.Map("PUT", $"/students/grade-levels/{gradeId}/notification-policy", HttpStatusCode.OK, "");
        // Guardian Signature editor: no tenant default / no grade override.
        handler.Map("GET", "/api/settings/assignment-policy", HttpStatusCode.NoContent, "");
        handler.Map("GET", $"/students/grade-levels/{gradeId}/assignment-policy", HttpStatusCode.NoContent, "");

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        Services.AddSingleton<AuthenticationStateProvider>(auth);
        var codedValuesClient = new CodedValuesApiClient(http);
        Services.AddSingleton(codedValuesClient);
        var api = new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, codedValuesClient);
        Services.AddSingleton(api);
        // The ContactsEditor (rendered by the student create/edit dialogs)
        // injects IContactsClient, which the app maps to StudentsApiClient.
        Services.AddSingleton<IContactsClient>(api);
        Services.AddSingleton(new NotificationPolicyApiClient(http));
        Services.AddSingleton(new AssignmentPolicyApiClient(http));
        Services.AddSingleton(new VisibleTenantService(auth, NullLogger<VisibleTenantService>.Instance));

        return (handler, gradeId);
    }

    /// <summary>
    /// The two exception spans the multi-exception regression test seeds. Fixed so the
    /// test can assert the COUNT is derived from them; the span bounds also prove the
    /// card no longer renders a period NAME from a period join.
    /// </summary>
    public static readonly DateOnly ChipSpan1Start = new(2027, 1, 1);
    public static readonly DateOnly ChipSpan1End = new(2027, 3, 31);
    public static readonly DateOnly ChipSpan2Start = new(2027, 4, 1);
    public static readonly DateOnly ChipSpan2End = new(2027, 6, 30);

    /// <summary>One enrollment exception: a period part plus the date span the subject is
    /// NOT offered for (subject-period-exception-model.md v3 §2.1).</summary>
    private static Dictionary<string, object?> ExceptionJson(
        Guid id, Guid gradeId, Guid topicId, string division, DateOnly startDate, DateOnly endDate) =>
        new()
        {
            ["id"] = id,
            ["gradeLevelId"] = gradeId,
            ["activityGroupId"] = (Guid?)null,
            ["topicId"] = topicId,
            ["division"] = division,
            ["startDate"] = startDate.ToString("yyyy-MM-dd"),
            ["endDate"] = endDate.ToString("yyyy-MM-dd"),
            ["reason"] = (string?)null,
            ["createdAt"] = DateTimeOffset.UnixEpoch,
            ["updatedAt"] = DateTimeOffset.UnixEpoch,
        };

    private static string GradeJson(Guid gradeId, string name = "Grade 5", bool blocked = false) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = gradeId,
            ["codedValueId"] = Guid.NewGuid(),
            ["level"] = 5,
            ["name"] = name,
            ["displayOrder"] = 5,
            ["topicCount"] = 1,
            ["studentCount"] = 3,
            ["createdAt"] = DateTimeOffset.UnixEpoch,
            ["updatedAt"] = DateTimeOffset.UnixEpoch,
            ["minAge"] = 10,
            ["maxAge"] = 12,
            ["allowedGenderCodedValueId"] = (Guid?)null,
            ["isBlockedFromEnrollment"] = blocked,
        });

    private static Dictionary<string, object?> AssignmentJson(Guid assignmentId, Guid topicId, Guid gradeId) =>
        new()
        {
            ["id"] = assignmentId,
            ["audience"] = "grade",
            ["gradeLevelId"] = gradeId,
            ["activityGroupId"] = (Guid?)null,
            ["topicId"] = topicId,
            ["startDate"] = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
            ["endDate"] = (string?)null,
            ["topicStrandId"] = (Guid?)null,
            ["topicLessonId"] = (Guid?)null,
            ["periodId"] = (Guid?)null,
            ["createdAt"] = DateTimeOffset.UnixEpoch,
            ["updatedAt"] = DateTimeOffset.UnixEpoch,
        };

    /// <summary>One row of the grade's curriculum endpoint (topic + strand/lesson counts).</summary>
    private static Dictionary<string, object?> CurriculumJson(
        Guid topicId, string name = "Mathematics", string code = "MATH") =>
        new()
        {
            ["topicId"] = topicId,
            ["name"] = name,
            ["code"] = code,
            ["strandCount"] = 2,
            ["lessonCount"] = 3,
        };

    private static Dictionary<string, object?> TeacherJson(
        Guid teacherId,
        string firstName,
        string lastName,
        string email,
        Guid? roleId = null,
        params (Guid TopicId, string TopicName)[] topics) =>
        new()
        {
            ["id"] = teacherId,
            ["titleCodedValueId"] = (Guid?)null,
            ["firstName"] = firstName,
            ["lastName"] = lastName,
            ["displayName"] = (string?)null,
            ["email"] = email,
            ["contactPhone"] = (string?)null,
            ["isDeleted"] = false,
            ["teacherRoleCodedValueId"] = roleId,
            ["assignedTopics"] = topics.Select(t => new Dictionary<string, object?>
            {
                ["id"] = t.TopicId,
                ["codedValueId"] = (Guid?)null,
                ["code"] = (string?)null,
                ["name"] = t.TopicName,
                ["description"] = (string?)null,
                ["displayOrder"] = 0,
                ["createdAt"] = DateTimeOffset.UnixEpoch,
                ["updatedAt"] = DateTimeOffset.UnixEpoch,
            }).ToArray(),
            ["createdAt"] = DateTimeOffset.UnixEpoch,
            ["updatedAt"] = DateTimeOffset.UnixEpoch,
        };

    private static Dictionary<string, object?> StudentJson(
        Guid studentId, string studentNumber, string first, string last,
        Guid? genderId, DateOnly? dob) => new()
    {
        ["id"] = studentId,
        ["studentNumber"] = studentNumber,
        ["titleCodedValueId"] = (Guid?)null,
        ["firstName"] = first,
        ["lastName"] = last,
        ["dateOfBirth"] = dob,
        ["genderCodedValueId"] = genderId,
        ["isDeleted"] = false,
        ["createdAt"] = DateTimeOffset.UnixEpoch,
        ["updatedAt"] = DateTimeOffset.UnixEpoch,
    };

    [TestMethod]
    public void Detail_Overview_ShowsGradeNameAndSectionCards()
    {
        var gradeId = Guid.NewGuid();
        Register(gradeId, GradeJson(gradeId, "Grade 5"));

        var cut = Render<Detail>(p => p.Add(x => x.Id, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Grade 5"));

        cut.Markup.Should().Contain("Level");
        cut.Markup.Should().Contain("10–12", "age range renders as min–max");
        cut.Markup.Should().Contain("3 students");

        // Three equally-sized section cards render with titles + counts.
        cut.Markup.Should().Contain("Subjects", "Subjects card title renders");
        cut.Markup.Should().Contain("Teachers", "Teachers card title renders");
        cut.Markup.Should().Contain("Students", "Students card title renders");
    }

    [TestMethod]
    public void Detail_TopicsCard_Row_HasKebab_WithSecondaryActions()
    {
        var gradeId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        Register(
            gradeId,
            GradeJson(gradeId),
            topicsCatalogJson: JsonSerializer.Serialize(new[] { new Dictionary<string, object?>
            {
                ["id"] = topicId, ["codedValueId"] = (Guid?)null, ["code"] = "MATH",
                ["name"] = "Mathematics", ["description"] = (string?)null,
                ["displayOrder"] = 0, ["createdAt"] = DateTimeOffset.UnixEpoch, ["updatedAt"] = DateTimeOffset.UnixEpoch,
            } }),
            assignmentsJson: JsonSerializer.Serialize(new[] { AssignmentJson(Guid.NewGuid(), topicId, gradeId) }),
            curriculumJson: JsonSerializer.Serialize(new[] { new Dictionary<string, object?>
            {
                ["topicId"] = topicId, ["name"] = "Mathematics", ["code"] = "MATH",
                ["strandCount"] = 2, ["lessonCount"] = 3,
            } }));

        var cut = Render<Detail>(p => p.Add(x => x.Id, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("View all subjects (1)"));

        // Counts are informational (not links) now.
        cut.Markup.Should().Contain("2 strands", "strand count renders as plain text");
        cut.Markup.Should().Contain("3 lessons", "lesson count renders as plain text");

        // Open the topic row kebab to surface its inline menu items. The topic
        // name itself is the primary affordance (opens the topic edit dialog),
        // so the kebab hosts the remaining secondary actions.
        cut.Find("fluent-button[title=\"Actions for Mathematics\"]").Click();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Strands", "kebab offers strands"));
        cut.Markup.Should().Contain("Teachers", "kebab offers teachers");
        cut.Markup.Should().Contain("Remove", "kebab offers remove");
    }

    [TestMethod]
    public void Detail_TopicsCard_SubjectWithTwoExceptions_ShowsTheCount_AndViewAllDoesNotCrash()
    {
        // REWRITTEN to the exception model (subject-period-exception-model.md v3 §5.2).
        //
        // The old version of this test seeded TWO bridge rows for one (grade, topic)
        // and asserted both periods rendered from them. That premise is unreachable in
        // a real database: the filtered unique index
        // ix_topic_assignments_tenant_grade_topic_unique allows at most ONE bridge row
        // per (tenant, grade, topic). The multi-span case is now expressed the way the
        // schema can actually hold it — one bridge row (the subject IS offered) plus one
        // exception per span it is NOT offered in — and the card renders a COUNT badge.
        //
        // The crash this test used to guard is now STRUCTURAL: the page hands the dialog
        // a topic→count map built with GroupBy(...).ToDictionary(g => g.Key, g => g.Count()),
        // so a repeated topic key cannot throw. The guard survives as the assertion that
        // the View-all dialog opens at all with two exceptions on one subject.
        var gradeId = Guid.NewGuid();
        var topicId = Guid.NewGuid();

        Register(
            gradeId,
            GradeJson(gradeId),
            topicsCatalogJson: JsonSerializer.Serialize(new[] { new Dictionary<string, object?>
            {
                ["id"] = topicId, ["codedValueId"] = (Guid?)null, ["code"] = "MATH",
                ["name"] = "Mathematics", ["description"] = (string?)null,
                ["displayOrder"] = 0, ["createdAt"] = DateTimeOffset.UnixEpoch, ["updatedAt"] = DateTimeOffset.UnixEpoch,
            } }),
            // ONE bridge row — the subject is offered in this grade.
            assignmentsJson: JsonSerializer.Serialize(new[]
            {
                AssignmentJson(Guid.NewGuid(), topicId, gradeId),
            }),
            curriculumJson: JsonSerializer.Serialize(new[] { CurriculumJson(topicId) }),
            // TWO exceptions — the spans it is not offered for.
            exceptionsJson: JsonSerializer.Serialize(new[]
            {
                ExceptionJson(Guid.NewGuid(), gradeId, topicId, "Terms", ChipSpan1Start, ChipSpan1End),
                ExceptionJson(Guid.NewGuid(), gradeId, topicId, "Semesters", ChipSpan2Start, ChipSpan2End),
            }));

        var cut = Render<DialogHost>(p => p
            .AddChildContent<Detail>(x => x.Add(d => d.Id, gradeId)));

        // One card row for one subject, even though it carries two exceptions.
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("View all subjects (1)"));

        // The card shows a COUNT BADGE next to the subject NAME — never a period name, never a
        // span list, and no longer buried in the strand/lesson meta line (v7 follow-up: a count
        // among the strand/lesson text read as one more statistic, not as the spec's badge).
        cut.WaitForAssertion(() =>
        {
            var nameRow = cut.FindAll(".item-name-row")
                .Single(r => r.QuerySelector("fluent-badge") is not null);
            nameRow.QuerySelector(".item-name")!.TextContent.Trim().Should().Be("Mathematics",
                "the badge rides beside the subject NAME");
            nameRow.QuerySelector("fluent-badge")!.TextContent.Trim().Should().Be("2",
                "the badge carries the COUNT alone — the shared formatter is the tooltip");
            nameRow.QuerySelector(".item-name-count")!.TextContent.Trim().Should().Be("(2)",
                "the row reads SubjectText (2)");
            nameRow.ParentElement!.QuerySelector(".item-meta")!.TextContent.Should().NotContain("exception",
                "the count is a badge beside the name, not a meta-line statistic");
        });

        // The exceptions are READ-ONLY here: the kebab NAVIGATES to the management page
        // (see the source-wiring test), and the row's own actions are covered by
        // Detail_TopicsCard_Row_HasKebab_WithActions.

        // The crash itself: this used to throw before the dialog ever opened.
        cut.FindAll("fluent-anchor")
            .First(a => a.TextContent.Contains("View all subjects", StringComparison.Ordinal))
            .Click();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Topic actions"),
            TimeSpan.FromSeconds(5));
        cut.Find(".topic-dialog").TextContent.Should().Contain("2 exceptions",
            "the subject's exception count renders in the View-all dialog too");
    }

    [TestMethod]
    public void Detail_TopicsCard_ShowsEmptyState_WhenNoAssignments()
    {
        var gradeId = Guid.NewGuid();
        Register(gradeId, GradeJson(gradeId), assignmentsJson: "[]");

        var cut = Render<Detail>(p => p.Add(x => x.Id, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("View all subjects (0)"));
        cut.Markup.Should().Contain("No subjects assigned to this curriculum yet");
    }

    [TestMethod]
    public void Detail_TeachersCard_ShowsEmptyState_WhenNone()
    {
        var gradeId = Guid.NewGuid();
        Register(gradeId, GradeJson(gradeId));

        var cut = Render<Detail>(p => p.Add(x => x.Id, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Teachers"));
        cut.Markup.Should().Contain("No teachers linked to this grade yet");
    }

    [TestMethod]
    public void Detail_TeachersCard_Row_HasKebab_WithActions()
    {
        var gradeId = Guid.NewGuid();
        var teacherId = Guid.NewGuid();
        Register(gradeId, GradeJson(gradeId), teachersJson: JsonSerializer.Serialize(new[]
        {
            TeacherJson(teacherId, "Jane", "Doe", "jane@example.com"),
        }));

        var cut = Render<Detail>(p => p.Add(x => x.Id, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Jane Doe"));

        // The teacher name is the primary affordance (navigates to the teacher
        // detail page); the kebab hosts the secondary actions + destructive Remove.
        cut.Find("fluent-button[title=\"Actions for Jane Doe\"]").Click();
        var items = cut.FindAll("fluent-menu-item").Select(i => i.TextContent.Trim()).ToArray();
        items.Should().Contain("Role", "teachers kebab offers role");
        items.Should().Contain("View profile", "teachers kebab offers view profile");
        items.Should().Contain("Edit", "teachers kebab offers edit");
        items.Should().Contain("Remove", "teachers kebab offers remove");
    }

    [TestMethod]
    public void Detail_TeachersCard_Remove_Confirms_AndUnlinks()
    {
        var gradeId = Guid.NewGuid();
        var teacherId = Guid.NewGuid();
        var (handler, _) = Register(gradeId, GradeJson(gradeId), teachersJson: JsonSerializer.Serialize(new[]
        {
            TeacherJson(teacherId, "Jane", "Doe", "jane@example.com"),
        }));
        handler.Map("DELETE", $"/teachers/{teacherId}/grade-levels/{gradeId}", HttpStatusCode.OK, "");

        // Host the page under a FluentDialogProvider so the destructive Remove
        // confirmation prompt can render.
        var cut = Render<DialogHost>(p => p
            .AddChildContent<Detail>(child => child.Add(x => x.Id, gradeId)));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Jane Doe"));

        // Open the teacher kebab and click Remove.
        cut.Find("fluent-button[title=\"Actions for Jane Doe\"]").Click();
        var removeItem = cut.FindAll("fluent-menu-item").First(i => i.TextContent.Contains("Remove"));
        removeItem.Click();

        // The destructive Remove opens a MODAL confirmation dialog.
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Remove teacher 'Jane Doe' from this grade"));
        cut.WaitForAssertion(() => cut.FindAll(".confirm-dialog fluent-button[appearance='accent']").Any());

        // Confirm → the teacher is unlinked from this grade (stays in the catalog).
        cut.Find(".confirm-dialog fluent-button[appearance='accent']").Click();
        cut.WaitForAssertion(() => handler.Calls.Should().Contain(c =>
            c.Method == "DELETE" && c.Url == $"/teachers/{teacherId}/grade-levels/{gradeId}"));
    }

    [TestMethod]
    public void Detail_TeachersCard_Wires_PageErrorAlert()
    {
        // The Teachers card surfaces _teachersError (set by the mutation handlers /
        // ReloadTeachersAsync) via a PAGE-LEVEL message alert above the card —
        // the same pattern the Subjects card uses for _topicsError. Not the
        // SectionCard ErrorMessage param.
        var source = ReadDetailSource();
        source.Should().Contain("_teachersError",
            "the Teachers card surfaces _teachersError");
        source.Should().Contain("FluentMessageBar", "the error renders as a page message bar");
        source.Should().NotContain("ErrorMessage=\"@_teachersError\"",
            "the Teachers card does NOT use the SectionCard ErrorMessage param — page alert instead");
    }

    [TestMethod]
    public void Detail_TeachersCard_Add_OpensTeacherCreateDialog()
    {
        var source = ReadDetailSource();

        // The Teachers card Add button must open the shared TeacherEditDialog in
        // Create mode (new teacher), not just the link-existing GradeTeachersDialog.
        source.Should().Contain("OnAddClick=\"OpenTeacherCreateAsync\"",
            "the Teachers card Add button opens the create dialog");
        source.Should().Contain("ShowShellDialogAsync<TeacherEditDialog, TeacherEditDialog.TeacherFormModel, TeacherDto>",
            "the create handler opens TeacherEditDialog via the dialog shell");
        source.Should().Contain("ContextGradeLevelId = Id",
            "the create handler passes the current grade as the context grade");
        source.Should().Contain("ContextGradeLevelName = _grade.Name",
            "the create handler passes the context grade name");
    }

    [TestMethod]
    public void Detail_TeachersCard_Add_Click_OpensTeacherCreateDialog()
    {
        // BEHAVIORAL check (vs the source-only test above): clicking the Teachers
        // card Add button must actually open the shared TeacherEditDialog in Create
        // mode. This is the flow the source-inspection test never executes.
        var gradeId = Guid.NewGuid();
        var (handler, _) = Register(gradeId, GradeJson(gradeId));

        // Register IFeatureFlagService (required by TeacherEditDialog)
        Services.AddSingleton<IFeatureFlagService>(new StubFlagService { Enabled = true });
        Services.AddSingleton<IFeatureFlagChangeNotifier>(new StubFlagNotifier());

        // Endpoints the TeacherEditDialog hits on init (create mode).
        handler.Map("GET", "/students/grade-levels", HttpStatusCode.OK, "[]");
        handler.Map("GET", "/api/coded-values/by-parent?parentCode=QUALIF", HttpStatusCode.OK, "[]");

        var cut = Render<DialogHost>(p => p
            .AddChildContent<Detail>(child => child.Add(x => x.Id, gradeId)));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Teachers"));

        // The Subjects card (1st) and Teachers card (2nd) both use the SectionCard
        // default AddTitle="Add"; Students/Streams use distinct titles. So the
        // Teachers card's Add button is the 2nd "Add" button inside a section card.
        cut.FindAll("fluent-card.section-card-wrapper fluent-button[title=\"Add\"]")[1].Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain(
            "New Teacher",
            "clicking Add on the Teachers card opens the shared TeacherEditDialog in create mode"));
    }

    [TestMethod]
    public void Detail_StudentsCard_Edit_Click_OpensStudentEditDialog()
    {
        // BEHAVIORAL check: clicking the Students card kebab Edit must open the
        // shared StudentEditDialog pre-loaded with the row's student.
        var gradeId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var (handler, _) = Register(gradeId, GradeJson(gradeId), studentsJson: JsonSerializer.Serialize(new[]
        {
            StudentJson(studentId, "STU001", "Ada", "Lovelace", null, new DateOnly(2015, 3, 10)),
        }));
        handler.Map("GET", $"/students/{studentId}", HttpStatusCode.OK,
            JsonSerializer.Serialize(StudentJson(studentId, "STU001", "Ada", "Lovelace", null, new DateOnly(2015, 3, 10))));
        // The all-inclusive edit dialog also loads the student's guardians + contacts.
        handler.Map("GET", $"/students/{studentId}/guardians", HttpStatusCode.OK, "[]");
        handler.Map("GET", $"/contacts?ownerType=Student&ownerId={studentId}", HttpStatusCode.OK, "[]");

        var cut = Render<DialogHost>(p => p
            .AddChildContent<Detail>(child => child.Add(x => x.Id, gradeId)));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Ada Lovelace"));

        cut.Find("fluent-button[title=\"Actions for Ada Lovelace\"]").Click();
        var editItem = cut.FindAll("fluent-menu-item").First(i => i.TextContent.Contains("Edit"));
        editItem.Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain(
            "Save Changes",
            "clicking Edit on the Students card opens the shared StudentEditDialog"));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain(
            "Ada", "the edit dialog is pre-loaded with the selected student"));
    }

    [TestMethod]
    public void Detail_StudentsCard_Subitem_Click_OpensStudentEditDialog_WithStudentId()
    {
        // Regression: clicking a student's name in the Students section card
        // must open StudentEditDialog with that student's real Id (not an
        // empty guid). If an empty guid were passed, the dialog's
        // GET /students/{id} load would miss and show the error instead of
        // the pre-loaded student.
        var gradeId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var (handler, _) = Register(gradeId, GradeJson(gradeId), studentsJson: JsonSerializer.Serialize(new[]
        {
            StudentJson(studentId, "STU001", "Ada", "Lovelace", null, new DateOnly(2015, 3, 10)),
        }));
        handler.Map("GET", $"/students/{studentId}", HttpStatusCode.OK,
            JsonSerializer.Serialize(StudentJson(studentId, "STU001", "Ada", "Lovelace", null, new DateOnly(2015, 3, 10))));
        // The all-inclusive edit dialog also loads the student's guardians + contacts.
        handler.Map("GET", $"/students/{studentId}/guardians", HttpStatusCode.OK, "[]");
        handler.Map("GET", $"/contacts?ownerType=Student&ownerId={studentId}", HttpStatusCode.OK, "[]");

        var cut = Render<DialogHost>(p => p
            .AddChildContent<Detail>(child => child.Add(x => x.Id, gradeId)));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Ada Lovelace"));

        // Click the student's name (the section-card subitem anchor).
        cut.Find(".item-name").Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain(
            "Save Changes",
            "clicking the student subitem opens the shared StudentEditDialog"));
        // Definitive check: the FirstName input must be bound to the loaded
        // student profile. A mere "Ada" match on the markup is a false positive
        // (the dialog title is "Edit Student · Ada Lovelace"). If StudentId were
        // an empty guid (never bound via ShowReadonlyDialogAsync), the dialog's
        // GET /students/{id} would 404 and the field would render blank.
        cut.WaitForAssertion(() => cut.Find("#studentFormFirstName").GetAttribute("value")
            .Should().Be("Ada",
                "the edit dialog's FirstName input binds the loaded student (StudentId must be a real guid)"));
    }

    [TestMethod]
    public void Detail_StudentsCard_Subitem_Click_PassesCorrectStudent_WhenMultiple()
    {
        // Regression: with multiple students, clicking a specific subitem must
        // pass THAT student's Id to the edit dialog — not the last student, not
        // an empty guid (a classic @foreach closure-capture mistake).
        var gradeId = Guid.NewGuid();
        var adaId = Guid.NewGuid();
        var graceId = Guid.NewGuid();
        var (handler, _) = Register(gradeId, GradeJson(gradeId), studentsJson: JsonSerializer.Serialize(new[]
        {
            StudentJson(adaId, "STU001", "Ada", "Lovelace", null, new DateOnly(2015, 3, 10)),
            StudentJson(graceId, "STU002", "Grace", "Hopper", null, new DateOnly(2016, 4, 20)),
        }));
        handler.Map("GET", $"/students/{adaId}", HttpStatusCode.OK,
            JsonSerializer.Serialize(StudentJson(adaId, "STU001", "Ada", "Lovelace", null, new DateOnly(2015, 3, 10))));
        handler.Map("GET", $"/students/{graceId}", HttpStatusCode.OK,
            JsonSerializer.Serialize(StudentJson(graceId, "STU002", "Grace", "Hopper", null, new DateOnly(2016, 4, 20))));
        // The all-inclusive edit dialog also loads each student's guardians + contacts.
        handler.Map("GET", $"/students/{adaId}/guardians", HttpStatusCode.OK, "[]");
        handler.Map("GET", $"/contacts?ownerType=Student&ownerId={adaId}", HttpStatusCode.OK, "[]");
        handler.Map("GET", $"/students/{graceId}/guardians", HttpStatusCode.OK, "[]");
        handler.Map("GET", $"/contacts?ownerType=Student&ownerId={graceId}", HttpStatusCode.OK, "[]");

        var cut = Render<DialogHost>(p => p
            .AddChildContent<Detail>(child => child.Add(x => x.Id, gradeId)));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Ada Lovelace"));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Grace Hopper"));

        // Click the SECOND student's name (Grace Hopper).
        cut.FindAll(".item-name")[1].Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain(
            "Save Changes",
            "clicking a student subitem opens the shared StudentEditDialog"));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain(
            "Grace", "the edit dialog pre-loads the clicked student (not the last/empty one)"));
        cut.Markup.Should().NotContain("Could not load",
            "the dialog must receive a real Id, not an empty guid that 404s");
    }

    [TestMethod]
    public void Detail_StudentsCard_ShowsEmptyState_WhenNoStudents()
    {
        var gradeId = Guid.NewGuid();
        Register(gradeId, GradeJson(gradeId), studentsJson: "[]");

        var cut = Render<Detail>(p => p.Add(x => x.Id, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("View all students (0)"));
        cut.Markup.Should().Contain("No active students in this grade for the current period.");
    }

    [TestMethod]
    public void Detail_StudentsCard_Row_HasKebab_WithActions()
    {
        var gradeId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        Register(gradeId, GradeJson(gradeId), studentsJson: JsonSerializer.Serialize(new[]
        {
            StudentJson(studentId, "STU001", "Ada", "Lovelace", null, new DateOnly(2015, 3, 10)),
        }));

        var cut = Render<Detail>(p => p.Add(x => x.Id, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Ada Lovelace"));

        // The student name is the primary affordance (navigates to the student
        // view page); the kebab hosts the secondary actions + destructive Remove.
        cut.Find("fluent-button[title=\"Actions for Ada Lovelace\"]").Click();
        var items = cut.FindAll("fluent-menu-item").Select(i => i.TextContent.Trim()).ToArray();
        items.Should().Contain("Transfer", "students kebab offers transfer");
        items.Should().Contain("Withdraw", "students kebab offers withdraw");
        items.Should().Contain("View profile", "students kebab offers view profile");
        items.Should().Contain("Edit", "students kebab offers edit");
        items.Should().Contain("Remove", "students kebab offers remove");
    }

    [TestMethod]
    public void Detail_StudentsCard_Remove_Confirms_AndSoftDeletes()
    {
        var gradeId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var (handler, _) = Register(gradeId, GradeJson(gradeId), studentsJson: JsonSerializer.Serialize(new[]
        {
            StudentJson(studentId, "STU001", "Ada", "Lovelace", null, new DateOnly(2015, 3, 10)),
        }));
        handler.Map("DELETE", $"/students/{studentId}", HttpStatusCode.OK, "");

        // Host the page under a FluentDialogProvider so the destructive Remove
        // confirmation prompt can render.
        var cut = Render<DialogHost>(p => p
            .AddChildContent<Detail>(child => child.Add(x => x.Id, gradeId)));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Ada Lovelace"));

        // Open the student kebab and click Remove.
        cut.Find("fluent-button[title=\"Actions for Ada Lovelace\"]").Click();
        var removeItem = cut.FindAll("fluent-menu-item").First(i => i.TextContent.Contains("Remove"));
        removeItem.Click();

        // The destructive Remove opens a MODAL confirmation dialog.
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Remove student 'Ada Lovelace'?"));
        cut.WaitForAssertion(() => cut.FindAll(".confirm-dialog fluent-button[appearance='accent']").Any());

        // Confirm → the whole student record is soft-deleted (not an enrollment-withdraw).
        cut.Find(".confirm-dialog fluent-button[appearance='accent']").Click();
        cut.WaitForAssertion(() => handler.Calls.Should().Contain(c =>
            c.Method == "DELETE" && c.Url == $"/students/{studentId}"));
    }

    [TestMethod]
    public void Detail_StudentsCard_Withdraw_ResolvesEnrollment_AndOpensDialog()
    {
        var gradeId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var enrollmentId = Guid.NewGuid();
        var (handler, _) = Register(gradeId, GradeJson(gradeId), studentsJson: JsonSerializer.Serialize(new[]
        {
            StudentJson(studentId, "STU001", "Ada", "Lovelace", null, new DateOnly(2015, 3, 10)),
        }));
        handler.Map("GET", $"/students/enrollments/by-student/{studentId}", HttpStatusCode.OK,
            JsonSerializer.Serialize(new[]
            {
                new Dictionary<string, object?>
                {
                    ["id"] = enrollmentId, ["studentId"] = studentId, ["periodId"] = Guid.NewGuid(),
                    ["gradeLevelId"] = gradeId, ["streamCodedValueId"] = (Guid?)null,
                    ["enrolledOn"] = "2025-01-01", ["exitDate"] = (string?)null,
                    ["status"] = "Active",
                    ["createdAt"] = DateTimeOffset.UnixEpoch, ["updatedAt"] = DateTimeOffset.UnixEpoch,
                },
            }));

        // Host the page under a FluentDialogProvider so the WithdrawEnrollmentDialog
        // (opened via ShowShellDialogAsync) can render.
        var cut = Render<DialogHost>(p => p
            .AddChildContent<Detail>(child => child.Add(x => x.Id, gradeId)));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Ada Lovelace"));

        // Open the student kebab and click Withdraw.
        cut.Find("fluent-button[title=\"Actions for Ada Lovelace\"]").Click();
        var withdrawItem = cut.FindAll("fluent-menu-item").First(i => i.TextContent.Contains("Withdraw"));
        withdrawItem.Click();

        // The WithdrawEnrollmentDialog opens with the resolved active enrollment.
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("This will set the exit date on the current enrollment."));
    }

    [TestMethod]
    public void Detail_StreamsCard_ShowsEmptyState_WhenNone()
    {
        var gradeId = Guid.NewGuid();
        Register(gradeId, GradeJson(gradeId));

        var cut = Render<Detail>(p => p.Add(x => x.Id, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("No streams defined for this grade yet."));
    }

    [TestMethod]
    public void Detail_StreamsCard_Row_HasKebab_WithEditAndRemove()
    {
        var gradeId = Guid.NewGuid();
        Register(gradeId, GradeJson(gradeId), streamsJson: JsonSerializer.Serialize(new[]
        {
            StreamJson(Guid.NewGuid(), Guid.NewGuid(), gradeId, "Grade 5A", "GR5A"),
        }));

        var cut = Render<Detail>(p => p.Add(x => x.Id, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Grade 5A"));

        // The stream name is the primary affordance (opens the edit dialog); the
        // kebab hosts the secondary actions (Edit + destructive Remove).
        cut.Find("fluent-button[title=\"Actions for Grade 5A\"]").Click();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Edit", "streams kebab offers edit"));
        cut.Markup.Should().Contain("Remove", "streams kebab offers remove");
    }

    /// <summary>
    /// Regression: the Streams card's "View all" must open the GRADE-scoped
    /// read-only list dialog (the card's own bridge-loaded data), never
    /// navigate to the unfiltered <c>/coded-values/GRSTREAMS/children</c>
    /// catalogue page. The card's source is the <c>GradeStreamAssignment</c>
    /// bridge, already filtered to this grade, so a cross-grade catalogue
    /// link is the wrong target — the previous
    /// <c>ViewAllNavigationUrl="/coded-values/GRSTREAMS/children"</c> wiring.
    /// </summary>
    [TestMethod]
    public void Detail_StreamsCard_ViewAll_OpensGradeScopedListDialog_NotCataloguePage()
    {
        var gradeId = Guid.NewGuid();
        Register(gradeId, GradeJson(gradeId), streamsJson: JsonSerializer.Serialize(new[]
        {
            StreamJson(Guid.NewGuid(), Guid.NewGuid(), gradeId, "Grade 5A", "GR5A"),
            StreamJson(Guid.NewGuid(), Guid.NewGuid(), gradeId, "Grade 5B", "GR5B"),
        }));

        // DialogHost provides the FluentDialogProvider so the view-all dialog
        // can render; the assertion is on the dialog's own markup.
        var cut = Render<DialogHost>(p => p
            .AddChildContent<Detail>(child => child.Add(x => x.Id, gradeId)));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("View all streams (2)"));

        // The unfiltered catalogue navigation is gone from the card entirely.
        cut.Markup.Should().NotContain("/coded-values/GRSTREAMS/children",
            "the Streams card must never link to the cross-grade coded-values catalogue");

        cut.FindAll("fluent-anchor")
            .First(a => a.TextContent.Contains("View all streams", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() => cut.FindAll(".section-list-dialog").Should().NotBeEmpty());
        var dialog = cut.Find(".section-list-dialog");
        dialog.TextContent.Should().Contain("Grade 5A", "the dialog lists this grade's streams");
        dialog.TextContent.Should().Contain("Grade 5B", "the dialog lists this grade's streams");
    }

    [TestMethod]
    public void Detail_StreamsCard_Remove_DeletesTheBridgeRow_AndNeverDisablesTheCodedValue()
    {
        var gradeId = Guid.NewGuid();
        var assignmentId = Guid.NewGuid();
        var (handler, _) = Register(gradeId, GradeJson(gradeId), streamsJson: JsonSerializer.Serialize(new[]
        {
            StreamJson(assignmentId, Guid.NewGuid(), gradeId, "Grade 5A", "GR5A"),
        }));

        // Host the page under a FluentDialogProvider so the destructive Remove
        // confirmation prompt can render.
        var cut = Render<DialogHost>(p => p
            .AddChildContent<Detail>(child => child.Add(x => x.Id, gradeId)));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Grade 5A"));

        // Open the stream kebab and click Remove.
        cut.Find("fluent-button[title=\"Actions for Grade 5A\"]").Click();
        var removeItem = cut.FindAll("fluent-menu-item").First(i => i.TextContent.Contains("Remove"));
        removeItem.Click();

        // The destructive Remove opens a MODAL confirmation dialog.
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Stop offering stream 'Grade 5A' for this grade?"));
        cut.WaitForAssertion(() => cut.FindAll(".confirm-dialog fluent-button[appearance='accent']").Any());

        // Confirm -> the grade stops OFFERING the stream: the bridge row is deleted
        // and the coded value stays in the catalogue (never disabled, never deleted).
        cut.Find(".confirm-dialog fluent-button[appearance='accent']").Click();
        cut.WaitForAssertion(() => handler.Calls.Should().Contain(c =>
            c.Method == "DELETE" && c.Url == $"/students/grade-levels/{gradeId}/streams/{assignmentId}"));
        handler.Calls.Should().NotContain(c => c.Url.Contains("/disable"),
            "removing a stream from a grade must never disable the catalogue value");
    }

    /// <summary>
    /// The Streams card must RE-RENDER after a remove. The stream reload runs inside a
    /// callback owned by a child component (the row kebab in <c>SectionCard</c>), so
    /// Blazor does not re-render this page automatically — <c>ReloadStreamsAsync</c>
    /// must call <c>StateHasChanged</c>. The bridge read is answered dynamically: the
    /// row is served until the DELETE lands, then an empty list. Without the re-render
    /// the card keeps showing the stale row, so this assertion fails.
    /// </summary>
    [TestMethod]
    public void Detail_StreamsCard_Remove_RefreshesTheCard_SoTheRemovedStreamDisappears()
    {
        var gradeId = Guid.NewGuid();
        var assignmentId = Guid.NewGuid();
        var (handler, _) = Register(gradeId, GradeJson(gradeId));

        var beforeDelete = JsonSerializer.Serialize(new[]
        {
            StreamJson(assignmentId, Guid.NewGuid(), gradeId, "Grade 5A", "GR5A"),
        });
        handler.MapDynamic("GET", $"/students/grade-levels/{gradeId}/streams",
            () => handler.Calls.Any(c => c.Method == "DELETE") ? "[]" : beforeDelete);

        var cut = Render<DialogHost>(p => p
            .AddChildContent<Detail>(child => child.Add(x => x.Id, gradeId)));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Grade 5A"));

        cut.Find("fluent-button[title=\"Actions for Grade 5A\"]").Click();
        var removeItem = cut.FindAll("fluent-menu-item").First(i => i.TextContent.Contains("Remove"));
        removeItem.Click();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Stop offering stream 'Grade 5A' for this grade?"));
        cut.Find(".confirm-dialog fluent-button[appearance='accent']").Click();

        // The card must drop the removed row — proving the page re-rendered with the
        // reloaded (empty) bridge list.
        cut.WaitForAssertion(() => cut.Markup.Should().NotContain("Grade 5A",
            "a removed stream must disappear from the card once the bridge read no longer returns it"));
    }

    [TestMethod]
    public void Detail_StreamsCard_RendersErrorMessage_OnLoadFailure()
    {
        var gradeId = Guid.NewGuid();
        // Invalid JSON makes the streams fetch throw, so the card must surface a
        // page-level error alert (the Subjects/Topic card pattern) instead of
        // failing silently. The card's empty state may still render below the
        // alert — the alert itself is the contract.
        Register(gradeId, GradeJson(gradeId), streamsJson: "not-json");

        var cut = Render<Detail>(p => p.Add(x => x.Id, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Could not load streams",
            "a load failure renders a page-level error alert, not a silent empty state"));
    }

    [TestMethod]
    public void Detail_StreamsAdd_Click_OpensStreamCreateDialog()
    {
        // AC6(c): the Streams card's Add affordance OPENS the new dialog (the
        // dialog-opener contract). Hosting the page under a FluentDialogProvider
        // renders the real dialog content, so the assertion is on the dialog's own
        // markup rather than on a mocked IDialogService.
        var gradeId = Guid.NewGuid();
        Register(gradeId, GradeJson(gradeId));

        var cut = Render<DialogHost>(p => p
            .AddChildContent<Detail>(child => child.Add(x => x.Id, gradeId)));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("No streams defined for this grade yet."));

        cut.Find("fluent-button[title=\"Add stream\"]").Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Pick existing stream",
            "the Streams Add button opens StreamCreateDialog"));
        cut.Markup.Should().Contain("Version label", "the dialog writes the streamVersion label when supplied");
        cut.Markup.Should().Contain("Add stream", "the dialog's submit is the stream create action");
    }

    [TestMethod]
    public void Detail_StreamsAdd_OpensStreamCreateDialog_AndNeverWritesTheGradeLevelAttribute()
    {
        var source = ReadDetailSource();

        // The Streams card "Add" affordance must open the new StreamCreateDialog
        // (pick-an-existing-catalogue-stream / create-new), not the raw
        // CodedValueDialog with a manual `gradeLevel` attribute write.
        source.Should().Contain("OnAddClick=\"OpenStreamCreateAsync\"",
            "the Streams card Add button opens the create dialog, not a navigation");
        source.Should().Contain("StreamCreateDialog.StreamCreateModel",
            "the create handler opens StreamCreateDialog with its own model");
        source.Should().NotContain("CodedValueFormModel.ForCreate",
            "the raw coded-value create dialog is no longer the stream add flow");
        source.Should().NotContain("SetAttributeAsync(created.Id, \"gradeLevel\"",
            "the gradeLevel attribute is no longer written on the create path");
        source.Should().Contain("ReloadStreamsAsync",
            "the create handler reloads the streams card after creating");

        // The old navigation-away behavior is gone.
        source.Should().NotContain("OpenStreamsAsync", "the navigation-away handler is removed");
    }

    [TestMethod]
    public void Detail_EnrollmentToggle_ReflectsBlockedState()
    {
        var gradeId = Guid.NewGuid();
        Register(gradeId, GradeJson(gradeId, blocked: false));

        var cut = Render<Detail>(p => p.Add(x => x.Id, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Allowed"));

        // The overview reflects the not-blocked state as a status badge, and the
        // enrollment toggle is grouped in the header "Actions" menu (not a switch).
        cut.Markup.Should().Contain("Enrollment");
        cut.Find("fluent-button[title='Actions']").Should().NotBeNull();

        // Open the header Actions menu → Edit + the enrollment toggle render inline.
        cut.Find("fluent-button[title='Actions']").Click();
        cut.Markup.Should().Contain("Edit");
        cut.Markup.Should().Contain("Block enrollment");
    }

    [TestMethod]
    public void Detail_ViewAll_Wires_TopicsAndTeachers_Dialogs()
    {
        var source = ReadDetailSource();

        source.Should().Contain("ShowReadonlyDialogAsync<GradeTopicsDialog>(",
            "the Subjects card's View-all opens GradeTopicsDialog via the read-only helper");

        // GradeTopicsDialog gets the assigned topics + assignable catalog + action callbacks
        // (passed via the Content DialogParameters indexer keys).
        source.Should().Contain("GradeTopicsDialog.TopicsKey");
        source.Should().Contain("GradeTopicsDialog.UnassignedTopicsKey");
        source.Should().Contain("GradeTopicsDialog.RemoveKey");
        source.Should().Contain("GradeTopicsDialog.AssignKey");

        // Students card's View-all navigates to the grade-filtered students landing.
        source.Should().Contain("View all students");
        source.Should().Contain("/students?gradeLevelId=");

        // Subjects card's Add button opens the shared TopicCreateDialog (add-new
        // subject/topic wired to the grade), and its View-all opens GradeTopicsDialog
        // to assign existing topics / manage the assigned list.
        source.Should().Contain("OnAddClick=\"OpenTopicCreateAsync\"",
            "the Subjects card's Add button opens the topic create dialog");
        source.Should().Contain("OnViewAllClick=\"OpenTopicsDialogAsync\"",
            "the Subjects card's View-all opens GradeTopicsDialog");

        // The old segmented pill tab control is gone.
        source.Should().NotContain("grade-tabs__bar");
        source.Should().NotContain("SetActiveTab");
    }

    [TestMethod]
    public void Detail_SubjectsCard_Add_OpensTopicCreateDialog()
    {
        var gradeId = Guid.NewGuid();
        Register(gradeId, GradeJson(gradeId));

        var cut = Render<Detail>(p => p.Add(x => x.Id, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("View all subjects (0)"));

        // Bug fix: the Subjects card's Add button must open the shared
        // TopicCreateDialog (add-new subject/topic wired to the grade), following
        // the same DialogShellBase pattern as the topic edit dialog — not just the
        // assign-existing-topics GradeTopicsDialog. The subject is display-only;
        // the underlying entity is a Topic, and creating one never renames an
        // existing topic.
        var source = ReadDetailSource();
        source.Should().Contain("OnAddClick=\"OpenTopicCreateAsync\"",
            "the Subjects card's Add button opens the topic create dialog");
        source.Should().Contain("ShowShellDialogAsync<", "the add handler uses the shell dialog service");
        source.Should().Contain("TopicCreateDialog", "the add handler opens TopicCreateDialog");
        source.Should().Contain("GradeLevelId = _grade.Id",
            "the add handler passes the grade context so the subject is wired to the grade");
    }

    [TestMethod]
    public void Detail_TopicLine_OpensTopicEditDialog()
    {
        var source = ReadDetailSource();

        // The topic name is the primary affordance in the Subjects card and opens
        // the topic edit dialog (rename / code / description), not the strands dialog.
        source.Should().Contain("ItemOnClick=\"t => OpenTopicEditAsync(t)\"",
            "the topic name click must invoke the edit-dialog handler");
        source.Should().Contain("ItemNameTitle=\"Edit topic\"",
            "the topic anchor advertises the edit affordance");

        // The edit handler opens TopicEditDialog through the shell dialog helper.
        source.Should().Contain("ShowShellDialogAsync<", "the topic edit handler uses the shell dialog service");
        source.Should().Contain("TopicEditDialog", "the topic edit handler opens TopicEditDialog");
        source.Should().Contain("size: DialogSize.Large",
            "the topic edit dialog opens large to fit the inline strands editor");

        // Strands stay reachable (moved to the row kebab, not removed).
        source.Should().Contain("OpenStrandsAsync", "strands remain available via the row kebab");
        source.Should().Contain("ShowReadonlyDialogAsync<TopicStrandsDialog>",
            "strands are opened via the read-only helper");
    }

    [TestMethod]
    public void Detail_SectionCards_HaveAddIconButtons()
    {
        var source = ReadDetailSource();

        // Each card uses the SectionCard component with add button parameters.
        source.Should().Contain("<SectionCard", "SectionCard component is used for all three cards");
        source.Should().Contain("OnAddClick=\"OpenTopicCreateAsync\"", "Subjects card has add callback");
        source.Should().Contain("OnAddClick=\"OpenTeacherCreateAsync\"", "Teachers card has add callback");
        source.Should().Contain("OnAddClick=\"OpenStudentCreateAsync\"", "Students card has add callback");
        source.Should().Contain("AddTitle=\"Add student\"", "Students card has add title");
        source.Should().Contain("AddAriaLabel=\"Add student\"", "Students card has add aria-label");
    }

    [TestMethod]
    public void Detail_SectionCards_Wire_ItemSelectors_And_PrimaryAffordances()
    {
        // The SectionCard rendering mechanics (text/meta/href/click/tooltip) are
        // covered once in SectionCardTests.cs. These source-inspection assertions
        // verify the per-card WIRING — which selector / primary affordance each
        // card binds — so a card can't silently regress to a different selector.
        var source = ReadDetailSource();

        // Subjects card: topic name + strand/lesson counts; name opens the edit dialog.
        source.Should().Contain("ItemTextSelector=\"t => t.Name\"", "Subjects card binds the topic name");
        source.Should().Contain("ItemMetaSelector=\"SubjectMeta\"", "Subjects card binds the meta selector");
        source.Should().Contain("private string[] SubjectMeta(", "SubjectMeta renders the meta line parts");
        // TRAP REPLACEMENT (i), intent-preserving: v1 asserted the card sourced its
        // per-period label from the (now retired) label helper. That label concept
        // dissolved into a COUNT badge, and the badge now sits NEXT TO THE SUBJECT NAME
        // (spec §5.2) via the SectionCard ItemNameSuffix slot — not in the meta line,
        // where it read as one more strand/lesson statistic. The intent — "the card shows
        // this subject's enrollment-exception count" — is asserted at its new source: the
        // per-topic count feeding the badge.
        source.Should().Contain("<ItemNameSuffix Context=\"t\">",
            "the card wires the badge into the shared next-to-the-name slot");
        source.Should().Contain("ExceptionCount(t.TopicId)",
            "the badge is fed by the subject's per-topic exception COUNT");
        source.Should().Contain("EnrollmentExceptionLabels.FormatCount(exceptionCount)",
            "the badge text is rendered from that count by the ONE shared count formatter");
        // TRAP REPLACEMENT (ii), intent-preserving: v1 asserted the card carried NO
        // "Enrollment exceptions" editor. The guard's intent ("no inline editor; the
        // affordance OPENS the editor") survives under v8's mechanism — the kebab action is a
        // CALLBACK that opens the EnrollmentExceptionsDialog scoped to this grade + subject
        // (spec §5.2 v8), and it is the only path to the editor, so the card itself keeps no
        // list, no form and no period field.
        source.Should().Contain("RowAction.Callback(\"Enrollment exceptions\"",
            "the card's exceptions affordance OPENS the management dialog");
        source.Should().Contain("EnrollmentExceptionsDialog.EnrollmentExceptionsModel",
            "the callback builds the dialog's locked scope (owner + subject)");
        source.Should().Contain("OpenEnrollmentExceptionsAsync(",
            "and the callback is the dialog-opening method — not a second inline editor");
        source.Should().NotContain("RowAction.Navigate(\"Enrollment exceptions\"",
            "M3: wiring the exceptions affordance as a navigation (to the retired page) must fail this test");
        source.Should().Contain("ItemOnClick=\"t => OpenTopicEditAsync(t)\"", "Subjects card name opens the topic edit dialog");
        source.Should().Contain("ItemKeySelector=\"t => t.TopicId\"", "Subjects card opts into the central edit-key guard (TopicId)");
        source.Should().Contain("GradeTopicsDialog.ExceptionCountsByTopicKey",
            "the View-all dialog receives the subject's exception COUNTS");
        source.Should().Contain(".ToDictionary(g => g.Key, g => g.Count())",
            "counts are grouped per topic: a repeat can no longer collide on a topic key");
        source.Should().Contain("OnItemActionBlocked=\"OnTopicEditBlocked\"", "Subjects card surfaces the guard block");
        source.Should().Contain("ItemNameTitle=\"Edit topic\"", "Subjects card advertises the edit affordance");

        // Teachers card: display name + role; name navigates to the teacher detail page.
        source.Should().Contain("ItemTextSelector=\"t => GetTeacherDisplayName(t)\"", "Teachers card binds the teacher display name");
        source.Should().Contain("ItemMetaSelector=\"@(t => [ GetTeacherRole(t) ])\"", "Teachers card binds the role meta");
        source.Should().Contain("ItemHrefSelector=\"@(t => $\"/students/teachers/{t.Id}\")\"", "Teachers card name navigates to the teacher detail page");

        // Students card: full name + demographics; name opens the edit dialog.
        source.Should().Contain("ItemTextSelector=\"@(st => $", "Students card binds the student full name");
        source.Should().Contain("ItemMetaSelector=\"@(st => [ GetStudentDemographics(st) ])\"", "Students card binds demographics meta");
        source.Should().Contain("ItemOnClick=\"st => OpenStudentEditAsync(st)\"", "Students card name opens the student edit dialog");
        source.Should().Contain("ItemKeySelector=\"st => st.Id\"", "Students card opts into the central edit-key guard (Id)");
        source.Should().Contain("OnItemActionBlocked=\"OnStudentEditBlocked\"", "Students card surfaces the guard block");
        source.Should().Contain("ItemNameTitle=\"Edit student\"", "Students card advertises the edit affordance");

        // Streams card: stream name; name opens the stream edit dialog.
        source.Should().Contain("ItemTextSelector=\"s => s.Name\"", "Streams card binds the stream name");
        source.Should().Contain("ItemOnClick=\"s => OpenStreamEditAsync(s)\"", "Streams card name opens the stream edit dialog");
        source.Should().Contain("ItemKeySelector=\"s => s.StreamCodedValueId\"", "Streams card opts into the central edit-key guard (coded value id)");
        source.Should().Contain("OnItemActionBlocked=\"OnStreamEditBlocked\"", "Streams card surfaces the guard block");
        source.Should().Contain("ItemNameTitle=\"Edit stream\"", "Streams card advertises the edit affordance");
    }

    [TestMethod]
    public void Detail_StudentAdd_OpensStudentCreateDialog()
    {
        var source = ReadDetailSource();

        // The Students card Add button must open the shared StudentCreateDialog
        // (new student), not the enroll-existing StudentPickerDialog.
        source.Should().Contain("OnAddClick=\"OpenStudentCreateAsync\"",
            "the Students card Add button opens the create dialog");
        source.Should().Contain("ShowReadonlyDialogAsync<StudentCreateDialog>",
            "the create handler opens StudentCreateDialog");
        source.Should().NotContain("OpenAddStudentsAsync",
            "the enroll-existing handler is removed from the card Add");
    }

    [TestMethod]
    public void Detail_NotificationEditor_IsWired()
    {
        var source = ReadDetailSource();

        source.Should().Contain("GradeNotificationPolicyEditor",
            "the merged nd/4 feature hosts the per-grade notification & delivery editor on the grade detail page");
        source.Should().Contain("notification-card",
            "the editor is wrapped in a Notification & Delivery card below the section cards");
        source.Should().Contain("Notification &amp; Delivery",
            "the card is titled 'Notification & Delivery'");
    }

    [TestMethod]
    public void Detail_RendersAssignmentPolicyCard()
    {
        // Precedent: Detail_NotificationEditor_IsWired uses ReadDetailSource().
        var source = ReadDetailSource();

        source.Should().Contain("AssignmentPolicyEditor",
            "the assignment-policy card hosts the per-grade assignment policy editor component (D6, " +
            "absorbing the retired guardian-signature row)");
        source.Should().Contain("Assignment Policy",
            "the card is titled 'Assignment Policy'");
        source.Should().NotContain("GradeSignaturePolicyEditor",
            "the retired component is gone from its only host");
    }

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
}
