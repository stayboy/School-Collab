using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Students.Admin.Components.Pages.Students.GradeLevels;
using SchoolCollab.Students.Admin.Services;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for the Grade Levels landing page now that PR #94's
/// validation rules (MinAge / MaxAge / AllowedGenderCodedValueId) are
/// surfaced as columns. The page must:
///   - Render an "Age range" column showing "min–max" / "min+" / "≤max" /
///     "Any age" muted placeholder.
///   - Render a "Gender" column showing the tenant-resolved gender name
///     (or "Co-ed" muted when no restriction is set).
///   - Batch-load the gender names via <c>GET /api/coded-values/by-ids</c>
///     rather than one <c>GET /api/coded-values/{id}</c> per row.
/// The scriptable <see cref="ScriptedHandler"/> lets each test pin the exact
/// bytes the API returns so the assertions are deterministic.
/// </summary>
[TestClass]
public class GradeLevelsLandingValidationTests : BunitContext
{
    public GradeLevelsLandingValidationTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    /// <summary>
    /// Tiny HttpMessageHandler that answers any URL with a configured JSON
    /// body + status. Captures the request history so tests can assert on
    /// exactly which URLs the page hit (e.g. that the gender lookup batched
    /// the ids via <c>/api/coded-values/by-ids</c>).
    /// </summary>
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public readonly List<string> Urls = new();
        private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _responses = new(StringComparer.OrdinalIgnoreCase);

