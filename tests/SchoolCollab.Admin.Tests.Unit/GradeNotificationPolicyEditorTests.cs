using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Students.Application.Components.Students;
using SchoolCollab.Students.Application.Services;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for the per-grade Notification &amp; Delivery editor
/// (notification-delivery-plan.md §5): effective-policy view with per-field
/// "uses global default" / "Override" indicators, and the override set/clear
/// round-trip against the scripted Students + Settings APIs.
/// </summary>
[TestClass]
public class GradeNotificationPolicyEditorTests : BunitContext
{
    public GradeNotificationPolicyEditorTests()
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

    private (ScriptedHandler Handler, Guid GradeId) Register(string tenantBody, string gradeBody, HttpStatusCode gradeStatus = HttpStatusCode.OK)
    {
        var gradeId = Guid.NewGuid();
        var handler = new ScriptedHandler();
        handler.Map("GET", "/api/settings/notification-policy",
            string.IsNullOrEmpty(tenantBody) ? HttpStatusCode.NoContent : HttpStatusCode.OK, tenantBody);
        handler.Map("GET", $"/students/grade-levels/{gradeId}/notification-policy", gradeStatus, gradeBody);
        handler.Map("PUT", $"/students/grade-levels/{gradeId}/notification-policy", HttpStatusCode.OK, gradeBody);

        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        Services.AddSingleton(new NotificationPolicyApiClient(http));
        var codedValues = new CodedValuesApiClient(http);
        Services.AddSingleton(codedValues);
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, codedValues));

        return (handler, gradeId);
    }

    private const string NoContent = "";

    private static string TenantJson(int? maxNotifications, int? linkValidityDays, int[] preferredChannels) =>
        Json(new Dictionary<string, object?>
        {
            ["id"] = Guid.NewGuid(),
            ["preferredChannelOrder"] = preferredChannels,
            ["blockedChannels"] = Array.Empty<int>(),
            ["maxNotifications"] = maxNotifications,
            ["maxReminders"] = null,
            ["reminderIntervalHours"] = null,
            ["linkValidityDays"] = linkValidityDays,
            ["sendoutTimeOfDay"] = null,
            ["sendoutIntervalMinutes"] = null,
            ["updatedAt"] = System.DateTimeOffset.UnixEpoch,
        });

    private static string GradeJson(int? maxNotifications, int[]? preferredChannels) =>
        Json(new Dictionary<string, object?>
        {
            ["gradeLevelId"] = Guid.NewGuid(),
            ["preferredChannelOrder"] = preferredChannels,
            ["blockedChannels"] = Array.Empty<int>(),
            ["maxNotifications"] = maxNotifications,
            ["maxReminders"] = null,
            ["reminderIntervalHours"] = null,
            ["linkValidityDays"] = null,
            ["sendoutTimeOfDay"] = null,
            ["sendoutIntervalMinutes"] = null,
            ["updatedAt"] = System.DateTimeOffset.UnixEpoch,
        });

    private static string Json(Dictionary<string, object?> dict) =>
        System.Text.Json.JsonSerializer.Serialize(dict);

    [TestMethod]
    public void Effective_policy_shows_override_and_global_default_indicators()
    {
        var (_, gradeId) = Register(
            tenantBody: TenantJson(maxNotifications: 50, linkValidityDays: 7, preferredChannels: [0]),
            gradeBody: GradeJson(maxNotifications: 10, preferredChannels: null));

        var cut = Render<GradeNotificationPolicyEditor>(p => p.Add(x => x.GradeLevelId, gradeId));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Effective Policy"));
        // Grade override wins for max notifications.
        cut.Markup.Should().Contain("10");
        cut.Markup.Should().Contain("Override");
        // Link validity inherits the tenant default.
        cut.Markup.Should().Contain("Global default");
        cut.Markup.Should().Contain("7");
        // Tenant default reference shows 50.
        cut.Markup.Should().Contain("Tenant Global Default");
        cut.Markup.Should().Contain("50");
        // Channel display.
        cut.Markup.Should().Contain("Email");
    }

    [TestMethod]
    public void Effective_policy_shows_not_set_when_no_configuration()
    {
        var (_, gradeId) = Register(
            tenantBody: NoContent,
            gradeBody: NoContent,
            gradeStatus: HttpStatusCode.NoContent);

        var cut = Render<GradeNotificationPolicyEditor>(p => p.Add(x => x.GradeLevelId, gradeId));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Effective Policy"));
        cut.Markup.Should().Contain("Not set");
        cut.Markup.Should().NotContain("Global default");
    }

    [TestMethod]
    public void Save_roundtrips_a_grade_override()
    {
        var (handler, gradeId) = Register(
            tenantBody: TenantJson(maxNotifications: 50, linkValidityDays: null, preferredChannels: []),
            gradeBody: NoContent,
            gradeStatus: HttpStatusCode.NoContent);

        var cut = Render<GradeNotificationPolicyEditor>(p => p.Add(x => x.GradeLevelId, gradeId));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Save overrides"));

        // Turn on the "Max notifications per sendout" override and set it to 10.
        var maxRow = cut.FindAll(".override-row").First(r => r.TextContent.Contains("Max notifications"));
        var checkboxInput = maxRow.QuerySelector("input[type=checkbox]");
        checkboxInput.Should().NotBeNull();
        checkboxInput!.Change(true);

        cut.WaitForAssertion(() => cut.FindAll(".override-row").First(r => r.TextContent.Contains("Max notifications"))
            .QuerySelector("input[type=number]")!.HasAttribute("disabled").Should().BeFalse());
        var numberInput = cut.FindAll(".override-row").First(r => r.TextContent.Contains("Max notifications"))
            .QuerySelector("input[type=number]");
        numberInput.Should().NotBeNull();
        numberInput!.Change("10");

        cut.FindAll("fluent-button").First(b => b.TextContent.Contains("Save overrides")).Click();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("10"));

        var put = handler.Calls.Should().Contain(c => c.Method == "PUT" && c.Url.Contains("notification-policy")).Which;
        put.Body.Should().Contain("\"maxNotifications\":10");
    }
}
