using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using AngleSharp.Dom;
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
using StudentDto = SchoolCollab.Students.Application.Services.StudentDto;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// The two subject bands on the student edit rail (subject-enrollment-requirement-flag.md round B,
/// owner Q42/Q45): "Needs explicit enrolment" (<c>RailBandVariant.Required</c>, every <c>true</c>
/// subject) and "Assumed" (<c>RailBandVariant.Assumed</c>, the implied set).
///
/// <para>What is pinned here is the behaviour the round claims, not the markup that happens to
/// implement it:</para>
/// <list type="bullet">
///   <item>both chip states exist in one band — an enrolled subject is a FACT, an un-enrolled one is
///         STRUCK THROUGH and says so in words, so the strike is never the only signal;</item>
///   <item>the struck chip's name is a real <c>&lt;button type="button"&gt;</c> that is not
///         <c>aria-disabled</c> — the one control that fixes the gap must be reachable by keyboard;</item>
///   <item>clicking it writes exactly ONE enrolment, against the active academic YEAR (not the
///         student's own enrolment period, which may be a sub-period), and the chip becomes enrolled
///         in place;</item>
///   <item>a failed write leaves the chip exactly as it was and renders a plain sentence — never the
///         exception's own text (the #416 seam).</item>
/// </list>
/// </summary>
[TestClass]
public class StudentSubjectEnrollmentBandTests : BunitContext
{
    private static readonly Guid StudentId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid GradeLevelId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid EnrollmentPeriodId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid ActiveYearId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid MathTopicId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ArtTopicId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid MusicTopicId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid MathAssignmentId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid ArtAssignmentId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid MusicAssignmentId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");

    private static readonly JsonSerializerOptions ApiJson = new(JsonSerializerDefaults.Web);

    public StudentSubjectEnrollmentBandTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    /// <summary>
    /// Scripted students API: url-PREFIX answers, longest prefix first — so
    /// <c>/students/{id}/subjects</c> can never be swallowed by the shorter <c>/students/{id}</c>
    /// entry. Every unmapped route 404s, and every call is logged.
    /// </summary>
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public readonly List<(string Method, string Url, string? Body)> Calls = [];
        private readonly List<(string Method, string Url, HttpStatusCode Status, string Body)> _responses = [];

