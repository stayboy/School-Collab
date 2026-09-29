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
using SchoolCollab.Core.Features;
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

    /// <summary>
    /// Value handed to the registered <see cref="StubFlagService"/>. Set to false
    /// (before rendering) to reproduce the default dark-launched state, in which
    /// <c>/activity-groups</c> is not mapped at all.
    /// </summary>
    protected bool ActivityGroupsEnabled { get; set; } = true;

    public TopicCreateDialogTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
        // The dialog resolves FEATURE:EnableActivityGroups before loading
        // /activity-groups (the route only exists when the flag is on).
        Services.AddSingleton<IFeatureFlagService>(new StubFlagService(this));
    }

    /// <summary>
    /// Minimal <see cref="IFeatureFlagService"/> whose state is read live from the
    /// owning test, so a test can flip the flag after the constructor has run.
    /// </summary>
    private sealed class StubFlagService : IFeatureFlagService
    {
        private readonly TopicCreateDialogTests _owner;
        public StubFlagService(TopicCreateDialogTests owner) => _owner = owner;
        private bool Enabled => _owner.ActivityGroupsEnabled;
        public bool IsEnabled(string featureKey) => Enabled;
        public Task<bool> IsEnabledAsync(string featureKey, CancellationToken ct = default) => Task.FromResult(Enabled);
        public IDictionary<string, bool> GetAllFlags() => new Dictionary<string, bool>();
        public Task<IReadOnlyDictionary<string, bool>> GetAllFlagsAsync(Guid? tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, bool>>(new Dictionary<string, bool>());
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

    private void Register(ScriptedHandler handler, bool mapActivityGroups = true)
    {
        // The dialog's OnInitializedAsync loads the activity groups to populate the
        // owner picker. Map it to empty so the dialog renders in the test.
        // Leave /activity-groups unmapped (mapActivityGroups: false) to reproduce
        // the FEATURE:EnableActivityGroups-off reality, where the route does not exist.
        //
        // NOTE: the dialog no longer loads /students/periods — its period picker was
        // removed in the subject-period-blocks round (the period is now an EXCEPTION,
        // a SubjectEnrollmentException managed from TopicEditDialog).
        if (mapActivityGroups)
            handler.Map("GET", "/activity-groups", HttpStatusCode.OK, "[]");

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        var cv = new CodedValuesApiClient(http);
        Services.AddSingleton(cv);
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, cv));
        Services.AddSingleton(new EntityCodeRulesApiClient(http));
    }

    private static TopicCreateDialog.TopicCreateModel CreateModel() =>
        new() { GradeLevelId = Guid.NewGuid() };

    /// <summary>
    /// Regression: <c>/activity-groups</c> is only mapped when
    /// <c>FEATURE:EnableActivityGroups</c> is on. With the flag off the dialog
    /// called it unguarded from <c>OnInitializedAsync</c>, the 404 escaped, and the
    /// entire dialog failed to open — taking the grade-level owner path down with
    /// it. The dialog must open and offer the Grade Level owner instead.
    /// </summary>
    [TestMethod]
    public async Task CreateDialog_ActivityGroupsFlagOff_StillOpensWithGradeOwner()
    {
        ActivityGroupsEnabled = false;

        var handler = new ScriptedHandler();
        Register(handler, mapActivityGroups: false);

        var cut = Render<FluentDialogProvider>();
        var task = DialogService.ShowShellDialogAsync<TopicCreateDialog, TopicCreateDialog.TopicCreateModel, TopicDto>(
            CreateModel(), "Add subject", DialogSize.Large);

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull(
            "the dialog must open even though /activity-groups 404s"));

        cut.Markup.Should().Contain("Grade Level",
            "the always-available grade-level owner must still be offered");
        handler.Calls.Should().NotContain(c => c.Url.StartsWith("/activity-groups", StringComparison.OrdinalIgnoreCase),
            "a route that is known to be unmapped should not be called at all");

        cut.Find("fluent-button[aria-label='Close']").Click();
        (await task.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeNull();
    }

    /// <summary>
    /// A caller may pre-seed the group owner (e.g. Subjects.razor does). With the
    /// flag off there is no group owner to honour, so the model must fall back to
    /// the grade-level owner rather than leaving the dialog pointed at a dead one.
    /// </summary>
    [TestMethod]
    public async Task CreateDialog_ActivityGroupsFlagOff_PreseededGroupOwnerFallsBackToGradeLevel()
    {
        ActivityGroupsEnabled = false;

        var handler = new ScriptedHandler();
        Register(handler, mapActivityGroups: false);

        var model = new TopicCreateDialog.TopicCreateModel
        {
            GradeLevelId = Guid.NewGuid(),
            OwnerType = "ActivityGroup",
            ActivityGroupId = Guid.NewGuid(),
        };

        var cut = Render<FluentDialogProvider>();
        var task = DialogService.ShowShellDialogAsync<TopicCreateDialog, TopicCreateDialog.TopicCreateModel, TopicDto>(
            model, "Add subject", DialogSize.Large);

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());

        model.OwnerType.Should().Be("GradeLevel", "the unavailable group owner must be cleared");
        model.ActivityGroupId.Should().BeNull("a dangling group id must not survive the fallback");

        cut.Find("fluent-button[aria-label='Close']").Click();
        (await task.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeNull();
    }

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

    private static TopicCreateDialog.TopicCreateModel GroupModel(string ownerType) =>
        new() { GradeLevelId = Guid.NewGuid(), OwnerType = ownerType };

    private async Task DriveGroupSelectAsync(IRenderedComponent<FluentDialogProvider> cut, string groupId)
    {
        var groupSelect = cut.FindComponents<FluentSelect<string>>()
            .First(s => s.Instance.Id == "topic-create-group");
        // Drive the bound ValueChanged callback (not SelectedOptionChanged): the
        // dialog binds @bind-Value:after="OnActivityGroupChangedAsync", so only
        // ValueChanged sets _activityGroupIdText AND triggers the group-topic reload
        // that backs the duplicate-coded-value guard.
        await cut.InvokeAsync(() => groupSelect.Instance.ValueChanged.InvokeAsync(groupId));
    }

    /// <summary>
    /// AC-2a/2b (FR-56): with an ActivityGroup owner, once a group is selected the
    /// dialog shows a read-only "Enrollment span" badge with the group's span value
    /// (Termly). With no group selected the span display is absent.
    /// </summary>
    [TestMethod]
    public async Task CreateDialog_GroupSelected_ShowsEnrollmentSpanBadge()
    {
        var handler = new ScriptedHandler();
        Register(handler);
        handler.Map("GET", "/activity-groups", HttpStatusCode.OK, GroupJson("Termly"));
        handler.Map("GET", $"/students/subjects/by-group/{GroupId}", HttpStatusCode.OK, "[]");

        var cut = Render<FluentDialogProvider>();
        var task = DialogService.ShowShellDialogAsync<TopicCreateDialog, TopicCreateDialog.TopicCreateModel, TopicDto>(
            GroupModel("ActivityGroup"), "Add subject", DialogSize.Large);

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());
        // AC-2a: nothing selected yet → no span display.
        cut.Markup.Should().NotContain("Enrollment span");

        await DriveGroupSelectAsync(cut, GroupId.ToString());

        // AC-2b: span badge shows the group's value.
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Enrollment span"));
        cut.Markup.Should().Contain("Termly", "AC-2b: the selected group's span is displayed");

        // Cleanup: close the dialog.
        cut.Find("fluent-button[aria-label='Close']").Click();
        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().BeNull("closing the dialog yields no result");
    }

    /// <summary>
    /// AC-5 (subject-period-blocks round): the period picker is GONE from the create
    /// dialog on the default (grade) owner path — the bridge row it creates carries no
    /// period meaning, and the period is now an exception managed in TopicEditDialog.
    /// The dialog must not even load the tenant's periods.
    /// </summary>
    [TestMethod]
    public async Task CreateDialog_GradeOwner_HasNoPeriodPicker()
    {
        var handler = new ScriptedHandler();
        Register(handler);

        var cut = Render<FluentDialogProvider>();
        var task = DialogService.ShowShellDialogAsync<TopicCreateDialog, TopicCreateDialog.TopicCreateModel, TopicDto>(
            CreateModel(), "Add subject", DialogSize.Large);

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());

        cut.FindAll("#topic-create-period").Should().BeEmpty(
            "the period picker was removed — the create flow is period-less (AC-5)");
        cut.Markup.Should().NotContain("Period (optional)", "the period field is not part of the form any more");
        handler.Calls.Should().NotContain(c => c.Url.Contains("/students/periods"),
            "a dialog with no period picker must not fetch the tenant's periods");

        // Cleanup: close the dialog.
        cut.Find("fluent-button[aria-label='Close']").Click();
        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().BeNull("closing the dialog yields no result");
    }

    /// <summary>
    /// AC-5 on the GROUP owner path: the picker is gone there too. The OpenEnded span
    /// (which used to be the "carries no period" case) is exercised, so a group owner
    /// that can never carry a period cannot reintroduce a period control.
    /// </summary>
    [TestMethod]
    public async Task CreateDialog_GroupOwner_HasNoPeriodPicker()
    {
        var handler = new ScriptedHandler();
        Register(handler);
        handler.Map("GET", "/activity-groups", HttpStatusCode.OK, GroupJson("OpenEnded"));
        handler.Map("GET", $"/students/subjects/by-group/{GroupId}", HttpStatusCode.OK, "[]");

        var cut = Render<FluentDialogProvider>();
        var task = DialogService.ShowShellDialogAsync<TopicCreateDialog, TopicCreateDialog.TopicCreateModel, TopicDto>(
            GroupModel("ActivityGroup"), "Add subject", DialogSize.Large);

        cut.WaitForAssertion(() => cut.Find("form").Should().NotBeNull());
        await DriveGroupSelectAsync(cut, GroupId.ToString());

        cut.FindAll("#topic-create-period").Should().BeEmpty(
            "the period picker was removed for every owner kind (AC-5)");
        cut.Markup.Should().NotContain("Term 1");
        cut.Markup.Should().NotContain("Semester A");
        handler.Calls.Should().NotContain(c => c.Url.Contains("/students/periods"),
            "a dialog with no period picker must not fetch the tenant's periods");

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
    /// AC-5/AC-46 (subject-period-blocks round): the grade-owned create submits
    /// <c>CreateTopicForGrade</c> with <c>periodId: null</c>. The field stays on the wire
    /// contract for back-compat but is now a no-op — the period picker is gone, so a
    /// created subject is simply offered, with no period meaning on the bridge row.
    /// Driven via <c>EditForm.OnValidSubmit</c>, not a button click.
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