        public ScriptedHandler Map(string url, HttpStatusCode status, string body)
        {
            _responses[url] = (status, body);
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.PathAndQuery);
            // Match by path-and-query first; fall back to a wildcard match
            // (any path-and-query starting with the configured prefix) so a
            // single registration can cover "by-ids?id=..." style queries.
            var url = request.RequestUri.PathAndQuery;
            foreach (var kv in _responses)
            {
                if (url.Equals(kv.Key, StringComparison.OrdinalIgnoreCase) ||
                    url.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(new HttpResponseMessage(kv.Value.Status)
                    {
                        Content = new StringContent(kv.Value.Body, Encoding.UTF8, "application/json"),
                    });
                }
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent($"Unexpected URL: {url}", Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class FakeAuth : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(
                new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                    new[]
                    {
                        new System.Security.Claims.Claim("tenant_id", Guid.NewGuid().ToString()),
                        new System.Security.Claims.Claim("tenant_name", "Hydeson"),
                    },
                    "TestScheme"))));
    }

    private (FakeAuth Auth, ScriptedHandler Handler) RegisterWith(params (string UrlPrefix, HttpStatusCode Status, string Body)[] responses)
    {
        var auth = new FakeAuth();
        var handler = new ScriptedHandler();
        foreach (var (url, status, body) in responses)
        {
            handler.Map(url, status, body);
        }
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };

        Services.AddSingleton<AuthenticationStateProvider>(auth);
        var codedValuesClient = new CodedValuesApiClient(http);
        Services.AddSingleton(codedValuesClient);
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, codedValuesClient));
        Services.AddSingleton(new VisibleTenantService(auth, NullLogger<VisibleTenantService>.Instance));

        return (auth, handler);
    }

    private static string LandingJson(int? minAge, int? maxAge, Guid? allowedGenderId)
        => JsonSerializer.Serialize(new[]
        {
            new Dictionary<string, object?>
            {
                ["Id"] = Guid.NewGuid(),
                ["CodedValueId"] = Guid.NewGuid(),
                ["Level"] = 1,
                ["Name"] = "Grade 1",
                ["DisplayOrder"] = 1,
                ["SubjectCount"] = 0,
                ["StudentCount"] = 0,
                ["CurrentPeriodId"] = null,
                ["CurrentPeriodName"] = null,
                ["CreatedAt"] = DateTimeOffset.UnixEpoch,
                ["UpdatedAt"] = DateTimeOffset.UnixEpoch,
                ["MinAge"] = minAge,
                ["MaxAge"] = maxAge,
                ["AllowedGenderCodedValueId"] = allowedGenderId,
            }
        });

    private static string GenderNamesJson(Guid id, string name)
        => JsonSerializer.Serialize(new[]
    {
        new { Id = id, Name = name, Code = "MALE", DisplayOrder = 1, IsOverridden = false, ParentCode = "GENDER", ParentId = Guid.NewGuid(), DefaultName = name }
    });

    [TestMethod]
    public async Task Landing_AgeRangeColumn_Renders_MinMax()
    {
        var genderId = Guid.NewGuid();
        var (_, handler) = RegisterWith(
            ("/students/grade-levels/landing", HttpStatusCode.OK, LandingJson(10, 12, genderId)),
            // Catch-all for /api/coded-values/* (the page hits both /by-parent and /by-ids)
            ("/api/coded-values/", HttpStatusCode.OK, GenderNamesJson(genderId, "Male")));

        var cut = Render<SchoolCollab.Students.Admin.Components.Pages.Students.GradeLevels.Index>();

        // Allow the OnInitializedAsync pipeline + Task.Delay in CodedValueDropdown
        // to complete before we assert.
        cut.WaitForState(() => cut.Markup.Contains("10"), TimeSpan.FromSeconds(2));

        cut.Markup.Should().Contain("Age range");
        // En-dash separator (U+2013) — see FormatAgeRange: $"{min}–{max}".
        cut.Markup.Should().Contain("10–12", "min=10 + max=12 should render as '10–12'");
        // The header was added by this change too.
        cut.Markup.Should().Contain("Gender");
        // The gender lookup should have hit the by-ids batch endpoint.
        handler.Urls.Should().Contain(u => u.Contains("/api/coded-values/by-ids"),
            "gender names must be batch-loaded, not per-row");
    }

    [TestMethod]
    public async Task Landing_AgeRangeColumn_Renders_MinOnly_AsMinPlus()
    {
        var (_, _) = RegisterWith(
            ("/students/grade-levels/landing", HttpStatusCode.OK, LandingJson(10, null, null)),
            ("/api/coded-values/", HttpStatusCode.OK, "[]"));

        var cut = Render<SchoolCollab.Students.Admin.Components.Pages.Students.GradeLevels.Index>();
        cut.WaitForState(() => cut.Markup.Contains("10+"), TimeSpan.FromSeconds(2));

        cut.Markup.Should().Contain("10+", "min-only should render as '10+'");
        cut.Markup.Should().NotContain("Any age");
    }

    [TestMethod]
    public async Task Landing_AgeRangeColumn_Renders_NeitherAsAnyAgeMuted()
    {
        var (_, _) = RegisterWith(
            ("/students/grade-levels/landing", HttpStatusCode.OK, LandingJson(null, null, null)),
            ("/api/coded-values/", HttpStatusCode.OK, "[]"));

        var cut = Render<SchoolCollab.Students.Admin.Components.Pages.Students.GradeLevels.Index>();
        cut.WaitForState(() => cut.Markup.Contains("Any age"), TimeSpan.FromSeconds(2));

        cut.Markup.Should().Contain("Any age");
        cut.Markup.Should().Contain("Co-ed");
    }

    [TestMethod]
    public async Task Landing_GenderColumn_RendersResolvedName()
    {
        var genderId = Guid.NewGuid();
        var (_, _) = RegisterWith(
            ("/students/grade-levels/landing", HttpStatusCode.OK, LandingJson(null, null, genderId)),
            ("/api/coded-values/", HttpStatusCode.OK, GenderNamesJson(genderId, "Female")));

        var cut = Render<SchoolCollab.Students.Admin.Components.Pages.Students.GradeLevels.Index>();
        cut.WaitForState(() => cut.Markup.Contains("Female"), TimeSpan.FromSeconds(2));

        cut.Markup.Should().Contain("Female", "the gender id must resolve to the tenant-displayed name");
    }
}