using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components.Dialogs;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Students.Application.Components.Students;
using SchoolCollab.Students.Application.Services;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit coverage for the student rail's activity-group picker (#419). The picker's behaviour was
/// verified live only — struck-through blocked rows, add → chip → remove — so every assertion here had
/// only a human's eye behind it until now.
///
/// <para>What the picker promises, and what this suite pins:</para>
/// <list type="bullet">
///   <item>a blocked row is NOT selectable: it renders no checkbox at all, and the row carries the
///         reason the mirror refused it for (owner Q18 — a hidden group reads as a bug, a struck-through
///         one with its reason reads as a rule);</item>
///   <item>picking an available row is what enables Add (the footer is disabled on an empty pick);</item>
///   <item>the dialog hands back exactly the picked ids — the PAGE writes the memberships, so a partial
///         failure cannot leave a closed dialog claiming success;</item>
///   <item>a FAILED load is not an empty tenant: the picker says it could not read the list instead of
///         "No activity groups exist yet", and never shows the exception's own text (the #416 seam).</item>
/// </list>
/// </summary>
[TestClass]
public class ActivityGroupPickerDialogTests : BunitContext
{
    private static readonly Guid StudentId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly Guid GradeId = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid OtherGradeId = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid ChessClubId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SwitchedOffId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid WrongGradeId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private IDialogService DialogService => Services.GetRequiredService<IDialogService>();

    public ActivityGroupPickerDialogTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
        Services.AddSingleton<ILogger<ActivityGroupPickerDialog>>(
            NullLogger<ActivityGroupPickerDialog>.Instance);
    }

    /// <summary>A scripted Students API: exact (method, url) answers, and every unmapped route 404s.</summary>
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public readonly List<(string Method, string Url)> Calls = new();
        private readonly Dictionary<(string Method, string Url), (HttpStatusCode Status, string Body)> _responses = new();

        public ScriptedHandler Map(string method, string url, HttpStatusCode status, string body)
        {
            _responses[(method.ToUpperInvariant(), url)] = (status, body);
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.PathAndQuery;
            Calls.Add((request.Method.Method.ToUpperInvariant(), url));

            if (_responses.TryGetValue((request.Method.Method.ToUpperInvariant(), url), out var hit))
            {
                return Task.FromResult(new HttpResponseMessage(hit.Status)
                {
                    Content = new StringContent(hit.Body, Encoding.UTF8, "application/json"),
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent($"Unexpected {request.Method.Method} {url}", Encoding.UTF8, "application/json"),
            });
        }
    }

    private void Register(ScriptedHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        var codedValues = new CodedValuesApiClient(http);
        Services.AddSingleton(codedValues);
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, codedValues));
    }

    /// <summary>
    /// One activity-group row as the API serves it. <c>eligibleGradeIds</c> empty means "any grade"
    /// (repository contract, FR-40), which is what makes the Chess Club addable.
    /// </summary>
    private static string GroupJson(
        Guid id,
        string name,
        bool isActive = true,
        Guid[]? eligibleGradeIds = null,
        int? capacity = null,
        int activeMemberCount = 0) =>
        $"{{\"id\":\"{id:D}\",\"name\":\"{name}\",\"description\":null,\"category\":null,"
        + $"\"capacity\":{(capacity is null ? "null" : capacity.Value.ToString())},"
        + $"\"isActive\":{isActive.ToString().ToLowerInvariant()},\"span\":\"OpenEnded\","
        + "\"enrollmentStartDate\":null,\"enrollmentEndDate\":null,\"autoRenewDefault\":true,"
        + $"\"eligibleGradeIds\":[{(eligibleGradeIds ?? []).Aggregate(string.Empty, (acc, g) => acc + (acc.Length == 0 ? string.Empty : ",") + $"\"{g:D}\"")}],"
        + $"\"activeMemberCount\":{activeMemberCount},"
        + "\"createdAt\":\"2026-01-01T00:00:00Z\",\"updatedAt\":\"2026-01-01T00:00:00Z\"}";

    /// <summary>An available group, a switched-off one, and one offered only to another grade.</summary>
    private static string MixedGroupsJson() =>
        "["
        + GroupJson(ChessClubId, "Chess Club")
        + ","
        + GroupJson(SwitchedOffId, "Robotics Club", isActive: false)
        + ","
        + GroupJson(WrongGradeId, "Semester Band", eligibleGradeIds: [OtherGradeId])
        + "]";

    /// <summary>Opens the picker through the real dialog pipeline and returns the rendered provider.</summary>
    private IRenderedComponent<FluentDialogProvider> OpenPicker(
        ScriptedHandler handler,
        out Task<Guid[]?> result)
    {
        Register(handler);
        var cut = Render<FluentDialogProvider>();
        result = DialogService.ShowShellDialogAsync<ActivityGroupPickerDialog, ActivityGroupPickerModel, Guid[]>(
            new ActivityGroupPickerModel(StudentId, GradeId), "Add activity groups", DialogWidth.Medium);
        return cut;
    }

    /// <summary>The footer's Add button — the one control whose state the pick gates.</summary>
    private static AngleSharp.Dom.IElement AddButton(IRenderedComponent<FluentDialogProvider> cut) =>
        cut.FindAll("fluent-button").Single(b => b.TextContent.Trim() == "Add");

    /// <summary>
    /// (a) A blocked row is not selectable: no checkbox exists in it, and its reason is in the visible
    /// text. Both halves matter — an unselectable row with no reason reads as a bug (owner Q18).
    /// </summary>
    [TestMethod]
    public void BlockedRows_AreNotSelectable_AndCarryTheirReason()
    {
        var handler = new ScriptedHandler()
            .Map("GET", "/activity-groups", HttpStatusCode.OK, MixedGroupsJson())
            .Map("GET", $"/students/{StudentId}/activity-groups", HttpStatusCode.OK, "[]");

        var cut = OpenPicker(handler, out _);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Chess Club"));

        var blocked = cut.FindAll("li.picker-row--blocked");
        blocked.Should().HaveCount(2, "the switched-off group and the one offered to another grade");

        foreach (var row in blocked)
        {
            row.QuerySelectorAll("fluent-checkbox").Should().BeEmpty(
                "a blocked option must not be selectable at all — not merely refused on submit");
        }

        cut.Markup.Should().Contain("Group is switched off",
            "the mirror's own reason is shown beside the struck-through row");
        cut.Markup.Should().Contain("Not offered to this grade");

        // The available row is the only selectable one.
        cut.FindAll("fluent-checkbox").Should().ContainSingle("only Chess Club can be picked");
    }

    /// <summary>
    /// (b) Picking an available row is what enables Add: the footer is disabled on an empty pick, and
    /// nothing a blocked row can do changes that.
    /// </summary>
    [TestMethod]
    public async Task SelectingAnAvailableRow_EnablesAdd()
    {
        var handler = new ScriptedHandler()
            .Map("GET", "/activity-groups", HttpStatusCode.OK, MixedGroupsJson())
            .Map("GET", $"/students/{StudentId}/activity-groups", HttpStatusCode.OK, "[]");

        var cut = OpenPicker(handler, out _);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Chess Club"));

        AddButton(cut).GetAttribute("disabled").Should().NotBeNull(
            "an empty pick is not a submit — the page would write memberships for nothing");

        var checkbox = cut.FindComponents<FluentCheckbox>().Single();
        await cut.InvokeAsync(() => checkbox.Instance.ValueChanged.InvokeAsync(true));

        cut.WaitForAssertion(() => AddButton(cut).GetAttribute("disabled").Should().BeNull(
            "picking an available row is the one thing that arms Add"));
    }

    /// <summary>
    /// (c) The dialog returns the picked ids — and only those. The page performs the writes, so this
    /// result is the whole contract between the picker and the caller.
    /// </summary>
    [TestMethod]
    public async Task Submitting_ReturnsTheSelectedIds()
    {
        var handler = new ScriptedHandler()
            .Map("GET", "/activity-groups", HttpStatusCode.OK, MixedGroupsJson())
            .Map("GET", $"/students/{StudentId}/activity-groups", HttpStatusCode.OK, "[]");

        var cut = OpenPicker(handler, out var result);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Chess Club"));

        var checkbox = cut.FindComponents<FluentCheckbox>().Single();
        await cut.InvokeAsync(() => checkbox.Instance.ValueChanged.InvokeAsync(true));

        cut.Find("form").Submit();

        var ids = await result.WaitAsync(TimeSpan.FromSeconds(5));

        ids.Should().NotBeNull("a pick closes the dialog with a result");
        ids.Should().Equal(new[] { ChessClubId }, "exactly the available group the operator picked");
        handler.Calls.Should().NotContain(c => c.Method == "POST",
            "the PICKER never writes a membership — the page does, one call per id");
    }

    /// <summary>
    /// (d) A FAILED load is not an empty tenant. The list read fails, so the picker must say it could not
    /// be read — never "No activity groups exist yet" beside an error — and it must not echo the
    /// exception's own text (the #416 seam; the raw text lives in the log).
    /// </summary>
    [TestMethod]
    public void FailedLoad_SaysItCouldNotBeRead_NotThatTheTenantHasNone()
    {
        var handler = new ScriptedHandler()
            .Map("GET", "/activity-groups", HttpStatusCode.InternalServerError, "boom")
            .Map("GET", $"/students/{StudentId}/activity-groups", HttpStatusCode.InternalServerError, "boom");

        var cut = OpenPicker(handler, out _);

        cut.WaitForAssertion(() => cut.Markup.Should()
            .Contain("The activity groups could not be loaded. Close this dialog and try again.",
                "a failed load is a failure, stated in the picker's own words"));

        cut.Markup.Should().NotContain("No activity groups exist yet",
            "the empty state means 'this tenant has none', which is a different fact from 'the read failed'");

        // The #416 assertions read the ERROR BAR's text, never the whole markup: every
        // blazor:elementReference in bUnit markup is a random GUID, and a GUID contains "500" often
        // enough to redden this test roughly one run in five. Positive first, so a selector that
        // resolved to the wrong bar cannot satisfy the two negatives while asserting nothing.
        var errorBar = cut.Find(".activity-group-picker fluent-message-bar[intent='error']").TextContent;

        errorBar.Should().Contain("The activity groups could not be loaded.",
            "the scoped element is the picker's own failure bar, not just any message bar");
        errorBar.Should().NotContain("Response status code",
            "the exception's own text belongs in the log, never on screen (#416)");
        errorBar.Should().NotContain("500",
            "and neither does the status code it carried");
    }
}
