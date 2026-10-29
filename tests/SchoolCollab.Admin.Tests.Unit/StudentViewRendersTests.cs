using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Students.Application.Components.Pages.Students;
using SchoolCollab.Students.Application.Services;
using SchoolCollab.Students.Core.Contracts;
using SchoolCollab.Students.Core.Domain;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// Renders the student view page for real (bUnit + a scripted API) and asserts what a reader sees.
/// <para>
/// Every other student-view test reads the .razor file as TEXT. That is the right tool for a structural
/// invariant, and the wrong one for "does the page draw": a source string can be present while the
/// markup around it discards it — the repo already has one incident of a shell that rendered every page
/// empty while hundreds of bUnit tests stayed green. This fixture closes that gap for the redesign: it
/// proves the page renders its blocks, its status pill, the subject bands and the contact chips from real
/// payloads, and that the panel it renders inside is the shared skeleton.
/// </para>
/// <para>
/// The API is stubbed at the HTTP layer (not with a mocking framework) because <c>StudentsApiClient</c> is
/// a concrete sealed class with 100+ methods — the same technique the Grade-Level Detail fixture uses.
/// Unmapped URLs answer <c>[]</c> so a read this page grows later cannot fail the whole fixture, and every
/// read the assertions depend on is additionally pinned by the <c>Calls</c> list.
/// </para>
/// </summary>
[TestClass]
public class StudentViewRendersTests : BunitContext
{
    private static readonly Guid StudentId = Guid.Parse("a0000000-0000-0000-0000-000000000001");
    private static readonly Guid GradeId = Guid.Parse("b0000000-0000-0000-0000-000000000008");
    private static readonly Guid PeriodId = Guid.Parse("c0000000-0000-0000-0000-000000000001");
    private static readonly Guid StreamId = Guid.Parse("d0000000-0000-0000-0000-00000000000a");
    private static readonly Guid TopicMaths = Guid.Parse("e0000000-0000-0000-0000-000000000001");
    private static readonly Guid TopicEthics = Guid.Parse("e0000000-0000-0000-0000-000000000002");
    private static readonly Guid TopicScience = Guid.Parse("e0000000-0000-0000-0000-000000000003");
    private static readonly Guid GuardianId = Guid.Parse("f0000000-0000-0000-0000-000000000001");
    private static readonly Guid GroupId = Guid.Parse("11000000-0000-0000-0000-000000000001");

