using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Components.Layout;
using SchoolCollab.Admin.Services;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Core.Auth;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// The dev tenant switcher's three-state dropdown and its self-service "make this my
/// default" action.
/// </summary>
/// <remarks>
/// The states are the part that is easy to get subtly wrong: "cleared back to the default"
/// and "explicitly chose the system / no-tenant context" must be distinguishable, because
/// the second is what shared-blueprint coded-value editing needs. Each state therefore gets
/// a test that asserts the <em>resolved selection</em>, not just the rendered labels.
/// </remarks>
[TestClass]
public class DevTenantSwitcherTests : BunitContext
{
    private static readonly Guid Alpha = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Beta = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private const string TenantsJson =
        """
        [{"id":"11111111-1111-1111-1111-111111111111","name":"Alpha","type":"School","isDefault":true},
         {"id":"22222222-2222-2222-2222-222222222222","name":"Beta","type":"School","isDefault":false}]
        """;

    public DevTenantSwitcherTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
        Services.AddLogging();
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public readonly List<(string Method, string Url, string? Body)> Calls = new();
        private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _responses = new(StringComparer.OrdinalIgnoreCase);

        public ScriptedHandler Map(string method, string url, HttpStatusCode status, string body)
        {
            _responses[$"{method.ToUpperInvariant()} {url}"] = (status, body);
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request.Method.Method, request.RequestUri!.PathAndQuery, body));

            var key = $"{request.Method.Method.ToUpperInvariant()} {request.RequestUri.PathAndQuery}";
            if (_responses.TryGetValue(key, out var hit))
            {
                return new HttpResponseMessage(hit.Status)
                {
                    Content = new StringContent(hit.Body, Encoding.UTF8, "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }

    private sealed class StubSelection : IDevTenantSelection
    {
        public Guid? Selected { get; set; }
        public Guid? Applied { get; private set; }

        public Task<Guid?> GetSelectedTenantIdAsync(CancellationToken ct = default) => Task.FromResult(Selected);
        public Task SetSelectedTenantIdAsync(Guid? tenantId, CancellationToken ct = default)
        {
            Selected = tenantId;
            return Task.CompletedTask;
        }

        public Task<Guid?> GetAppliedDefaultTenantIdAsync(CancellationToken ct = default) => Task.FromResult(Applied);
        public Task SetAppliedDefaultTenantIdAsync(Guid? tenantId, CancellationToken ct = default)
        {
            Applied = tenantId;
            return Task.CompletedTask;
        }
    }

    private sealed class StubAuthStateProvider(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(user));
    }

    /// <summary>
    /// A principal carrying (or omitting) the <c>tenant_id</c> claim, mirroring what
    /// <see cref="TestAuthHandler"/> produces: a real tenant id, or no claim at all for the
    /// system / no-tenant context.
    /// </summary>
    private static ClaimsPrincipal UserWithTenant(Guid? tenantId)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, "Test User") };
        if (tenantId is { } id && id != Guid.Empty)
            claims.Add(new Claim("tenant_id", id.ToString()));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, TestAuthExtensions.TestAuthScheme));
    }

    private (IRenderedComponent<DevTenantSwitcher> Cut, StubSelection Selection, ScriptedHandler Handler) Render(
        Guid? selected,
        Guid? preference = null,
        Guid? claimTenant = null)
    {
        var preferenceJson = preference is null
            ? """{"defaultTenantId":null}"""
            : $$"""{"defaultTenantId":"{{preference}}"}""";

        var handler = new ScriptedHandler()
            .Map("GET", "/api/tenants", HttpStatusCode.OK, TenantsJson)
            .Map("GET", "/api/tenants/me/default", HttpStatusCode.OK, preferenceJson)
            .Map("PUT", "/api/tenants/me/default", HttpStatusCode.NoContent, string.Empty);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeatureFlags:FEATURE:DisableOIDCAuth"] = "true",
            })
            .Build();

        var selection = new StubSelection { Selected = selected };
        var client = new TenantsApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://settings-api") });

        Services.AddSingleton<IConfiguration>(config);
        Services.AddSingleton(client);
        Services.AddSingleton<IDevTenantSelection>(selection);
        Services.AddSingleton(new AdminDefaultTenantResolver(
            client, selection, NullLogger<AdminDefaultTenantResolver>.Instance));

        // The component reconciles this circuit's tenant claim against the applied default after
        // the first render, so it needs the auth state the real host supplies.
        Services.AddSingleton<AuthenticationStateProvider>(new StubAuthStateProvider(UserWithTenant(claimTenant)));
        Services.AddSingleton<VisibleTenantService>();

        return (Render<DevTenantSwitcher>(), selection, handler);
    }

    private static IRenderedComponent<FluentSelect<DevTenantSwitcher.TenantOption>> SelectOf(
        IRenderedComponent<DevTenantSwitcher> cut)
        => cut.FindComponent<FluentSelect<DevTenantSwitcher.TenantOption>>();

    [TestMethod]
    public void RendersSystemDefaultAndEveryTenant()
    {
        var (cut, _, _) = Render(selected: null);

        var labels = cut.FindAll("fluent-option").Select(o => o.TextContent.Trim()).ToList();

        labels.Should().HaveCount(4);
        labels[0].Should().Be("(system / no tenant)");
        labels[1].Should().Be("(default: Alpha)", "the platform default is named, so the entry says where 'default' points");
        labels.Should().Contain("Alpha").And.Contain("Beta");
    }

    [TestMethod]
    public void WithNoExplicitPick_SelectsTheResolvedDefaultEntry()
    {
        var (cut, _, _) = Render(selected: null);

        SelectOf(cut).Instance.SelectedOption!.Value.Should().Be(
            string.Empty, "no explicit pick means the '(default …)' entry, not the system context");
    }

    /// <summary>
    /// The state that must not collapse into "nothing picked": an explicit system /
    /// no-tenant selection is a real value (<see cref="Guid.Empty"/>), and the dropdown has to
    /// show it as such or the operator can never tell which context they are editing in.
    /// </summary>
    [TestMethod]
    public void WithAnExplicitSystemPick_SelectsTheSystemEntry()
    {
        var (cut, _, _) = Render(selected: Guid.Empty);

        SelectOf(cut).Instance.SelectedOption!.Value.Should().Be("system");
    }

    [TestMethod]
    public void WithAnExplicitTenantPick_SelectsThatTenant()
    {
        var (cut, _, _) = Render(selected: Beta);

        SelectOf(cut).Instance.SelectedOption!.Value.Should().Be(Beta.ToString());
    }

    /// <summary>
    /// With a recorded preference the default entry must name the PREFERENCE, not the platform
    /// default — the whole point of the second link in the chain.
    /// </summary>
    [TestMethod]
    public void WithAPreference_TheDefaultEntryNamesThePreference()
    {
        var (cut, _, _) = Render(selected: null, preference: Beta);

        var labels = cut.FindAll("fluent-option").Select(o => o.TextContent.Trim()).ToList();
        labels[1].Should().Be("(default: Beta)");
    }

    /// <summary>
    /// The self-service action. It must record the tenant the operator is currently looking at
    /// (here: the resolved platform default, since they have no pick and no preference), and
    /// re-publish it so the rest of the circuit already speaks for the new default.
    /// </summary>
    [TestMethod]
    public async Task MakeDefault_RecordsTheCurrentTenant_AndPublishesIt()
    {
        var (cut, selection, handler) = Render(selected: null);

        var button = cut.FindAll("fluent-button").First(b => b.TextContent.Contains("Default", StringComparison.Ordinal));
        await cut.InvokeAsync(() => button.Click());

        var put = handler.Calls.Single(c => c.Method == "PUT" && c.Url == "/api/tenants/me/default");
        put.Body.Should().Contain(Alpha.ToString(), "the operator pins the tenant they are looking at");
        selection.Applied.Should().Be(Alpha);
    }

    [TestMethod]
    public void ClearIsNotOfferedWhenNoPreferenceExists()
    {
        var (cut, _, _) = Render(selected: null);

        cut.FindAll("fluent-button")
            .Should().NotContain(b => b.TextContent.Contains("Clear", StringComparison.Ordinal),
                "there is nothing to clear when no preference is recorded");
    }

    [TestMethod]
    public void ClearIsOfferedWhenAPreferenceExists()
    {
        var (cut, _, _) = Render(selected: null, preference: Beta);

        cut.FindAll("fluent-button")
            .Should().Contain(b => b.TextContent.Contains("Clear", StringComparison.Ordinal),
                "a recorded preference must be releasable, or the platform default could never apply again");
    }
}
