using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components;
using SchoolCollab.Admin.Shared.Components.Dialogs;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Students.Application.Components.Students;
using SchoolCollab.Students.Application.Services;
using TopicDto = SchoolCollab.Students.Core.DTOs.TopicDto;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for <see cref="TopicCreateDialog"/> (grade-detail Subjects card
/// Add button). Rendered through the real <see cref="FluentDialogProvider"/> +
/// <c>DialogService.ShowShellDialogAsync</c> pipeline. The dialog creates a
/// brand-new topic (displayed as a subject) wired to a grade.
/// </summary>
[TestClass]
public class TopicCreateDialogTests : BunitContext
{
    private IDialogService DialogService => Services.GetRequiredService<IDialogService>();

    public TopicCreateDialogTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public readonly List<(string Method, string Url, string? Body)> Calls = new();
        private readonly Dictionary<(string Method, string Url), (HttpStatusCode Status, string Body)> _responses = new();

        public ScriptedHandler Map(string method, string url, HttpStatusCode status, string body)
        {
            _responses[(method.ToUpperInvariant(), url)] = (status, body);
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request.Method.Method, request.RequestUri!.PathAndQuery, body));

            var url = request.RequestUri.PathAndQuery;
            if (_responses.TryGetValue((request.Method.Method.ToUpperInvariant(), url), out var exact))
                return new HttpResponseMessage(exact.Status) { Content = new StringContent(exact.Body, Encoding.UTF8, "application/json") };

            return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent($"Unexpected {url}", Encoding.UTF8, "application/json") };
        }
    }

    private void Register(ScriptedHandler handler)
    {
        // The dialog's OnInitializedAsync loads activity groups and periods to
        // populate the owner/period pickers. Map them to empty so the dialog
        // renders in the test. GetActiveAcademicYearAsync returns null on 404.
        handler.Map("GET", "/activity-groups", HttpStatusCode.OK, "[]");
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, "[]");

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        var cv = new CodedValuesApiClient(http);
        Services.AddSingleton(cv);
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, cv));
        Services.AddSingleton(new EntityCodeRulesApiClient(http));
    }

    private static TopicCreateDialog.TopicCreateModel CreateModel() =>
        new() { GradeLevelId = Guid.NewGuid() };

    /// <summary>
    /// The Create submit button text must come from the dialog's
    /// <c>SubmitText</c> override ("Create") — the footer must reference
    /// <c>@SubmitText</c> rather than a hardcoded literal.
    /// </summary>
    [TestMethod]
    public async Task CreateDialog_ShowsSubmitText_Create()
    {
        Register(new ScriptedHandler());

        var cut = Render<FluentDialogProvider>();
        var task = DialogService.ShowShellDialogAsync<TopicCreateDialog, TopicCreateDialog.TopicCreateModel, TopicDto>(
            CreateModel(), "Add subject", DialogSize.Large);

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());

        // The submit button text is "Create" (from the SubmitText override).
        var submit = cut.FindAll("fluent-button").Single(b => b.TextContent.Contains("Create"));
        submit.Should().NotBeNull("the Submit button text MUST be \"Create\" (the dialog overrides SubmitText)");
        submit.TextContent.Should().NotBe("Save", "the dialog must override the default DialogShellBase SubmitText of \"Save\"");

        // Cleanup: close the dialog from display mode.
        cut.Find("fluent-button[aria-label='Close']").Click();
        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().BeNull("closing the dialog yields no result");
    }

    /// <summary>
    /// The create dialog toggles its submit like the edit dialog: display mode
    /// shows a server "Create", edit mode shows a LOCAL "Update Fields" (no
    /// server submit).
    /// </summary>
    [TestMethod]
    public async Task CreateDialog_EditMode_TogglesToUpdateFields_LocalAction()
    {
        Register(new ScriptedHandler());

        var cut = Render<FluentDialogProvider>();
        var task = DialogService.ShowShellDialogAsync<TopicCreateDialog, TopicCreateDialog.TopicCreateModel, TopicDto>(
            CreateModel(), "Add subject", DialogSize.Large);

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());

        // Display mode: the submit is the server "Create".
        var create = cut.FindAll("fluent-button").Single(b => b.TextContent.Contains("Create"));
        create.GetAttribute("type").Should().Be("submit", "display mode shows the server Create action");
        cut.Markup.Should().NotContain("Update Fields", "display mode does not show Update Fields");

        // Switch to edit mode.
        cut.Find("fluent-button[aria-label='Edit topic fields']").Click();

        // Edit mode: the submit becomes the LOCAL "Update Fields" (a plain
        // button, not a form submit) — the server "Create" is gone.
        // WaitForAssertion: the CodedValueDropdown loads coded values
        // asynchronously, and the re-render from StartEditing races with that
        // pending load. Asserting immediately was flaky in CI (the read-back
        // markup could still be the display-mode "Create" snapshot).
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Update Fields",
            "edit mode shows the local Update Fields action"));
        cut.Markup.Should().NotContain("Create", "edit mode replaces the server Create with Update Fields");
        var update = cut.FindAll("fluent-button").Single(b => b.TextContent.Contains("Update Fields"));
        update.GetAttribute("type").Should().NotBe("submit", "Update Fields is a local action, not a server submit");

        // Cleanup: close the dialog.
        cut.Find("fluent-button[aria-label='Close']").Click();
        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().BeNull("closing the dialog yields no result");
    }

    /// <summary>
    /// Item 1 (deferred P2): when the model is seeded with an existing coded
    /// value id and the user picks that coded value, the duplicate warning bar
    /// renders and Create is disabled (grade owner).
    /// </summary>
    [TestMethod]
    public async Task CreateDialog_GradeOwner_DuplicateCodedValue_WarnsAndDisables()
    {
        var handler = new ScriptedHandler();
        var cvId = Guid.NewGuid();
        // The CodedValueDropdown loads SUBJECT coded values via this endpoint.
        handler.Map("GET", "/api/coded-values/by-parent?parentCode=SUBJECT", HttpStatusCode.OK,
            $"[{{\"id\":\"{cvId}\",\"code\":\"MATH\",\"name\":\"Mathematics\"}}]");
        Register(handler);

        var cut = Render<FluentDialogProvider>();
        var model = CreateModel();
        model.ExistingTopicCodedValueIds = new HashSet<Guid> { cvId };
        var task = DialogService.ShowShellDialogAsync<TopicCreateDialog, TopicCreateDialog.TopicCreateModel, TopicDto>(
            model, "Add subject", DialogSize.Large);

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());

        // Drive the CodedValueDropdown's FluentSelect to pick the coded value.
        var dropdown = cut.FindComponent<CodedValueDropdown>();
        var fluentSelect = dropdown.FindComponent<FluentSelect<CodedValueDto>>();
        var picked = dropdown.Instance.Items.First(i => i.Id == cvId);
        await cut.InvokeAsync(() => fluentSelect.Instance.SelectedOptionChanged.InvokeAsync(picked));

        // The duplicate warning bar renders and Create is disabled.
        cut.WaitForAssertion(() =>
            cut.Markup.Should().Contain("This subject is already linked to the grade"));
        var create = cut.FindAll("fluent-button").Single(b => b.TextContent.Contains("Create"));
        create.GetAttribute("disabled").Should().NotBeNull("Create is disabled when a duplicate coded value is picked");

        // Cleanup: close the dialog.
        cut.Find("fluent-button[aria-label='Close']").Click();
        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().BeNull("closing the dialog yields no result");
    }

    private static string GroupJson(string span) =>
        $"[{{\"id\":\"{GroupId}\",\"name\":\"Chess Club\",\"description\":null,\"category\":null,\"capacity\":null,\"isActive\":true,\"span\":\"{span}\",\"enrollmentStartDate\":null,\"enrollmentEndDate\":null,\"autoRenewDefault\":true,\"eligibleGradeIds\":[],\"activeMemberCount\":0,\"createdAt\":\"2026-01-01T00:00:00Z\",\"updatedAt\":\"2026-01-01T00:00:00Z\"}}]";

    private static readonly Guid GroupId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid YearId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Term1Id = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Term2Id = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid SemesterId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private static string PeriodsJson() =>
        $"[{{\"id\":\"{YearId}\",\"name\":\"2026\",\"startDate\":\"2026-01-01\",\"endDate\":\"2026-12-31\",\"status\":\"Active\",\"periodType\":\"AcademicYear\",\"parentPeriodId\":null,\"nextPeriodId\":null,\"createdAt\":\"2026-01-01T00:00:00Z\",\"updatedAt\":\"2026-01-01T00:00:00Z\"}}," +
        $"{{\"id\":\"{Term1Id}\",\"name\":\"Term 1\",\"startDate\":\"2026-01-01\",\"endDate\":\"2026-06-30\",\"status\":\"Active\",\"periodType\":\"Term\",\"parentPeriodId\":\"{YearId}\",\"nextPeriodId\":null,\"createdAt\":\"2026-01-01T00:00:00Z\",\"updatedAt\":\"2026-01-01T00:00:00Z\"}}," +
        $"{{\"id\":\"{Term2Id}\",\"name\":\"Term 2\",\"startDate\":\"2026-07-01\",\"endDate\":\"2026-12-31\",\"status\":\"Active\",\"periodType\":\"Term\",\"parentPeriodId\":\"{YearId}\",\"nextPeriodId\":null,\"createdAt\":\"2026-01-01T00:00:00Z\",\"updatedAt\":\"2026-01-01T00:00:00Z\"}}," +
        $"{{\"id\":\"{SemesterId}\",\"name\":\"Semester A\",\"startDate\":\"2026-01-01\",\"endDate\":\"2026-06-30\",\"status\":\"Active\",\"periodType\":\"Semester\",\"parentPeriodId\":\"{YearId}\",\"nextPeriodId\":null,\"createdAt\":\"2026-01-01T00:00:00Z\",\"updatedAt\":\"2026-01-01T00:00:00Z\"}}]";

    private static TopicCreateDialog.TopicCreateModel GroupModel(string ownerType) =>
        new() { GradeLevelId = Guid.NewGuid(), OwnerType = ownerType };

    private async Task DriveGroupSelectAsync(IRenderedComponent<FluentDialogProvider> cut, string groupId)
    {
        var groupSelect = cut.FindComponents<FluentSelect<string>>()
            .First(s => s.Instance.Id == "topic-create-group");
        // Drive the bound ValueChanged callback (not SelectedOptionChanged): the
        // dialog binds @bind-Value:after="OnActivityGroupChangedAsync", so only
        // ValueChanged sets _activityGroupIdText AND triggers the period reload.
        await cut.InvokeAsync(() => groupSelect.Instance.ValueChanged.InvokeAsync(groupId));
    }

    /// <summary>
    /// AC-2a/2b/2c (FR-56): with an ActivityGroup owner, once a group is
    /// selected the dialog shows a read-only "Enrollment span" badge with the
    /// group's span value (Termly), coexisting with the filtered period options.
    /// With no group selected the span display is absent.
    /// </summary>
    [TestMethod]
    public async Task CreateDialog_GroupSelected_ShowsEnrollmentSpanBadge()
    {
        var handler = new ScriptedHandler();
        Register(handler);
        handler.Map("GET", "/activity-groups", HttpStatusCode.OK, GroupJson("Termly"));
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, PeriodsJson());
        handler.Map("GET", $"/students/subjects/by-group/{GroupId}", HttpStatusCode.OK, "[]");

        var cut = Render<FluentDialogProvider>();
        var task = DialogService.ShowShellDialogAsync<TopicCreateDialog, TopicCreateDialog.TopicCreateModel, TopicDto>(
            GroupModel("ActivityGroup"), "Add subject", DialogSize.Large);

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());
        // AC-2a: nothing selected yet → no span display.
        cut.Markup.Should().NotContain("Enrollment span");

        await DriveGroupSelectAsync(cut, GroupId.ToString());

        // AC-2b/2c: span badge shows the group's value, alongside the filtered Term options.
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Enrollment span"));
        cut.Markup.Should().Contain("Termly", "AC-2b: the selected group's span is displayed");
        cut.Markup.Should().Contain("Term 1", "AC-2c: the span display coexists with the filtered period options");

        // Cleanup: close the dialog.
        cut.Find("fluent-button[aria-label='Close']").Click();
        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().BeNull("closing the dialog yields no result");
    }

    /// <summary>
    /// AC-45 (FR-56): a <c>Termly</c> activity group must only offer <c>Term</c>
    /// periods — a <c>Semester</c> period must be impossible to select.
    /// </summary>
    [TestMethod]
    public async Task CreateDialog_TermlyGroup_PeriodOptionsFilteredToTerms()
    {
        var handler = new ScriptedHandler();
        Register(handler);
        handler.Map("GET", "/activity-groups", HttpStatusCode.OK, GroupJson("Termly"));
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, PeriodsJson());
        handler.Map("GET", $"/students/subjects/by-group/{GroupId}", HttpStatusCode.OK, "[]");

        var cut = Render<FluentDialogProvider>();
        var task = DialogService.ShowShellDialogAsync<TopicCreateDialog, TopicCreateDialog.TopicCreateModel, TopicDto>(
            GroupModel("ActivityGroup"), "Add subject", DialogSize.Large);

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());
        await DriveGroupSelectAsync(cut, GroupId.ToString());

        // The two Term periods are offered; the Semester period is not.
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Term 1"));
        cut.Markup.Should().Contain("Term 2");
        cut.Markup.Should().NotContain("Semester A", "a Termly group must not offer a Semester period (AC-45)");

        // Cleanup: close the dialog.
        cut.Find("fluent-button[aria-label='Close']").Click();
        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().BeNull("closing the dialog yields no result");
    }

    /// <summary>
    /// AC-45 (FR-56): an <c>OpenEnded</c> group carries no period — the info bar
    /// renders and no period <c>FluentSelect</c> is shown.
    /// </summary>
    [TestMethod]
    public async Task CreateDialog_OpenEndedGroup_ShowsNoPeriodHint()
    {
        var handler = new ScriptedHandler();
        Register(handler);
        handler.Map("GET", "/activity-groups", HttpStatusCode.OK, GroupJson("OpenEnded"));
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, PeriodsJson());
        handler.Map("GET", $"/students/subjects/by-group/{GroupId}", HttpStatusCode.OK, "[]");

        var cut = Render<FluentDialogProvider>();
        var task = DialogService.ShowShellDialogAsync<TopicCreateDialog, TopicCreateDialog.TopicCreateModel, TopicDto>(
            GroupModel("ActivityGroup"), "Add subject", DialogSize.Large);

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());
        await DriveGroupSelectAsync(cut, GroupId.ToString());

        // The info bar renders and no period select exists.
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("carries no period"));
        cut.FindAll("#topic-create-period").Should().BeEmpty("an OpenEnded group must not render a period select");
        cut.Markup.Should().NotContain("Term 1");
        cut.Markup.Should().NotContain("Semester A");

        // Cleanup: close the dialog.
        cut.Find("fluent-button[aria-label='Close']").Click();
        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().BeNull("closing the dialog yields no result");
    }

    /// <summary>
    /// Item 3 (group-path duplicate guard): picking a coded value already
    /// assigned to the selected group warns, disables Create, and blocks the
    /// assign POST when submit is driven.
    /// </summary>
    [TestMethod]
    public async Task CreateDialog_GroupOwner_DuplicateCodedValue_WarnsAndBlocksAssign()
    {
        var handler = new ScriptedHandler();
        var cvId = Guid.NewGuid();
        Register(handler);
        handler.Map("GET", "/api/coded-values/by-parent?parentCode=SUBJECT", HttpStatusCode.OK,
            $"[{{\"id\":\"{cvId}\",\"code\":\"MATH\",\"name\":\"Mathematics\"}}]");
        handler.Map("GET", "/activity-groups", HttpStatusCode.OK, GroupJson("Termly"));
        handler.Map("GET", "/students/periods", HttpStatusCode.OK, PeriodsJson());
        handler.Map("GET", $"/students/subjects/by-group/{GroupId}", HttpStatusCode.OK,
            $"[{{\"id\":\"{Guid.NewGuid()}\",\"codedValueId\":\"{cvId}\",\"code\":\"MATH\",\"name\":\"Mathematics\",\"displayOrder\":1,\"isOverridden\":false,\"createdAt\":\"2026-01-01T00:00:00Z\",\"updatedAt\":\"2026-01-01T00:00:00Z\"}}]");

        var cut = Render<FluentDialogProvider>();
        var task = DialogService.ShowShellDialogAsync<TopicCreateDialog, TopicCreateDialog.TopicCreateModel, TopicDto>(
            GroupModel("ActivityGroup"), "Add subject", DialogSize.Large);

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());
        await DriveGroupSelectAsync(cut, GroupId.ToString());

        // Pick the coded value that is already assigned to the group.
        var dropdown = cut.FindComponent<CodedValueDropdown>();
        var fluentSelect = dropdown.FindComponent<FluentSelect<CodedValueDto>>();
        var picked = dropdown.Instance.Items.First(i => i.Id == cvId);
        await cut.InvokeAsync(() => fluentSelect.Instance.SelectedOptionChanged.InvokeAsync(picked));

        // Warning bar + disabled Create.
        cut.WaitForAssertion(() =>
            cut.Markup.Should().Contain("This subject is already assigned to this activity group."));
        var create = cut.FindAll("fluent-button").Single(b => b.TextContent.Contains("Create"));
        create.GetAttribute("disabled").Should().NotBeNull("Create is disabled on a duplicate group assignment");

        // Drive the form submit — the guard must block the assign POST.
        // OnValidSubmit is EventCallback<EditContext>; pass the form's context.
        var editForm = cut.FindComponent<EditForm>();
        await cut.InvokeAsync(() => editForm.Instance.OnValidSubmit.InvokeAsync(editForm.Instance.EditContext));

        handler.Calls.Should().NotContain(c => c.Method == "POST" && c.Url.Contains("/students/topics"),
            "the duplicate guard must block the create/assign POST");
        handler.Calls.Should().NotContain(c => c.Method == "POST" && c.Url.Contains("/students/topic-assignments/activity-group"),
            "the duplicate guard must block the assign POST");
        cut.WaitForAssertion(() =>
            cut.Markup.Should().Contain("This subject is already assigned to this activity group."));

        // Cleanup: close the dialog.
        cut.Find("fluent-button[aria-label='Close']").Click();
        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().BeNull("the guard keeps the dialog open");
    }

    /// <summary>
    /// AC-46 (FR-55/NFR-6): with no period selected, the grade-owned create
    /// submits <c>CreateTopicForGrade</c> with <c>periodId: null</c> (default =
    /// year-spanning). Driven via <c>EditForm.OnValidSubmit</c>, not a button click.
    /// </summary>
    [TestMethod]
    public async Task CreateDialog_GradeOwner_NullPeriodId_PostsPeriodIdNull()
    {
        var handler = new ScriptedHandler();
        var topicId = Guid.NewGuid();
        handler.Map("POST", "/students/topics/for-grade", HttpStatusCode.OK,
            $"{{\"id\":\"{topicId}\",\"codedValueId\":null,\"code\":\"MATH\",\"name\":\"Mathematics\",\"description\":null,\"displayOrder\":1,\"createdAt\":\"2026-01-01T00:00:00Z\",\"updatedAt\":\"2026-01-01T00:00:00Z\"}}");
        Register(handler);

        var cut = Render<FluentDialogProvider>();
        var model = CreateModel();
        model.Name = "Mathematics";
        var task = DialogService.ShowShellDialogAsync<TopicCreateDialog, TopicCreateDialog.TopicCreateModel, TopicDto>(
            model, "Add subject", DialogSize.Large);

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());

        // Drive the form submit directly (not a FluentButton click).
        // OnValidSubmit is EventCallback<EditContext>; pass the form's context.
        var editForm = cut.FindComponent<EditForm>();
        await cut.InvokeAsync(() => editForm.Instance.OnValidSubmit.InvokeAsync(editForm.Instance.EditContext));

        cut.WaitForAssertion(() =>
            handler.Calls.Should().Contain(c => c.Method == "POST" && c.Url.Contains("/students/topics/for-grade")));
        var call = handler.Calls.Single(c => c.Method == "POST" && c.Url.Contains("/students/topics/for-grade"));
        call.Body.Should().Contain("\"periodId\":null", "AC-46: no period selected must submit periodId null");

        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().NotBeNull("a successful create returns the created topic");
    }
}
