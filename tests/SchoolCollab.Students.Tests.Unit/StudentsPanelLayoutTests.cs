using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Students.Application.Components.Contacts;
using SchoolCollab.Students.Application.Components.Students;
using SchoolCollab.Students.Core.Domain;
using SchoolCollab.Students.Application.Components.Pages.Students;
using SchoolCollab.Students.Application.Services;
using SchoolCollab.Students.Core.Contracts;
using StudentDto = SchoolCollab.Students.Application.Services.StudentDto;

namespace SchoolCollab.Students.Tests.Unit;

/// <summary>
/// The student edit page on the shared create / edit skeleton
/// (<c>documents/solution/panel-layout-pattern.md</c>, owner 2026-10-24).
/// <para>
/// Asserted through the rendered page, external behaviour only: which region each block
/// belongs to, that the page action header owns the save pair, and that only the
/// collapsible block offers the affordance. The retargeted source-level tests for the same
/// migration live in <c>SchoolCollab.Admin.Tests.Unit/EditSidebarLayoutTests.cs</c>; these
/// two read the DOM, because "the panel is inside the rail" is a fact about the rendered
/// tree, not about the markup text.
/// </para>
/// </summary>
[TestClass]
public class StudentsPanelLayoutTests : BunitContext
{
    private static readonly Guid StudentId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly JsonSerializerOptions ApiJson = new(JsonSerializerDefaults.Web);

    public StudentsPanelLayoutTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    /// <summary>
    /// Scripted backend: the student, no enrollments and no guardians, so the page renders
    /// its panels in their EMPTY state — which is what makes "the block is in the rail"
    /// assertable without seeding rows, and proves the block renders even when empty.
    /// </summary>
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public readonly List<(string Method, string Url)> Calls = [];
        private readonly Dictionary<(string Method, string Url), (HttpStatusCode Status, string Body)> _responses = [];

