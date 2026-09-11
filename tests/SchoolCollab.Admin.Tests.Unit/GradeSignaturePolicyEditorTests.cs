using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Students.Application.Components.Students;
using SchoolCollab.Students.Application.Services;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for the per-grade Guardian Signature policy editor
/// (GradeSignaturePolicyEditor.razor, WS-C1 / spec §7 Q1). Covers the
/// tenant-global default switch, the tri-state grade override select,
/// immediate-save behaviour, and load-failure surfacing.
/// </summary>
[TestClass]
public class GradeSignaturePolicyEditorTests : BunitContext
{
    public GradeSignaturePolicyEditorTests()
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
            var key = (request.Method.Method.ToUpperInvariant(), request.RequestUri.PathAndQuery);
            if (_responses.TryGetValue(key, out var hit))
                return new HttpResponseMessage(hit.Status) { Content = new StringContent(hit.Body, Encoding.UTF8, "application/json") };
            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent($"Unexpected URL: {request.Method.Method} {request.RequestUri.PathAndQuery}", Encoding.UTF8, "application/json"),
            };
        }
    }

    private static string TenantJson(bool requiresSignatureDefault) =>
        JsonSerializer.Serialize(new { requiresSignatureDefault });

    private static string GradeJson(Guid gradeLevelId, bool? requiresSignatureDefault) =>
        JsonSerializer.Serialize(new
        {
            gradeLevelId,
            requiresSignatureDefault,
            updatedAt = DateTimeOffset.UnixEpoch,
        });

    private (ScriptedHandler Handler, Guid GradeId) Register(
        bool tenantDefault = false,
        bool? gradeOverride = null,
        bool gradeHasRow = true,
        HttpStatusCode tenantStatus = HttpStatusCode.OK)
    {
        var gradeId = Guid.NewGuid();
        var handler = new ScriptedHandler();
        handler.Map("GET", "/api/settings/assignment-policy", tenantStatus, TenantJson(tenantDefault));
        handler.Map(
            "GET",
            $"/students/grade-levels/{gradeId}/assignment-policy",
            gradeHasRow ? HttpStatusCode.OK : HttpStatusCode.NoContent,
            gradeHasRow && gradeOverride.HasValue ? GradeJson(gradeId, gradeOverride) : "");
        handler.Map("PUT", "/api/settings/assignment-policy", HttpStatusCode.OK, "");
        handler.Map("PUT", $"/students/grade-levels/{gradeId}/assignment-policy", HttpStatusCode.OK, "");

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        var codedValues = new CodedValuesApiClient(http);
        Services.AddSingleton(codedValues);
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, codedValues));
        Services.AddSingleton(new AssignmentPolicyApiClient(http));
        Services.AddSingleton<ILogger<GradeSignaturePolicyEditor>>(NullLogger<GradeSignaturePolicyEditor>.Instance);

        return (handler, gradeId);
    }

    [TestMethod]
    public void Editor_Loads_ShowsGlobalDefaultAndInheritBadge()
    {
        var (_, gradeId) = Register(tenantDefault: true, gradeHasRow: false);

        var cut = Render<GradeSignaturePolicyEditor>(p => p.Add(x => x.GradeLevelId, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Required by default (tenant)"));

        cut.Markup.Should().Contain("Inherit global", "no grade override shows the inherit badge");
    }

    [TestMethod]
    public async Task Editor_ToggleGlobalSwitch_PutsTenantPolicy()
    {
        var (handler, gradeId) = Register(tenantDefault: false, gradeHasRow: false);

        var cut = Render<GradeSignaturePolicyEditor>(p => p.Add(x => x.GradeLevelId, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Required by default (tenant)"));

        var toggle = cut.FindComponent<FluentSwitch>();
        await cut.InvokeAsync(() => toggle.Instance.ValueChanged.InvokeAsync(true));

        handler.Calls.Should().Contain(c =>
            c.Method == "PUT" &&
            c.Url == "/api/settings/assignment-policy" &&
            c.Body == "{\"requiresSignatureDefault\":true}",
            "toggling the switch on saves the tenant default as true");
    }

    [TestMethod]
    public async Task Editor_SelectGradeOverride_PutsGradePolicy()
    {
        var (handler, gradeId) = Register(tenantDefault: false, gradeHasRow: false);

        var cut = Render<GradeSignaturePolicyEditor>(p => p.Add(x => x.GradeLevelId, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Required by default (tenant)"));

        var select = cut.FindComponent<FluentSelect<GradeSignaturePolicyEditor.OverrideOption>>();
        await cut.InvokeAsync(() => select.Instance.SelectedOptionChanged.InvokeAsync(
            new GradeSignaturePolicyEditor.OverrideOption("require", "Require signature")));

        handler.Calls.Should().Contain(c =>
            c.Method == "PUT" &&
            c.Url == $"/students/grade-levels/{gradeId}/assignment-policy" &&
            c.Body == "{\"requiresSignatureDefault\":true}",
            "selecting 'Require signature' saves the grade override as true");
    }

    [TestMethod]
    public async Task Editor_ResetToInherit_PutsNullOverride()
    {
        var (handler, gradeId) = Register(tenantDefault: false, gradeOverride: true, gradeHasRow: true);

        var cut = Render<GradeSignaturePolicyEditor>(p => p.Add(x => x.GradeLevelId, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Required by default (tenant)"));

        var select = cut.FindComponent<FluentSelect<GradeSignaturePolicyEditor.OverrideOption>>();
        await cut.InvokeAsync(() => select.Instance.SelectedOptionChanged.InvokeAsync(
            new GradeSignaturePolicyEditor.OverrideOption("inherit", "Inherit global")));

        handler.Calls.Should().Contain(c =>
            c.Method == "PUT" &&
            c.Url == $"/students/grade-levels/{gradeId}/assignment-policy" &&
            c.Body == "{\"requiresSignatureDefault\":null}",
            "selecting 'Inherit global' saves a null grade override");
    }

    [TestMethod]
    public void Editor_LoadFails_ShowsError()
    {
        var (_, gradeId) = Register(tenantStatus: HttpStatusCode.InternalServerError);

        var cut = Render<GradeSignaturePolicyEditor>(p => p.Add(x => x.GradeLevelId, gradeId));
        // The editor renders the raw exception message (_error = ex.Message,
        // the GradeNotificationPolicyEditor precedent), so assert the error
        // message bar surfaces rather than a specific friendly string.
        cut.WaitForAssertion(() =>
        {
            cut.FindComponents<FluentMessageBar>()
                .Should().Contain(mb => mb.Instance.Intent == MessageIntent.Error,
                    "a load failure surfaces a page-level error message bar");
        }, TimeSpan.FromSeconds(5));
    }
}