        public ScriptedHandler Map(string method, string url, HttpStatusCode status, string body)
        {
            _responses.Add((method.ToUpperInvariant(), url, status, body));
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.PathAndQuery;
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request.Method.Method.ToUpperInvariant(), url, body));

            var hit = _responses
                .Where(r => string.Equals(r.Method, request.Method.Method, StringComparison.OrdinalIgnoreCase)
                            && url.StartsWith(r.Url, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => r.Url.Length)
                .Cast<(string Method, string Url, HttpStatusCode Status, string Body)?>()
                .FirstOrDefault();

            if (hit is { } found)
            {
                return new HttpResponseMessage(found.Status)
                {
                    Content = new StringContent(found.Body, Encoding.UTF8, "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent(
                    $"Unexpected URL: {request.Method.Method} {url}", Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class MutableAuth : AuthenticationStateProvider
    {
        private readonly ClaimsPrincipal _user = new(new ClaimsIdentity(
            [new Claim("tenant_id", Guid.NewGuid().ToString()), new Claim("tenant_name", "Hydeson")],
            "TestScheme"));

        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(_user));
    }

    /// <summary>The page's backend, scripted for a grade with three subjects: Mathematics (needs an
    /// explicit enrolment and the student does NOT hold it), Art (needs one and the student holds it),
    /// Music (implied — the student gets it without choosing).</summary>
    private ScriptedHandler RegisterBackend(bool enrolWriteFails = false)
    {
        var auth = new MutableAuth();
        var handler = new ScriptedHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };

        Services.AddSingleton<AuthenticationStateProvider>(auth);
        var codedValues = new CodedValuesApiClient(http);
        var students = new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, codedValues);
        Services.AddSingleton(codedValues);
        Services.AddSingleton(students);
        Services.AddSingleton<IContactsClient>(students);
        Services.AddSingleton(new VisibleTenantService(auth, NullLogger<VisibleTenantService>.Instance));
        Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        handler.Map("GET", $"/students/{StudentId}/subjects", HttpStatusCode.OK,
            JsonSerializer.Serialize(Subjects(), ApiJson));
        handler.Map("GET", $"/students/{StudentId}/guardians", HttpStatusCode.OK, "[]");
        handler.Map("GET", $"/students/enrollments/by-student/{StudentId}", HttpStatusCode.OK,
            JsonSerializer.Serialize(new[] { ActiveEnrollment() }, ApiJson));
        handler.Map("GET", "/students/periods/active-academic-year", HttpStatusCode.OK,
            JsonSerializer.Serialize(ActiveYear(), ApiJson));
        handler.Map("GET", $"/students/{StudentId}", HttpStatusCode.OK,
            JsonSerializer.Serialize(Student(), ApiJson));

        // The one write the subject band performs. A prefix match, because the request carries its
        // ids in the body rather than the url.
        handler.Map("POST", "/students/student-topics",
            enrolWriteFails ? HttpStatusCode.InternalServerError : HttpStatusCode.OK,
            $"{{\"id\":\"{Guid.NewGuid()}\"}}");

        // Every other block's lookup (coded values, contacts) reads an empty list.
        handler.Map("GET", "/", HttpStatusCode.OK, "[]");
        return handler;
    }

    private static StudentDto Student() => new(
        Id: StudentId,
        StudentNumber: "STU-0001",
        FirstName: "Ada",
        LastName: "Lovelace",
        DateOfBirth: null,
        GenderCodedValueId: null,
        IsDeleted: false,
        CreatedAt: DateTimeOffset.UnixEpoch,
        UpdatedAt: DateTimeOffset.UnixEpoch);

    /// <summary>One ACTIVE enrolment — without it the Enrollment panel renders its empty state and no
    /// band exists at all. Its period is deliberately NOT the active academic year: the enrol write
    /// must use the year, never this.</summary>
    private static StudentEnrollmentDto ActiveEnrollment() => new(
        Id: Guid.Parse("88888888-8888-8888-8888-888888888888"),
        StudentId: StudentId,
        PeriodId: EnrollmentPeriodId,
        GradeLevelId: GradeLevelId,
        StreamCodedValueId: null,
        EnrolledOn: new DateOnly(2025, 9, 1),
        ExitDate: null,
        Status: "Active",
        CreatedAt: DateTimeOffset.UnixEpoch,
        UpdatedAt: DateTimeOffset.UnixEpoch);

    private static PeriodDto ActiveYear() => new(
        Id: ActiveYearId,
        Name: "2026/2027",
        StartDate: new DateOnly(2026, 9, 1),
        EndDate: new DateOnly(2027, 8, 31),
        Status: "Active",
        ParentPeriodId: null,
        NextPeriodId: null,
        Division: "None",
        ActivationToleranceDays: null,
        CreatedAt: DateTimeOffset.UnixEpoch,
        UpdatedAt: DateTimeOffset.UnixEpoch);

    /// <summary>The §6 union as the API serves it: two explicit-enrolment subjects (one enrolled, one
    /// not) and one implied subject.</summary>
    private static object[] Subjects() =>
    [
        new { topicId = MathTopicId, name = "Mathematics", code = "MATH", assignmentId = MathAssignmentId,
              requiresEnrollment = true, isEnrolled = false },
        new { topicId = ArtTopicId, name = "Art", code = "ART", assignmentId = ArtAssignmentId,
              requiresEnrollment = true, isEnrolled = true },
        new { topicId = MusicTopicId, name = "Music", code = "MUS", assignmentId = MusicAssignmentId,
              requiresEnrollment = false, isEnrolled = true },
    ];

    private (IRenderedComponent<Edit> Cut, ScriptedHandler Handler) RenderEdit(bool enrolWriteFails = false)
    {
        var handler = RegisterBackend(enrolWriteFails);
        var cut = Render<Edit>(parameters => parameters.Add(p => p.Id, StudentId));
        // The bands render only after the page's async loads complete — waiting on one of them is
        // waiting for the page, so the assertions never read a spinner.
        cut.WaitForAssertion(
            () => cut.FindAll("#student-edit-subjects-required").Should().ContainSingle(
                "the 'Needs explicit enrolment' band renders once the union has loaded"),
            timeout: TimeSpan.FromSeconds(5));
        return (cut, handler);
    }

    private static string[] BandLabels(IRenderedComponent<Edit> cut) =>
        [.. cut.Find("#student-edit-enrollment").QuerySelectorAll(".rail-band__label")
            .Select(l => l.TextContent.Trim())];

    private static IElement[] RequiredChips(IRenderedComponent<Edit> cut) =>
        [.. cut.Find("#student-edit-subjects-required").QuerySelectorAll(".rail-chip")];

    private static bool IsStruck(IElement chip) => chip.ClassList.Contains("rail-chip--actionable");

    // ── Both chip states in the band (acceptance criteria 1 and 2) ──────────────────────────

    [TestMethod]
    public void TheRailRendersTheFourBandsInTheSettledOrder()
    {
        var (cut, _) = RenderEdit();

        var labels = BandLabels(cut);
        labels.Should().HaveCount(4, "four bands, one kind of fact each (owner Q42)");
        labels[0].Should().StartWith("Placement");
        labels[1].Should().StartWith("Activity groups");
        labels[2].Should().StartWith("Needs explicit enrolment",
            "the enrolment kinds read after what the enrolment IS");
        labels[3].Should().StartWith("Assumed");

        cut.Find("#student-edit-subjects-required").ClassName.Should().Contain("rail-band--required",
            "the band uses the EXISTING Required variant — round B adds no member to the enum");
        cut.Find("#student-edit-subjects-assumed").ClassName.Should().Contain("rail-band--assumed");
    }

    [TestMethod]
    public void TheStruckChipAndTheEnrolledChip_AreTheTwoStatesOfOneBand()
    {
        var (cut, _) = RenderEdit();

        var chips = RequiredChips(cut);
        chips.Should().HaveCount(2, "the band draws every `true` subject, enrolled or not");

        var struck = chips.Where(IsStruck).ToArray();
        struck.Should().HaveCount(1, "exactly one of the two subjects needs an explicit enrolment action");
        struck[0].TextContent.Should().Contain("Mathematics");
        struck[0].TextContent.Should().Contain("not chosen",
            "the state is WORDS as well as a line — a strike-through and a colour are not accessible signals");

        var fact = chips.Where(c => !IsStruck(c)).ToArray();
        fact.Should().HaveCount(1, "the enrolled subject is a fact, not a to-do");
        fact[0].TextContent.Should().Contain("Art");
        fact[0].QuerySelectorAll("button").Should().BeEmpty("a fact does nothing");

        var assumed = cut.Find("#student-edit-subjects-assumed").QuerySelectorAll(".rail-chip").ToArray();
        assumed.Should().HaveCount(1);
        assumed[0].TextContent.Should().Contain("Music");
        IsStruck(assumed[0]).Should().BeFalse(
            "an implied subject is the student's without choosing (FR-ER-1) — there is nothing to act on");
    }

    [TestMethod]
    public void TheStruckChip_IsARealButton_ThatIsNotAriaDisabled()
    {
        var (cut, _) = RenderEdit();

        var struck = RequiredChips(cut).Single(IsStruck);
        struck.GetAttribute("aria-disabled").Should().BeNull(
            "aria-disabled belongs to the picker's BLOCKED rows; on the one control that fixes the gap it "
            + "would announce the fix as unavailable");

        var button = struck.QuerySelector("button");
        button.Should().NotBeNull("the chip's name is a real button, not a span");
        button!.GetAttribute("type").Should().Be("button",
            "typed so it can never submit a surrounding form");
        button.GetAttribute("aria-label").Should().Be("Mathematics — not chosen",
            "the accessible name carries the subject AND its state");
        button.HasAttribute("disabled").Should().BeFalse();
        button.HasAttribute("tabindex").Should().BeFalse(
            "no negative tabindex — a native button is in the tab order, which is what makes the gap "
            + "fixable from the keyboard (acceptance criterion 3)");
    }

    // ── The enrol action (acceptance criteria 4 and 5) ──────────────────────────────────────

    [TestMethod]
    public void ClickingTheStruckChip_EnrolsInTheActiveAcademicYear_AndReplacesTheChip()
    {
        var (cut, handler) = RenderEdit();

        cut.Find("#student-edit-subjects-required button.rail-chip__activate").Click();

        cut.WaitForAssertion(() =>
        {
            var writes = handler.Calls
                .Where(c => c.Method == "POST" && c.Url == "/students/student-topics").ToArray();
            writes.Should().HaveCount(1, "one click, exactly one enrolment write");

            using var request = JsonDocument.Parse(writes[0].Body!);
            var root = request.RootElement;
            root.GetProperty("studentId").GetGuid().Should().Be(StudentId);
            root.GetProperty("topicId").GetGuid().Should().Be(MathTopicId);
            root.GetProperty("periodId").GetGuid().Should().Be(
                ActiveYearId,
                "the row is year-keyed (FR-ER-3), so the write carries the ACTIVE ACADEMIC YEAR — the "
                + "student's own enrolment period may be a sub-period, which the handler rejects");
            root.GetProperty("isOverride").GetBoolean().Should().BeFalse();
            root.GetProperty("sourceType").GetString().Should().Be("IndividualAssignment");

            RequiredChips(cut).Where(IsStruck).Should().BeEmpty(
                "the chip becomes the enrolled state IN PLACE — no reload, no second read");
        }, timeout: TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public void ClickingTheStruckChip_WhenTheWriteFails_LeavesTheChipAndRendersAPlainSentence()
    {
        var (cut, _) = RenderEdit(enrolWriteFails: true);

        cut.Find("#student-edit-subjects-required button.rail-chip__activate").Click();

        cut.WaitForAssertion(() =>
        {
            var panel = cut.Find("#student-edit-enrollment").TextContent;
            panel.Should().Contain("'Mathematics' could not be added to this student. Try again.",
                "a failure is the site's own sentence (FriendlyError.Describe)");
            panel.Should().NotContain("500", "no status code reaches the message bar");
            panel.Should().NotContain("InternalServerError");

            RequiredChips(cut).Where(IsStruck).Should().HaveCount(
                1, "the failed write changes nothing about the chip");
            RequiredChips(cut).Single(IsStruck).QuerySelector("button").Should().NotBeNull(
                "the action is still available — a failure must not remove the only way to fix the gap");
        }, timeout: TimeSpan.FromSeconds(5));
    }
}