        public ScriptedHandler Map(string method, string url, HttpStatusCode status, string body)
        {
            _responses[(method.ToUpperInvariant(), url)] = (status, body);
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.PathAndQuery;
            Calls.Add((request.Method.Method, url));

            foreach (var ((method, prefix), response) in _responses)
            {
                if (!string.Equals(method, request.Method.Method, StringComparison.OrdinalIgnoreCase)) continue;
                if (!url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                return Task.FromResult(new HttpResponseMessage(response.Status)
                {
                    Content = new StringContent(response.Body, Encoding.UTF8, "application/json"),
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent($"Unexpected URL: {request.Method.Method} {url}", Encoding.UTF8, "application/json"),
            });
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

    /// <summary>The page's backend, scripted for an EMPTY student surface (no enrollments, no
    /// guardians, no coded values). Registered in this order: the page's own reads first, then
    /// the catch-all that answers the blocks' lookup reads with an empty list.</summary>
    private ScriptedHandler RegisterBackend(bool withActiveEnrollment = false, bool contactWriteFails = false)
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
        // The page, the contacts block and the coded-value dropdowns each inject a typed logger;
        // the open-generic registration satisfies every one of them without naming each type.
        Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        handler.Map("GET", $"/students/{StudentId}", HttpStatusCode.OK, JsonSerializer.Serialize(Student(), ApiJson));
        handler.Map("GET", $"/students/enrollments/by-student/{StudentId}", HttpStatusCode.OK,
            withActiveEnrollment
                ? JsonSerializer.Serialize(new[] { ActiveEnrollment() }, ApiJson)
                : "[]");
        handler.Map("GET", $"/students/{StudentId}/guardians", HttpStatusCode.OK, "[]");
        // Every lookup the blocks make on mount (coded values, contacts) reads an empty list.
        handler.Map("GET", "/", HttpStatusCode.OK, "[]");

        // ── the create page's writes ────────────────────────────────────────────────────────────────
        // POST /students returns the new id; the page then flushes each drafted contact against it.
        // NOTE the prefix is "/students" WITHOUT a trailing slash — `PostAsJsonAsync("/students", …)` is what
        // the client sends, and a "/students/" prefix never matches, which silently turned the create into a
        // 404 and made the flush unreachable.
        handler.Map("POST", "/students", HttpStatusCode.OK, $"{{\"id\":\"{NewStudentId}\"}}");
        handler.Map("POST", "/contacts", contactWriteFails ? HttpStatusCode.InternalServerError : HttpStatusCode.OK,
            contactWriteFails ? """{"message":"boom"}""" : $"{{\"id\":\"{Guid.NewGuid()}\"}}");
        return handler;
    }

    private static readonly Guid GenderId = Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly Guid NewStudentId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid GradeLevelId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid PeriodId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    /// <summary>One ACTIVE enrollment — the state the Enrollment rail bands render in. Without it the
    /// panel shows its "no active enrollment" empty state and the bands never render.</summary>
    private static StudentEnrollmentDto ActiveEnrollment() => new(
        Id: Guid.Parse("66666666-6666-6666-6666-666666666666"),
        StudentId: StudentId,
        PeriodId: PeriodId,
        GradeLevelId: GradeLevelId,
        StreamCodedValueId: null,
        EnrolledOn: new DateOnly(2025, 9, 1),
        ExitDate: null,
        Status: "Active",
        CreatedAt: DateTimeOffset.UnixEpoch,
        UpdatedAt: DateTimeOffset.UnixEpoch);

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

    private IRenderedComponent<Edit> RenderEdit(bool withActiveEnrollment = false)
    {
        RegisterBackend(withActiveEnrollment);
        var cut = Render<Edit>(parameters => parameters.Add(p => p.Id, StudentId));
        // The rail renders only after the student load completes — waiting on it is waiting for
        // the page's async init, so the assertions below never read the spinner state.
        cut.WaitForAssertion(
            () => cut.FindAll("#student-edit-rail").Should().ContainSingle("the rail renders once the student has loaded"),
            timeout: TimeSpan.FromSeconds(5));
        return cut;
    }

    /// <summary>Renders /students/create — the same scripted backend, no student id needed.</summary>
    /// <summary>
    /// The create page drafts contacts in a BUFFERED editor — there is no owner id to write them against until
    /// the student exists — so it flushes them AFTER the create, one POST per contact. That flush is behavioural
    /// code, so it is pinned here: the pre-flight review (2026-10-26) caught it shipping with no test at all.
    /// </summary>
    [TestMethod]
    public async Task Create_FlushesTheDraftedContacts_AgainstTheNewStudentId()
    {
        var handler = RegisterBackend();
        var cut = Render<Create>();

        cut.WaitForAssertion(() => cut.Find("#student-create-contacts").Should().NotBeNull());

        // Draft two contacts in the buffered editor, then save the student.
        await DraftContactAsync(cut, ContactChannel.Email, "a@example.com", "Mum");
        await DraftContactAsync(cut, ContactChannel.SMS, "+27 82 000 0000", null);
        await SubmitTheCreateFormAsync(cut);

        cut.WaitForAssertion(() =>
        {
            var posts = handler.Calls.Where(c => c.Method == "POST" && c.Url.StartsWith("/contacts", StringComparison.OrdinalIgnoreCase)).ToArray();
            posts.Should().HaveCount(2,
                $"one POST per drafted contact — the buffered editor cannot write them earlier. Calls seen: {string.Join(" | ", handler.Calls.Select(c => $"{c.Method} {c.Url}"))}");

            // Ordering is the observable guarantee: the owner id only exists after the create, so every
            // contact write must come AFTER it. (The id itself travels in the request body; asserting on the
            // URL would be asserting on something the client never puts there.)
            var createIndex = handler.Calls.FindIndex(c => c.Method == "POST" && c.Url == "/students");
            var firstContactIndex = handler.Calls.FindIndex(c => c.Method == "POST" && c.Url.StartsWith("/contacts", StringComparison.OrdinalIgnoreCase));
            createIndex.Should().BeGreaterThanOrEqualTo(0, "the create must fire");
            firstContactIndex.Should().BeGreaterThan(createIndex,
                "a contact can only be flushed once the student has an id");
        }, TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// A failed contact flush must say so in a sentence the user can act on, and must NOT re-create the student:
    /// the student already exists at that point, so retrying the whole form would duplicate them. The detail
    /// belongs in the log, never in the message.
    /// </summary>
    [TestMethod]
    public async Task Create_WhenTheContactFlushFails_ReportsItAndDoesNotCreateASecondStudent()
    {
        var handler = RegisterBackend(contactWriteFails: true);
        var cut = Render<Create>();

        cut.WaitForAssertion(() => cut.Find("#student-create-contacts").Should().NotBeNull());

        await DraftContactAsync(cut, ContactChannel.Email, "a@example.com", null);
        await SubmitTheCreateFormAsync(cut);

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("some contacts could not be saved",
                $"the user needs a sentence, not a stack trace. Calls: {string.Join(" | ", handler.Calls.Select(c => $"{c.Method} {c.Url}"))}");

            handler.Calls.Count(c => c.Method == "POST"
                && c.Url == "/students").Should().Be(1,
                "a contact failure must not trigger a second create — the student already exists");
        }, TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Runs the create page's save path.
    /// <para>
    /// It invokes the <see cref="StudentFormFields"/> <c>OnValidSubmit</c> seam directly rather than replaying
    /// five FluentUI controls (shadow-DOM event chains) plus <c>EditForm</c> validation: validation is the form
    /// component's own covered ground, and what is being pinned here is the PAGE's new behaviour — create the
    /// student, then flush the buffered contacts against the id it returns.
    /// </para>
    /// </summary>
    private static async Task SubmitTheCreateFormAsync(IRenderedComponent<Create> cut)
    {
        var form = cut.FindComponent<StudentFormFields>();

        // The buffered contacts are flushed only after a real owner id exists, so the precondition is part of
        // the contract: the list the page owns is the list the editor mutates.
        form.Instance.Model.Contacts.Should().NotBeEmpty(
            "a flush with nothing drafted would prove nothing — the drafts must reach the page's model");

        await cut.InvokeAsync(() => form.Instance.OnValidSubmit.InvokeAsync());
    }

    /// <summary>
    /// Drafts one contact onto the list the PAGE owns (<see cref="StudentFormModel.Contacts"/>) — the list the
    /// buffered <see cref="ContactsEditor"/> mutates and the page flushes after the create. Appending to the
    /// editor's own <c>Contacts</c> parameter would be the wrong target: it is the editor's working copy, and
    /// without the editor's <c>ContactsChanged</c> event the page's model would never see it.
    /// </summary>
    private static async Task DraftContactAsync(IRenderedComponent<Create> cut, ContactChannel channel, string value, string? label)
    {
        var contacts = cut.FindComponent<StudentFormFields>().Instance.Model.Contacts;
        await cut.InvokeAsync(() => contacts.Add(new ContactModel
        {
            Channel = channel,
            Value = value,
            Label = label,
            Order = contacts.Count,
        }));
    }

    private IRenderedComponent<Create> RenderCreate()
    {
        RegisterBackend();
        return Render<Create>();
    }

    [TestMethod]
    public void EnrollmentPanel_PresentsPlacementAndActivityGroupBands()
    {
        // Arrange / Act — an ACTIVE enrollment, or the panel renders its empty state and no bands.
        var cut = RenderEdit(withActiveEnrollment: true);

        // Assert — the Enrollment panel is a stack of BANDS, one per kind of fact (owner, 2026-10-25,
        // mock v4/v5). Round A ships the first two: what the enrolment IS, and the activity-group
        // memberships. The enrolment-required / assumed subject bands arrive with the RequiresEnrollment
        // flag in round B, so asserting their absence here pins the current scope.
        var panel = cut.Find("#student-edit-enrollment");
        panel.QuerySelectorAll(".rail-band").Should().HaveCount(2,
            "round A draws exactly the placement band and the activity-groups band");
        panel.TextContent.Should().Contain("Placement");
        panel.TextContent.Should().Contain("Activity groups");

        // Assert — the groups band carries the add affordance; with no members the band says so rather
        // than rendering an empty chip row (an empty row reads as a rendering fault).
        cut.Find("#student-edit-add-group").Should().NotBeNull("the band's ＋ Add group opens the picker");
        cut.Find("#student-edit-activity-groups").TextContent.Should().Contain("No groups yet");

        // Assert — no band may nest a PanelSection: one box per block (§3).
        cut.Find("#student-edit-enrollment").QuerySelectorAll(".rail-band .panel-section").Should().BeEmpty(
            "a band is panel CONTENT, never a second box inside the panel");
    }

    [TestMethod]
    public void CreateSurface_PresentsTheSameRailOrderAsEdit_WithKebabOnlyGuardianAdd()
    {
        // Arrange / Act
        var cut = RenderCreate();

        // Assert — the create page now wears the SAME skeleton as edit (owner, 2026-10-24, mock v3):
        // a main column of the student's own blocks and a rail whose first panel is Enrollment, with
        // Guardians directly beneath it. Before this round create was a single column and Guardians
        // was a third block in the flow.
        var main = cut.Find(".region-grid__main");
        main.QuerySelectorAll("#student-create-details").Should().ContainSingle("Details is a main block");
        main.QuerySelectorAll("#student-create-contacts").Should().ContainSingle("Contacts is a main block");
        main.QuerySelectorAll("#student-create-guardians").Should().BeEmpty("Guardians is a relationship, not a field");

        var rail = cut.Find("#student-create-rail");
        rail.QuerySelectorAll(":scope > .panel-section").Select(p => p.Id).Should().Equal(
            new[] { "student-create-enrollment", "student-create-guardians" },
            "the rail reads Enrollment then Guardians — the same order as /students/{id}/edit");

        // Assert — Enrollment is explained, not actionable, on a student that does not exist yet.
        cut.Find("#student-create-enrollment-empty").TextContent.Should().Contain("once the student is saved",
            "the muted Enrollment panel says why it is empty instead of offering an action that cannot work");
        cut.Find("#student-create-enrollment").QuerySelectorAll(".row-actions-menu").Should().BeEmpty(
            "a panel that cannot act carries no kebab");

        // Assert — adding a guardian is the kebab's job: a kebab in the actions block, and NO inline
        // add row in the body (the old GuardianSection add row is gone).
        cut.Find("#student-create-guardians .panel-section__actions").QuerySelectorAll(".row-actions-menu")
            .Should().ContainSingle("the panel kebab is the add path");
        cut.Find("#student-create-guardians-count").TextContent.Trim().Should().Be("0");
        cut.Find("#student-create-guardians").QuerySelectorAll(".guardian-add-row").Should().BeEmpty(
            "the kebab is the only add path — no inline guardian add row survives");
        cut.Find("#student-create-guardians").QuerySelectorAll("#student-create-guardians-empty").Should().ContainSingle(
            "an empty collection still renders its empty state");
    }

    [TestMethod]
    public void EditSurface_PageHeaderOwnsThePair_AndGuardiansSitsUnderEnrollmentInTheRail()
    {
        // Arrange / Act
        var cut = RenderEdit();

        // Assert — the page action header owns the pair (§4). The old sidebar is gone, so both
        // buttons must be reachable outside any block.
        cut.FindAll("#authoring-primary-action").Should().ContainSingle(
            "the page's primary action renders in the page action header");
        cut.FindAll("#authoring-cancel").Should().ContainSingle(
            "Cancel sits beside it in the page action header");

        // Assert — main column carries the student's own detail only; the two relationships live in
        // the rail, Enrollment first and Guardians directly beneath it (owner, 2026-10-24, mock v3:
        // /students/create presents the same order).
        var main = cut.Find(".region-grid__main");
        main.QuerySelectorAll("#student-edit-details").Should().ContainSingle("Details is a main block");
        main.QuerySelectorAll("#student-edit-contacts").Should().ContainSingle("Contacts is a main block");
        main.QuerySelectorAll("#student-edit-enrollment").Should().BeEmpty("Enrollment is a relationship");
        main.QuerySelectorAll("#student-edit-guardians").Should().BeEmpty("Guardians is a relationship");

        var rail = cut.Find("#student-edit-rail");
        rail.QuerySelectorAll("#student-edit-enrollment").Should().ContainSingle(
            "Enrollment is the first rail relationship");
        rail.QuerySelectorAll("#student-edit-guardians").Should().ContainSingle(
            "Guardians is the second rail relationship");

        // Assert — order inside the rail: Enrollment above Guardians.
        var panels = rail.QuerySelectorAll(":scope > .panel-section").Select(p => p.Id).ToArray();
        panels.Should().Equal(new[] { "student-edit-enrollment", "student-edit-guardians" },
            "the rail reads Enrollment then Guardians on this page and on /students/create");
    }

    [TestMethod]
    public void OnlyGuardiansRendersAChevron_AndItIsTheOnlyAddPath()
    {
        // Arrange / Act
        var cut = RenderEdit();

        // Assert — collapsibility is opt-in (§6): Guardians is a collection so it is collapsible;
        // Enrollment is one record, Details and Contacts are main blocks, so none of them may show
        // an affordance they do not honour.
        cut.Find("#student-edit-guardians").QuerySelectorAll(".panel-section__chevron").Should().ContainSingle(
            "Guardians is a collection, so its panel is collapsible and carries the chevron");
        foreach (var id in new[] { "#student-edit-details", "#student-edit-contacts", "#student-edit-enrollment" })
        {
            cut.Find(id).QuerySelectorAll(".panel-section__chevron").Should().BeEmpty(
                $"{id} must not offer a collapse affordance");
        }

        // Assert — the count chip belongs to the TITLE, the kebab belongs to the actions block. They
        // shared one right-aligned wrapper until 2026-10-24, which is why a count rendered at the far
        // right of the row and read as unrelated to its title (owner report).
        var chip = cut.Find("#student-edit-guardians-count");
        chip.ClassList.Should().Contain("panel-section__count", "the chip is PanelSection's own count element");
        chip.ParentElement!.ClassList.Should().Contain("panel-section__header",
            "the count sits in the header row, not inside the right-aligned actions block");
        var actions = cut.Find("#student-edit-guardians .panel-section__actions");
        actions.QuerySelectorAll(".panel-section__count").Should().BeEmpty(
            "a count is not an action — keeping it out of the actions block is what puts it beside the title");
        actions.QuerySelectorAll(".row-actions-menu").Should().ContainSingle(
            "the kebab is the add path");

        // And it follows the title in document order (chevron → title → count → actions).
        var header = chip.ParentElement!;
        var order = header.Children.Select(c => c.ClassName ?? string.Empty).ToList();
        order.FindIndex(c => c.Contains("panel-section__title"))
            .Should().BeLessThan(order.FindIndex(c => c.Contains("panel-section__count")),
                "the count renders AFTER the title and BEFORE the actions");
    }
}