    public StudentViewRendersTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
        Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
    }

    // ── The payloads ────────────────────────────────────────────────────────

    private static string StudentJson => $$"""
        {
          "id": "{{StudentId}}",
          "studentNumber": "SC-2026-0001",
          "firstName": "Jordan",
          "lastName": "Dlamini",
          "dateOfBirth": "2013-04-18",
          "genderCodedValueId": null,
          "genderName": "Female",
          "isDeleted": false,
          "createdAt": "2026-01-12T08:30:00+00:00",
          "updatedAt": "2026-10-20T14:05:00+00:00"
        }
        """;

    /// <summary>
    /// The active placement is dated YESTERDAY so the page has to render "1 day", not "1 days". The first
    /// live render of this page said "1 days in this placement"; no test saw it, because the existing
    /// assertions were about the block existing rather than about the number's wording.
    /// </summary>
    private static string Yesterday => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)).ToString("yyyy-MM-dd");

    private static string EnrollmentsJson => $$"""
        [
          { "id": "{{Guid.NewGuid()}}", "studentId": "{{StudentId}}", "gradeLevelId": "{{GradeId}}",
            "periodId": "{{PeriodId}}", "streamCodedValueId": "{{StreamId}}", "enrolledOn": "{{Yesterday}}",
            "exitDate": null, "status": "Active", "isActive": true },
          { "id": "{{Guid.NewGuid()}}", "studentId": "{{StudentId}}", "gradeLevelId": "{{GradeId}}",
            "periodId": "{{PeriodId}}", "streamCodedValueId": null, "enrolledOn": "2025-01-15",
            "exitDate": "2025-12-05", "status": "Completed", "isActive": false }
        ]
        """;

    private const string GradeLevelsJson = """
        [ { "id": "b0000000-0000-0000-0000-000000000008", "name": "Year 8", "code": "Y8",
            "displayOrder": 8, "isActive": true } ]
        """;

    private const string PeriodsJson = """
        [ { "id": "c0000000-0000-0000-0000-000000000001", "name": "Term 1", "code": "T1",
            "startDate": "2026-01-10", "endDate": "2026-04-10", "isActive": true } ]
        """;

    private static string StreamsJson => $$"""
        [ { "id": "{{StreamId}}", "name": "8A", "code": "8A", "parentCodedValueId": null, "isActive": true } ]
        """;

    /// <summary>
    /// The §6 union: two subjects that REQUIRE an enrolment (one present, one missing) and one that is
    /// implied by the grade. Exactly the case the two bands exist to separate.
    /// </summary>
    private static string SubjectsJson => $$"""
        [
          { "topicId": "{{TopicMaths}}",   "name": "Mathematics", "code": "MATH",
            "assignmentId": "{{Guid.NewGuid()}}", "requiresEnrollment": true,  "isEnrolled": true  },
          { "topicId": "{{TopicEthics}}",  "name": "Ethics",      "code": "ETH",
            "assignmentId": "{{Guid.NewGuid()}}", "requiresEnrollment": true,  "isEnrolled": false },
          { "topicId": "{{TopicScience}}", "name": "Natural Sciences", "code": "NS",
            "assignmentId": "{{Guid.NewGuid()}}", "requiresEnrollment": false, "isEnrolled": false }
        ]
        """;

    private static string GuardiansJson => $$"""
        [
          { "guardianId": "{{GuardianId}}", "firstName": "Thandi", "lastName": "Dlamini",
            "displayName": "Thandi Dlamini", "relationshipCodedValueId": null,
            "role": 0, "isEmergencyContact": true,
            "contacts": [ { "channel": 1, "value": "0821234567", "countryCode": "+27" } ],
            "totalContactCount": 5 }
        ]
        """;

    private static string ContactsJson => $$"""
        [
          { "id": "{{Guid.NewGuid()}}", "ownerType": 0, "ownerId": "{{StudentId}}",
            "channel": 0, "value": "jordan.d@example.org", "label": "School account",
            "isVerified": true, "isDeleted": false, "displayOrder": 0,
            "createdAt": "2026-01-12T08:30:00+00:00", "updatedAt": "2026-01-12T08:30:00+00:00",
            "countryCode": null }
        ]
        """;

    private static string ActivityGroupsJson => $$"""
        [
          { "id": "{{GroupId}}", "name": "Chess Club", "description": null, "category": "Academic",
            "capacity": 30, "isActive": true, "span": "Yearly",
            "enrollmentStartDate": null, "enrollmentEndDate": null, "autoRenewDefault": false,
            "eligibleGradeIds": [], "activeMemberCount": 12,
            "createdAt": "2026-01-01T00:00:00+00:00", "updatedAt": "2026-01-01T00:00:00+00:00" }
        ]
        """;

    /// <summary>
    /// The six assignment counts (issue #444, layer L2b). Chosen so the DERIVED funnel is auditable and its
    /// four segments all differ: not sent 3, awaiting submission 9, awaiting marking 5, marked 7 — which sum to
    /// Assigned 24. <c>Assigned</c> and <c>SentOut</c> also differ, so the review axis's "of the 21 sent" cannot
    /// pass as "of 24" (criterion C4).
    /// </summary>
    private const string AssignmentStatsJson = """
        { "assigned": 24, "sentOut": 21, "submitted": 12, "marked": 7,
          "reviewRequired": 12, "reviewed": 10 }
        """;

    /// <summary>No assignments at all — tiles at 0 and NO bars (Q154).</summary>
    private const string ZeroAssignmentStatsJson = """
        { "assigned": 0, "sentOut": 0, "submitted": 0, "marked": 0, "reviewRequired": 0, "reviewed": 0 }
        """;

    /// <summary>A payload that contradicts itself: <c>Marked 25 &gt; Submitted 18</c>. The panel must say
    /// unavailable rather than draw a negative "awaiting marking" segment (Q155 — never clamped).</summary>
    private const string InconsistentAssignmentStatsJson = """
        { "assigned": 24, "sentOut": 21, "submitted": 18, "marked": 25, "reviewRequired": 12, "reviewed": 10 }
        """;

    // ── The harness ─────────────────────────────────────────────────────────

    private sealed class ScriptedHandler(
        string assignmentStatsJson = AssignmentStatsJson,
        HttpStatusCode assignmentStatsStatus = HttpStatusCode.OK) : HttpMessageHandler
    {
        public readonly List<string> Calls = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.PathAndQuery;
            Calls.Add($"{request.Method.Method} {url}");

            // The stats read is scripted apart from the list-shaped reads below: it is the one hop whose
            // FAILURE the panel has to survive, so a test must be able to fail it and nothing else.
            if (url.StartsWith($"/students/{StudentId}/assignment-stats", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(assignmentStatsStatus)
                {
                    Content = new StringContent(assignmentStatsJson, Encoding.UTF8, "application/json"),
                });
            }

            var body = url switch
            {
                var u when u.StartsWith($"/students/{StudentId}/subjects", StringComparison.Ordinal) => SubjectsJson,
                var u when u.StartsWith($"/students/{StudentId}/guardians", StringComparison.Ordinal) => GuardiansJson,
                var u when u.StartsWith($"/students/{StudentId}/activity-groups", StringComparison.Ordinal) => ActivityGroupsJson,
                var u when u.StartsWith($"/students/enrollments/by-student/{StudentId}", StringComparison.Ordinal) => EnrollmentsJson,
                var u when u.StartsWith("/contacts/audit", StringComparison.Ordinal) => "[]",
                var u when u.StartsWith("/contacts?", StringComparison.Ordinal) => ContactsJson,
                var u when u.StartsWith("/api/coded-values/by-parent", StringComparison.Ordinal) => StreamsJson,
                var u when u.StartsWith("/students/grade-levels", StringComparison.Ordinal) => GradeLevelsJson,
                var u when u.StartsWith("/students/periods", StringComparison.Ordinal) => PeriodsJson,
                // Anything else (a relationship / salutation lookup, a read this page gains later) answers
                // an empty list: the fixture must not fail because a non-asserted read exists.
                var u when u.StartsWith("/api/coded-values/", StringComparison.Ordinal) => "[]",
                var u when u.Equals($"/students/{StudentId}", StringComparison.Ordinal) => StudentJson,
                _ => "[]",
            };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class MutableAuthenticationStateProvider : AuthenticationStateProvider
    {
        private ClaimsPrincipal _user = new();

        public ClaimsPrincipal User
        {
            set
            {
                _user = value;
                NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
            }
        }

        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(_user));
    }

    /// <summary>Registers the page's collaborators against the scripted API, then renders the page. The
    /// assignment-stats answer is a parameter because three of its states are their own assertions: loaded,
    /// zero, and failed.</summary>
    private (IRenderedComponent<Detail> Cut, ScriptedHandler Handler) RenderPage(
        string assignmentStatsJson = AssignmentStatsJson,
        HttpStatusCode assignmentStatsStatus = HttpStatusCode.OK)
    {
        var claims = new[]
        {
            new Claim("tenant_id", Guid.NewGuid().ToString()),
            new Claim("tenant_name", "Hydeson"),
        };
        var auth = new MutableAuthenticationStateProvider
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestScheme")),
        };

        var handler = new ScriptedHandler(assignmentStatsJson, assignmentStatsStatus);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        var codedValues = new CodedValuesApiClient(http);
        var api = new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, codedValues);

        Services.AddSingleton<AuthenticationStateProvider>(auth);
        Services.AddSingleton(codedValues);
        Services.AddSingleton(api);
        Services.AddSingleton<IContactsClient>(api);
        Services.AddSingleton(new VisibleTenantService(auth, NullLogger<VisibleTenantService>.Instance));

        var cut = Render<Detail>(p => p.Add(x => x.Id, StudentId));
        return (cut, handler);
    }

    // ── What a reader sees ──────────────────────────────────────────────────

    [TestMethod]
    public void The_Page_Renders_The_Shared_Skeleton_With_Every_Block_And_The_Identity_Header()
    {
        var (cut, handler) = RenderPage();
        cut.WaitForAssertion(() => cut.FindAll("#student-view-details-title").Should().ContainSingle());

        // The shell: a crumb trail whose last entry is the page, and one action header.
        cut.Find("#authoring-breadcrumb").TextContent.Should().Contain("Students");
        cut.Find(".authoring-actionbar").TextContent.Should().Contain("Jordan Dlamini");

        // The identity header's monogram is decorative — the NAME carries the identity.
        var monogram = cut.Find(".authoring-heading .student-monogram");
        monogram.GetAttribute("aria-hidden").Should().Be("true");
        monogram.TextContent.Trim().Should().Be("JD");

        // Every block, in one page — no tabs, and the rail comes last.
        cut.FindAll(".region-grid__main > *").Count.Should().Be(4, "Details, Enrollment, Contacts, Guardians");
        cut.FindAll(".region-grid__rail > *").Count.Should().Be(4,
            "Assignments (first — Q124), Activity groups, Subjects, Contact history");
        cut.FindAll("fluent-tabs").Should().BeEmpty("the view is a single page (StudentDetailSectionsTests pins it)");

        // The reads the blocks depend on actually happened.
        handler.Calls.Should().Contain($"GET /students/{StudentId}");
        handler.Calls.Should().Contain($"GET /students/enrollments/by-student/{StudentId}");
        handler.Calls.Should().Contain($"GET /students/{StudentId}/guardians");
        handler.Calls.Should().Contain($"GET /contacts?ownerType=Student&ownerId={StudentId}");
        handler.Calls.Should().Contain($"GET /students/{StudentId}/subjects");
        handler.Calls.Should().Contain($"GET /students/{StudentId}/activity-groups");
        handler.Calls.Should().Contain($"GET /students/{StudentId}/assignment-stats",
            "the Assignments panel's own read must actually have happened — scripted is not the same as called");
    }

    [TestMethod]
    public void The_Status_Pill_States_The_Enrollment_In_Words_Not_Only_In_Colour()
    {
        var (cut, _) = RenderPage();
        // Scoped to the action bar: the guardian cards reuse .student-monogram--sm, so a bare
        // ".student-monogram" selector matches the page monogram AND every guardian monogram.
        cut.WaitForAssertion(() => cut.FindAll(".authoring-heading .student-monogram").Should().ContainSingle());

        var pill = cut.Find(".authoring-title-group .status-pill");
        pill.ClassName.Should().Contain("status-pill--success");
        pill.TextContent.Should().Contain("Enrolled", "the pill's meaning is its LABEL — colour reinforces it");

        // The dot is decoration and must not be announced, or every status reads "Enrolled bullet".
        var dot = cut.Find(".authoring-title-group .status-pill__dot");
        dot.GetAttribute("aria-hidden").Should().Be("true");
    }

    [TestMethod]
    public void The_Enrollment_Block_Shows_The_Current_Placement_And_The_History()
    {
        var (cut, _) = RenderPage();
        cut.WaitForAssertion(() => cut.FindAll("#student-view-placement").Should().ContainSingle());

        var placement = cut.Find("#student-view-placement");
        placement.TextContent.Should().Contain("Year 8 (8A)", "the current grade resolves through the stream name");
        placement.TextContent.Should().Contain("Term 1");
        placement.TextContent.Should().Contain("1 day in this placement",
            "the count is pluralised — the first live render shipped \"1 days\"");
        cut.Find(".tl__item--current").TextContent.Should().Contain("1 day",
            "the timeline meta uses the same pluralisation");
        cut.Find(".tl__item--current").TextContent.Should().NotContain("1 days");

        // Both enrollments are listed; only the active one carries the Current pill.
        cut.FindAll(".tl__item").Count.Should().Be(2);
        cut.FindAll(".tl__item--current").Count.Should().Be(1);
        cut.FindAll("#student-view-enrollment .status-pill").Select(p => p.TextContent)
            .Should().Contain(t => t.Contains("Current"));
    }

    [TestMethod]
    public void The_Subjects_Block_Separates_Needs_Enrolment_From_Assumed()
    {
        var (cut, _) = RenderPage();
        cut.WaitForAssertion(() => cut.FindAll("#student-view-subjects-required").Should().ContainSingle());

        // The required band: the enrolled subject is a plain chip, the missing one is STRUCK with a reason.
        var required = cut.Find("#student-view-subjects-required");
        required.TextContent.Should().Contain("Mathematics");
        required.TextContent.Should().Contain("Ethics");
        required.TextContent.Should().Contain("choose it on the Edit page",
            "a gap must say where it is fixed (FR-ER-24 makes the Edit form the enrolment surface)");

        var struck = cut.FindAll("#student-view-subjects-required .rail-chip")
            .Single(c => c.GetAttribute("aria-disabled") == "true");
        struck.TextContent.Should().Contain("Ethics");

        // The assumed band holds only the implied subject, and implies nothing to act on.
        cut.Find("#student-view-subjects-assumed").TextContent.Should().Contain("Natural Sciences");
        cut.FindAll("#student-view-subjects-assumed .rail-chip[aria-disabled='true']")
            .Should().BeEmpty("an implied enrolment is a fact, not a gap");
    }

    [TestMethod]
    public void The_Contacts_Block_Lists_The_Students_Own_Contacts_And_Guardian_Cards_Keep_Theirs()
    {
        var (cut, _) = RenderPage();
        cut.WaitForAssertion(() => cut.FindAll("#student-view-contacts").Should().ContainSingle());

        var contacts = cut.Find("#student-view-contacts");
        contacts.TextContent.Should().Contain("jordan.d@example.org", "the student's OWN contact is listed");
        contacts.TextContent.Should().Contain("School account");
        contacts.TextContent.Should().NotContain("0821234567",
            "a guardian's number must never appear in the student's own contact list");

        // The guardian still shows their own, plus the 'View all' affordance for the two it caps out.
        var guardian = cut.Find(".guardian-card");
        guardian.TextContent.Should().Contain("Thandi Dlamini");
        guardian.TextContent.Should().Contain("0821234567");
        guardian.TextContent.Should().Contain("View all 5 contacts",
            "TotalContactCount 5 > 3 shows the overflow affordance");
    }

    [TestMethod]
    public void The_Activity_Groups_Block_Shows_The_Optional_Category_Only_When_It_Is_Set()
    {
        var (cut, _) = RenderPage();
        cut.WaitForAssertion(() => cut.FindAll("#student-view-groups").Should().ContainSingle());

        var groups = cut.Find("#student-view-groups");
        groups.TextContent.Should().Contain("Chess Club");
        groups.TextContent.Should().Contain("Academic", "Category is optional free text and renders when set");
    }

    // ── Assignments panel (issue #444, layer L2b) ────────────────────────

    [TestMethod]
    public void The_Assignments_Block_Is_The_First_Rail_Panel_And_Shows_The_Four_Progress_Tiles()
    {
        var (cut, _) = RenderPage();
        cut.WaitForAssertion(() => cut.FindAll("#student-view-assignments-title").Should().ContainSingle());

        // First in the rail (Q124): asserted on the rendered DOM, not on the source order.
        cut.FindAll(".region-grid__rail > *")[0].Id.Should().Be("student-view-assignments",
            "the panel is what a form tutor opens this page to check, so it leads the rail");
        cut.Find("#student-view-assignments-title").TextContent.Trim().Should().Be("Assignments");

        // The header count IS Assigned — one number, the panel's own denominator.
        cut.Find("#student-view-assignments .panel-section__count").TextContent.Trim().Should().Be("24");

        var tiles = cut.FindAll("#student-view-assignments .stat")
            .Select(tile => (
                Value: tile.QuerySelector(".stat__value")!.TextContent.Trim(),
                Label: tile.QuerySelector(".stat__label")!.TextContent.Trim()))
            .ToArray();

        tiles.Should().BeEquivalentTo(
            new[] { ("24", "Assigned"), ("21", "Sent out"), ("12", "Submitted"), ("7", "Marked") },
            options => options.WithStrictOrdering(),
            "the four tiles are the DTO's four progress counts, verbatim");
    }

    [TestMethod]
    public void The_Funnel_Bar_Totals_Assigned_And_The_Review_Bar_Totals_ReviewRequired()
    {
        var (cut, _) = RenderPage();
        cut.WaitForAssertion(() => cut.FindAll("#student-view-assignments .stat-bar").Should().HaveCount(2));

        // One bar per axis — never one bar carrying both, which is the defect the mock's second bar fixed.
        var bars = cut.FindAll("#student-view-assignments .stat-bar");

        // Axis 1: 3 + 9 + 5 + 7 = 24 = Assigned. The label states every derived segment, so a wrong
        // difference cannot hide behind a plausible-looking width.
        bars[0].GetAttribute("aria-label").Should().Be(
            "24 assigned: 3 not sent, 9 awaiting submission, 5 awaiting marking, 7 marked",
            "the four derived segments sum to Assigned");

        // Axis 2: its OWN denominator — 12 required on 21 sent, never "of 24".
        bars[1].GetAttribute("aria-label").Should().Be(
            "Guardian review: 10 reviewed, 2 awaiting review, required on 12 of the 21 sent");

        var foot = cut.Find("#student-view-assignments-review-foot").TextContent;
        foot.Should().Contain("required on 12 of the 21 sent",
            "the review axis names its own denominator: a draft cannot be gated for review");
        foot.Should().NotContain("of 24", "'of Assigned' is the wrong denominator for this bar");

        // Every segment is named in text as well as coloured.
        var legend = cut.FindAll("#student-view-assignments .stat-legend__item")
            .Select(item => item.TextContent.Trim())
            .ToArray();

        legend.Should().BeEquivalentTo(
            new[] { "3 not sent", "9 awaiting submission", "5 awaiting marking", "7 marked" },
            options => options.WithStrictOrdering());
    }

    [TestMethod]
    public void Zero_Assigned_Renders_The_Tiles_But_No_Bars()
    {
        var (cut, _) = RenderPage(ZeroAssignmentStatsJson);
        cut.WaitForAssertion(() => cut.FindAll("#student-view-assignments-empty").Should().ContainSingle());

        cut.Find("#student-view-assignments .panel-section__count").TextContent.Trim().Should().Be("0");
        cut.FindAll("#student-view-assignments .stat .stat__value")
            .Select(value => value.TextContent.Trim())
            .Should().AllBe("0", "a zero count is a fact, so the tiles still render");

        cut.FindAll("#student-view-assignments .stat-bar").Should().BeEmpty(
            "a bar at 0% implies '0% of something' (Q154)");
        cut.Find("#student-view-assignments-empty").TextContent.Trim()
            .Should().Be("No assignments for this student yet.");
        cut.FindAll("#student-view-assignments .stat-legend").Should().BeEmpty("no segments, so nothing to name");
    }

    [TestMethod]
    public void A_Failed_Hop_Renders_No_Numbers_And_The_Rest_Of_The_Page_Still_Renders()
    {
        var (cut, handler) = RenderPage(assignmentStatsStatus: HttpStatusCode.InternalServerError);
        cut.WaitForAssertion(() => cut.FindAll("#student-view-assignments-unavailable").Should().ContainSingle());

        // The read was attempted — otherwise this test would pass on a panel that never loaded.
        handler.Calls.Should().Contain($"GET /students/{StudentId}/assignment-stats");

        var panel = cut.Find("#student-view-assignments");
        panel.TextContent.Trim().Should().Contain("Assignment stats are unavailable right now.");

        // NO numbers at all, not even the header count: zeros are a claim, unavailable is a fact (Q153).
        panel.QuerySelectorAll(".stat").Should().BeEmpty();
        panel.QuerySelectorAll(".stat-bar").Should().BeEmpty();
        panel.QuerySelectorAll(".panel-section__count").Should().BeEmpty();

        // …and the failure costs one panel, not the page (the load is sequenced after _student is set).
        cut.FindAll("#student-view-details-title").Should().ContainSingle();
        cut.Find("#student-view-groups").TextContent.Should().Contain("Chess Club");
        cut.Find("#student-view-placement").TextContent.Should().Contain("Year 8 (8A)");
    }

    [TestMethod]
    public void An_Inconsistent_Payload_Renders_Unavailable_Rather_Than_A_Negative_Segment()
    {
        // Marked (25) > Submitted (18): the funnel would have to draw a negative "awaiting marking".
        // The panel says the truth is unavailable instead of clamping a chart (Q155).
        var (cut, _) = RenderPage(InconsistentAssignmentStatsJson);
        cut.WaitForAssertion(() => cut.FindAll("#student-view-assignments-unavailable").Should().ContainSingle());

        var panel = cut.Find("#student-view-assignments");
        panel.QuerySelectorAll(".stat").Should().BeEmpty();
        panel.QuerySelectorAll(".stat-bar").Should().BeEmpty();
        panel.QuerySelectorAll(".panel-section__count").Should().BeEmpty(
            "the header count is a number too, and this panel presents no numbers while the data contradicts itself");
    }

    [TestMethod]
    public void An_Empty_Tenant_Renders_The_Fallback_And_Asks_The_Api_Nothing()
    {
        var auth = new MutableAuthenticationStateProvider
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim("tenant_id", Guid.Empty.ToString()),
                    new Claim("tenant_name", "System"),
                ], "TestScheme")),
        };

        var handler = new ScriptedHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        var codedValues = new CodedValuesApiClient(http);
        var api = new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, codedValues);

        Services.AddSingleton<AuthenticationStateProvider>(auth);
        Services.AddSingleton(codedValues);
        Services.AddSingleton(api);
        Services.AddSingleton<IContactsClient>(api);
        Services.AddSingleton(new VisibleTenantService(auth, NullLogger<VisibleTenantService>.Instance));

        var cut = Render<Detail>(p => p.Add(x => x.Id, StudentId));

        // The gate's fallback explains itself instead of drawing an empty page, and no student data is
        // fetched for a tenant the user is not in.
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("no tenant assigned"));
        cut.FindAll(".region-grid").Should().BeEmpty();
        handler.Calls.Should().BeEmpty("a gated page must not read tenant-scoped data it will not render");
    }
}
