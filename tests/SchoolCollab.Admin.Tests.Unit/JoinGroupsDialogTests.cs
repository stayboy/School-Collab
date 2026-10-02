using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components.Dialogs;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Core.Features;
using SchoolCollab.Students.Application.Components.Students;
using SchoolCollab.Students.Application.Services;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for the span-aware <see cref="JoinGroupsDialog"/> (Sprint 6
/// Round 3, AC-35/36). Rendered through the real <see cref="FluentDialogProvider"/>
/// + <c>DialogService.ShowShellDialogAsync</c> pipeline. Verifies the active
/// period-type resolution and that period-aligned spans are filtered to the
/// currently-open period while OpenEnded groups are always joinable.
/// </summary>
[TestClass]
public class JoinGroupsDialogTests : BunitContext
{
    private IDialogService DialogService => Services.GetRequiredService<IDialogService>();

    /// <summary>
    /// Value handed to the registered <see cref="StubFlagService"/>. Set to false
    /// (before rendering) to reproduce the default dark-launched state, in which
    /// <c>/activity-groups</c> is not mapped at all.
    /// </summary>
    protected bool ActivityGroupsEnabled { get; set; } = true;

    public JoinGroupsDialogTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
        // The dialog short-circuits when FEATURE:EnableActivityGroups is off (the
        // /activity-groups route is only mapped when it is on).
        Services.AddSingleton<IFeatureFlagService>(new StubFlagService(this));
    }

    /// <summary>
    /// Minimal <see cref="IFeatureFlagService"/> whose state is read live from the
    /// owning test, so a test can flip the flag after the constructor has run.
    /// </summary>
    private sealed class StubFlagService : IFeatureFlagService
    {
        private readonly JoinGroupsDialogTests _owner;
        public StubFlagService(JoinGroupsDialogTests owner) => _owner = owner;
        private bool Enabled => _owner.ActivityGroupsEnabled;
        public bool IsEnabled(string featureKey) => Enabled;
        public Task<bool> IsEnabledAsync(string featureKey, CancellationToken ct = default) => Task.FromResult(Enabled);
        public IDictionary<string, bool> GetAllFlags() => new Dictionary<string, bool>();
        public Task<IReadOnlyDictionary<string, bool>> GetAllFlagsAsync(Guid? tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, bool>>(new Dictionary<string, bool>());
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public readonly List<string> Calls = new();
        private readonly Dictionary<(string Method, string Url), (HttpStatusCode Status, string Body)> _responses = new();

        public ScriptedHandler Map(string method, string url, HttpStatusCode status, string body)
        {
            _responses[(method.ToUpperInvariant(), url)] = (status, body);
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.PathAndQuery;
            Calls.Add(url);
            if (_responses.TryGetValue((request.Method.Method.ToUpperInvariant(), url), out var exact))
                return Task.FromResult(new HttpResponseMessage(exact.Status)
                {
                    Content = new StringContent(exact.Body, Encoding.UTF8, "application/json"),
                });

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent($"Unexpected {url}", Encoding.UTF8, "application/json"),
            });
        }
    }

    private static readonly Guid StudentId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly Guid OpenGroupId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TermGroupId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SemGroupId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid YearId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid TermId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private void Register(ScriptedHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        var cv = new CodedValuesApiClient(http);
        Services.AddSingleton(cv);
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, cv));
    }

    private static string GroupJson(Guid id, string name, string span) =>
        $"{{\"id\":\"{id}\",\"name\":\"{name}\",\"description\":null,\"category\":null,\"capacity\":null,\"isActive\":true,\"span\":\"{span}\",\"enrollmentStartDate\":null,\"enrollmentEndDate\":null,\"autoRenewDefault\":true,\"eligibleGradeIds\":[],\"activeMemberCount\":0,\"createdAt\":\"2026-01-01T00:00:00Z\",\"updatedAt\":\"2026-01-01T00:00:00Z\"}}";

    private static string PeriodJson(Guid id, string name, string periodType) =>
        periodType == "Term"
            ? $"{{\"id\":\"{id}\",\"name\":\"{name}\",\"startDate\":\"2026-01-01\",\"endDate\":\"2026-12-31\",\"status\":\"Active\",\"parentPeriodId\":\"{YearId}\",\"nextPeriodId\":null,\"division\":\"Terms\",\"createdAt\":\"2026-01-01T00:00:00Z\",\"updatedAt\":\"2026-01-01T00:00:00Z\"}}"
            : $"{{\"id\":\"{id}\",\"name\":\"{name}\",\"startDate\":\"2026-01-01\",\"endDate\":\"2026-12-31\",\"status\":\"Active\",\"parentPeriodId\":null,\"nextPeriodId\":null,\"division\":\"None\",\"createdAt\":\"2026-01-01T00:00:00Z\",\"updatedAt\":\"2026-01-01T00:00:00Z\"}}";

    /// <summary>
    /// Defense in depth. The Activity Groups section that opens this dialog is
    /// already wrapped in a <c>FeatureFlagGate</c> (Detail.razor), but the dialog
    /// must not surface a raw 404 from the unmapped <c>/activity-groups</c> route
    /// if it is ever reached with the flag off — it should say the feature is off,
    /// and must not call the route at all.
    /// </summary>
    [TestMethod]
    public async Task JoinDialog_ActivityGroupsFlagOff_SaysFeatureDisabledAndSkipsRoute()
    {
        ActivityGroupsEnabled = false;

        var handler = new ScriptedHandler();
        // Deliberately do NOT map /activity-groups: with the flag off the route is
        // unmapped, so an unmapped (404) route is the realistic condition.
        handler.Map("GET", $"/students/{StudentId}/activity-groups", HttpStatusCode.OK, "[]");
        Register(handler);

        var cut = Render<FluentDialogProvider>();
        var task = DialogService.ShowShellDialogAsync<JoinGroupsDialog, JoinGroupsDialog.JoinGroupsModel, JoinGroupsDialog.JoinGroupsResult>(
            new JoinGroupsDialog.JoinGroupsModel { StudentId = StudentId }, "Join groups", DialogSize.Medium);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("not enabled for this tenant"));

        cut.Markup.Should().NotContain("Unexpected /activity-groups",
            "the raw 404 body must never reach the user");

        handler.Calls.Should().NotContain(url => url.StartsWith("/activity-groups", StringComparison.OrdinalIgnoreCase),
            "a route that is known to be unmapped should not be called at all");

        cut.Find("fluent-button[aria-label='Close']").Click();
        (await task.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeNull();
    }

    /// <summary>
    /// A7 (AC-36): with no active period (both active-period GETs 404), the
    /// active period type resolves to AcademicYear and an OpenEnded group is
    /// listed as joinable.
    /// </summary>
    [TestMethod]
    public async Task JoinDialog_OpenEnded_Listed_WhenNoActivePeriod()
    {
        var handler = new ScriptedHandler();
        handler.Map("GET", "/activity-groups", HttpStatusCode.OK,
            $"[{GroupJson(OpenGroupId, "Chess Club", "OpenEnded")}]");
        handler.Map("GET", $"/students/{StudentId}/activity-groups", HttpStatusCode.OK, "[]");
        handler.Map("GET", "/students/periods/active-sub-period", HttpStatusCode.NotFound, "{}");
        handler.Map("GET", "/students/periods/active-academic-year", HttpStatusCode.NotFound, "{}");
        Register(handler);

        var cut = Render<FluentDialogProvider>();
        var task = DialogService.ShowShellDialogAsync<JoinGroupsDialog, JoinGroupsDialog.JoinGroupsModel, JoinGroupsDialog.JoinGroupsResult>(
            new JoinGroupsDialog.JoinGroupsModel { StudentId = StudentId }, "Join groups", DialogSize.Medium);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Chess Club"));
        cut.Markup.Should().Contain("OpenEnded", "the group option text shows the span");

        // Cleanup: close the dialog.
        cut.Find("fluent-button[aria-label='Close']").Click();
        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().BeNull("closing the dialog yields no result");
    }

    /// <summary>
    /// A8 (AC-35): with an active Term, a Termly group is listed but a Semester
    /// group is filtered out (period-aligned spans only join the matching period).
    /// </summary>
    [TestMethod]
    public async Task JoinDialog_Termly_Listed_SemesterFiltered_WhenActiveTerm()
    {
        var handler = new ScriptedHandler();
        handler.Map("GET", "/activity-groups", HttpStatusCode.OK,
            $"[{GroupJson(TermGroupId, "Chess Club", "Termly")},{GroupJson(SemGroupId, "Semester Band", "Semester")}]");
        handler.Map("GET", $"/students/{StudentId}/activity-groups", HttpStatusCode.OK, "[]");
        handler.Map("GET", "/students/periods/active-sub-period", HttpStatusCode.OK, PeriodJson(TermId, "Term 1", "Term"));
        handler.Map("GET", "/students/periods/active-academic-year", HttpStatusCode.OK, PeriodJson(YearId, "2026", "AcademicYear"));
        Register(handler);

        var cut = Render<FluentDialogProvider>();
        var task = DialogService.ShowShellDialogAsync<JoinGroupsDialog, JoinGroupsDialog.JoinGroupsModel, JoinGroupsDialog.JoinGroupsResult>(
            new JoinGroupsDialog.JoinGroupsModel { StudentId = StudentId }, "Join groups", DialogSize.Medium);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Chess Club"));
        cut.Markup.Should().NotContain("Semester Band", "a Semester group is not joinable while a Term is active (AC-35)");

        // Cleanup: close the dialog.
        cut.Find("fluent-button[aria-label='Close']").Click();
        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().BeNull("closing the dialog yields no result");
    }

    /// <summary>
    /// Regression (round fluentui-dead-binding): the group picker bound the dead
    /// <c>SelectedValues</c> pair — absent from FluentUI 4.14.2 list components — so
    /// Blazor dropped it into the catch-all <c>AdditionalAttributes</c> (a stray
    /// <c>selectedvalues</c> HTML attribute) and the picker reported no selection, which
    /// left <c>SubmitAsync</c>'s "select at least one group" guard unsatisfiable. The
    /// supported control is the multi-select FluentSelect.
    /// </summary>
    [TestMethod]
    public async Task JoinDialog_GroupPicker_BindsTheSupportedApi_AndSubmitsTheSelectedGroups()
    {
        var handler = new ScriptedHandler();
        handler.Map("GET", "/activity-groups", HttpStatusCode.OK,
            $"[{GroupJson(OpenGroupId, "Chess Club", "OpenEnded")}]");
        handler.Map("GET", $"/students/{StudentId}/activity-groups", HttpStatusCode.OK, "[]");
        handler.Map("GET", "/students/periods/active-sub-period", HttpStatusCode.NotFound, "{}");
        handler.Map("GET", "/students/periods/active-academic-year", HttpStatusCode.NotFound, "{}");
        handler.Map("POST", $"/activity-groups/{OpenGroupId}/members", HttpStatusCode.Created, "{}");
        Register(handler);

        var cut = Render<FluentDialogProvider>();
        var model = new JoinGroupsDialog.JoinGroupsModel { StudentId = StudentId };
        var task = DialogService.ShowShellDialogAsync<JoinGroupsDialog, JoinGroupsDialog.JoinGroupsModel, JoinGroupsDialog.JoinGroupsResult>(
            model, "Join groups", DialogSize.Medium);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Chess Club"));

        cut.Markup.Should().NotContain("selectedvalues",
            "the dead SelectedValues binding must not leak into the DOM");
        cut.FindAll("fluent-listbox").Should().BeEmpty(
            "the supported control is the multi-select FluentSelect (the skills' multi-select route)");

        var picker = cut.FindComponent<FluentSelect<ActivityGroupDto>>();
        picker.Instance.Multiple.Should().BeTrue();
        cut.FindAll("fluent-option").Should().ContainSingle("the available group is offered by the picker");

        await cut.InvokeAsync(() => picker.Instance.SelectedOptionsChanged.InvokeAsync(
            picker.Instance.Items!.ToArray()));

        model.SelectedGroupIds.Should().BeEquivalentTo(new[] { OpenGroupId },
            "the picker's callback writes the picked ids back to the single source of truth");

        cut.Find("form").Submit();

        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().NotBeNull("the picker's selection must satisfy the join validation");
        result!.GroupIds.Should().Equal(OpenGroupId);
        handler.Calls.Should().Contain($"/activity-groups/{OpenGroupId}/members",
            "the submitted selection is joined");
    }

    /// <summary>
    /// Regression (round fluentui-dead-binding, option (a)): the picker renders only the
    /// search-filtered <c>Items</c> and reports only what it renders. A group picked before
    /// the user types a search that hides it must survive the next selection change — the
    /// merge in <c>OnSelectedGroupsChanged</c> keeps the ids that are not currently rendered
    /// and unions them with the incoming selection, which is correct both when FluentSelect
    /// preserves the passed selection and when it re-derives it from the rendered options.
    /// </summary>
    [TestMethod]
    public async Task JoinDialog_SearchFiltersOutSelectedGroup_SelectionOfHiddenGroupSurvives()
    {
        var handler = new ScriptedHandler();
        handler.Map("GET", "/activity-groups", HttpStatusCode.OK,
            $"[{GroupJson(OpenGroupId, "Chess Club", "OpenEnded")},{GroupJson(TermGroupId, "Robotics Club", "OpenEnded")}]");
        handler.Map("GET", $"/students/{StudentId}/activity-groups", HttpStatusCode.OK, "[]");
        handler.Map("GET", "/students/periods/active-sub-period", HttpStatusCode.NotFound, "{}");
        handler.Map("GET", "/students/periods/active-academic-year", HttpStatusCode.NotFound, "{}");
        Register(handler);

        var cut = Render<FluentDialogProvider>();
        var model = new JoinGroupsDialog.JoinGroupsModel { StudentId = StudentId };
        var task = DialogService.ShowShellDialogAsync<JoinGroupsDialog, JoinGroupsDialog.JoinGroupsModel, JoinGroupsDialog.JoinGroupsResult>(
            model, "Join groups", DialogSize.Medium);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Chess Club"));

        // Pick group A ("Chess Club") from the unfiltered list.
        var picker = cut.FindComponent<FluentSelect<ActivityGroupDto>>();
        await cut.InvokeAsync(() => picker.Instance.SelectedOptionsChanged.InvokeAsync(
            picker.Instance.Items!.Where(g => g.Id == OpenGroupId).ToArray()));
        model.SelectedGroupIds.Should().BeEquivalentTo(new[] { OpenGroupId });

        // Search text that keeps "Chess Club" out of the rendered Items. The search box is
        // not Immediate, so the browser's change event (blur) is what drives @bind-Value.
        await cut.InvokeAsync(() => cut.Find("fluent-text-field").ChangeAsync("Robotics"));

        cut.FindAll("fluent-option").Should().ContainSingle(
            "the active search renders only the matching group");
        cut.Markup.Should().NotContain("Chess Club",
            "the selected group is no longer part of the options FluentSelect renders");

        // Fire a selection change against the filtered list, picking the visible group B.
        var filteredPicker = cut.FindComponent<FluentSelect<ActivityGroupDto>>();
        await cut.InvokeAsync(() => filteredPicker.Instance.SelectedOptionsChanged.InvokeAsync(
            filteredPicker.Instance.Items!.ToArray()));

        model.SelectedGroupIds.Should().BeEquivalentTo(new[] { OpenGroupId, TermGroupId },
            "the group hidden by the search keeps its place alongside the newly picked one");

        // Clearing the visible selection must leave the hidden pick untouched: the user
        // cannot deselect a group the search is currently hiding.
        await cut.InvokeAsync(() => filteredPicker.Instance.SelectedOptionsChanged.InvokeAsync(
            Array.Empty<ActivityGroupDto>()));

        model.SelectedGroupIds.Should().BeEquivalentTo(new[] { OpenGroupId },
            "only groups the user can see can be deselected");

        cut.Find("fluent-button[aria-label='Close']").Click();
        (await task.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeNull(
            "the dialog is closed without joining");
    }
}
