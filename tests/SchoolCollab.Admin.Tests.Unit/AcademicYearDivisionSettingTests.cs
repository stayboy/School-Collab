using System.Net;
using System.Text;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Core.Features;
using SchoolCollab.Settings.Application.Components.Pages;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for the academic-year division setting card on
/// <c>ConfigFlagDetail</c> (Sprint 6 §6.3b, FR-H6/H7). Locks the effective
/// value/source rendering (T7a) and the FR-H7 framework-switch rejection
/// messaging (T7b) per plan-ui-sprint6-r2.md §5.3, plus the card-isolation
/// rule (criterion 5: the card renders ONLY for the division flag).
/// </summary>
[TestClass]
public class AcademicYearDivisionSettingTests : BunitContext
{
    private const string RejectionMessage =
        "Cannot change academic-year division from 'Terms' to 'Semesters': 3 sub-period(s) still exist. " +
        "Complete or remove them first.";

    public AcademicYearDivisionSettingTests()
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
                return new HttpResponseMessage(exact.Status)
                {
                    Content = new StringContent(exact.Body, Encoding.UTF8, "application/json"),
                };

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent($"Unexpected {url}", Encoding.UTF8, "application/json"),
            };
        }
    }

    private ScriptedHandler Register()
    {
        var handler = new ScriptedHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };
        Services.AddSingleton(new ConfigFlagsApiClient(http));
        return handler;
    }

    private static string FlagJson() =>
        "{\"id\":\"" + Guid.NewGuid() + "\",\"key\":\"FEATURE:AcademicYearDivision\",\"name\":\"Academic year division\"," +
        "\"description\":null,\"kind\":\"String\",\"value\":\"Terms\",\"isEnabled\":true,\"isArchived\":false," +
        "\"isDeleted\":false,\"overrideCount\":0,\"createdAt\":\"2026-01-01T00:00:00Z\",\"updatedAt\":\"2026-01-01T00:00:00Z\"}";

    /// <summary>Scripts the four GETs the detail page performs on load.</summary>
    private static void ScriptDivisionLoad(ScriptedHandler handler, string divisionBody)
    {
        handler.Map("GET", "/api/config/flags/FEATURE%3AAcademicYearDivision", HttpStatusCode.OK, FlagJson());
        handler.Map("GET", "/api/config/flags/FEATURE%3AAcademicYearDivision/overrides", HttpStatusCode.OK, "[]");
        handler.Map("GET", "/api/config/audit?key=FEATURE%3AAcademicYearDivision&skip=0&take=50", HttpStatusCode.OK, "[]");
        handler.Map("GET", "/api/config/flags/academic_year_division", HttpStatusCode.OK, divisionBody);
    }

    /// <summary>
    /// T7a: the division card shows the current effective value and its source.
    /// </summary>
    [TestMethod]
    public void DivisionSetting_CardShowsEffectiveValueAndSource()
    {
        var handler = Register();
        ScriptDivisionLoad(handler, "{\"value\":\"Terms\",\"source\":\"TenantOverride\"}");

        var cut = Render<ConfigFlagDetail>(p => p.Add(x => x.Key, FeatureFlagKeys.AcademicYearDivision));

        cut.WaitForState(() => cut.Markup.Contains("Academic-year division"), TimeSpan.FromSeconds(5));
        cut.Markup.Should().Contain("Terms", "the current effective value is shown");
        cut.Markup.Should().Contain("TenantOverride", "the setting source is shown");
        cut.Markup.Should().Contain("Default state", "the generic flag UI still renders");
    }

    /// <summary>
    /// T7b: switching the division while sub-periods exist surfaces the
    /// server's FR-H7 rejection message verbatim in the error message bar.
    /// </summary>
    [TestMethod]
    public async Task DivisionSetting_SwitchRejection_ShowsServerMessage()
    {
        var handler = Register();
        ScriptDivisionLoad(handler, "{\"value\":\"Terms\",\"source\":\"TenantOverride\"}");
        handler.Map("PUT", "/api/config/flags/academic_year_division", HttpStatusCode.UnprocessableContent,
            "{\"message\":\"" + RejectionMessage + "\"}");

        var cut = Render<ConfigFlagDetail>(p => p.Add(x => x.Key, FeatureFlagKeys.AcademicYearDivision));
        cut.WaitForState(() => cut.Markup.Contains("Academic-year division"), TimeSpan.FromSeconds(5));

        var card = cut.FindComponents<FluentCard>()
            .First(c => c.Markup.Contains("Academic-year division"));

        // Save is disabled until a reason is supplied.
        card.Find("fluent-button").HasAttribute("disabled").Should()
            .BeTrue("the Save button is disabled without a reason");

        // Drive the division select (bound @bind-Value="_divisionSelect").
        var select = card.FindComponents<FluentSelect<string>>().First(s => s.Instance.Label == "Division");
        await cut.InvokeAsync(() => select.Instance.ValueChanged.InvokeAsync("Semesters"));

        // Drive the reason text field (bound @bind-Value="_divisionReason").
        var reason = card.FindComponent<FluentTextField>();
        await cut.InvokeAsync(() => reason.Instance.ValueChanged.InvokeAsync("consolidating to semesters"));

        card.Find("fluent-button").HasAttribute("disabled").Should()
            .BeFalse("the Save button enables once a reason is supplied");
        card.Find("fluent-button").Click();

        cut.WaitForState(() => cut.Markup.Contains("Cannot change academic-year division"),
            TimeSpan.FromSeconds(5));
        cut.Markup.Should().Contain(RejectionMessage,
            "the FR-H7 rejection message must reach the UI verbatim");

        handler.Calls.Should().Contain(c => c.Method == "PUT" && c.Url == "/api/config/flags/academic_year_division");
        var put = handler.Calls.Single(c => c.Method == "PUT");
        put.Body.Should().Contain("\"value\":\"Semesters\"");
        put.Body.Should().Contain("\"reason\":\"consolidating to semesters\"");
    }

    /// <summary>
    /// Criterion 5 (card isolation): a non-division string flag must NOT render
    /// the division card and must not query the division endpoint.
    /// </summary>
    [TestMethod]
    public void DivisionSetting_CardNotRenderedForOtherStringFlags()
    {
        var handler = Register();
        var flag = FlagJson().Replace("FEATURE:AcademicYearDivision", "FEATURE:SomeOtherStringFlag");
        handler.Map("GET", "/api/config/flags/FEATURE%3ASomeOtherStringFlag", HttpStatusCode.OK, flag);
        handler.Map("GET", "/api/config/flags/FEATURE%3ASomeOtherStringFlag/overrides", HttpStatusCode.OK, "[]");
        handler.Map("GET", "/api/config/audit?key=FEATURE%3ASomeOtherStringFlag&skip=0&take=50", HttpStatusCode.OK, "[]");

        var cut = Render<ConfigFlagDetail>(p => p.Add(x => x.Key, "FEATURE:SomeOtherStringFlag"));

        cut.WaitForState(() => cut.Markup.Contains("Default state"), TimeSpan.FromSeconds(5));
        cut.Markup.Should().NotContain("Academic-year division",
            "the division card renders only for the division flag");
        handler.Calls.Should().NotContain(c => c.Url.Contains("academic_year_division"),
            "the division endpoint is only queried for the division flag");
    }
}