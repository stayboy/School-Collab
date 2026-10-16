using System.Net;
using System.Net.Http;
using System.Security.Claims;
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
using SchoolCollab.Admin.Components;
using SchoolCollab.Admin.Services;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Core.Auth;
using SchoolCollab.Students.Application.Services;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// Renders the Admin host's real <see cref="Routes"/> component and asserts the app shell comes
/// out of it.
/// </summary>
/// <remarks>
/// <para>
/// This is the check a component-level suite cannot make. Every page test in this project renders
/// its component directly, so all of them stayed green while the shipped app rendered a blank
/// page: Round A wrapped <c>&lt;Router&gt;</c> in FluentUI v5's <c>&lt;FluentProviders&gt;</c>, and
/// that component declares no <c>ChildContent</c> parameter (its only parameters are
/// <c>ClassValue</c>/<c>StyleValue</c>), so it renders its own provider set and silently discards
/// its children. No build error, no test failure, no circuit error — just no UI.
/// </para>
/// <para>
/// The assertion is on the <em>layout shell</em> (<c>article.content</c>, authored by
/// <c>Layout/MainLayout.razor</c>), never on page content. That element exists only if
/// <c>Router</c> matched a route and <c>RouteView</c> rendered the default layout, which is exactly
/// the placement contract: it discriminates on where the provider host sits without dragging a
/// page's API surface into the fixture.
/// </para>
/// </remarks>
[TestClass]
public class RoutesShellRenderTests : BunitContext
{
    public RoutesShellRenderTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
        Services.AddLogging();

        // The shell's own dependencies, registered with the same shape the other bUnit fixtures in
        // this project use. None of them is reached on "/" (no tenant claim means the toolbar's API
        // call and the dev tenant switcher both stay dormant), so one inert HTTP pipeline covers
        // all three typed clients.
        var http = new HttpClient(new NotFoundHandler()) { BaseAddress = new Uri("https://localhost:1234") };
        var codedValues = new CodedValuesApiClient(http);
        Services.AddSingleton(codedValues);
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, codedValues));
        Services.AddSingleton(new TenantsApiClient(http));

        // No DisableOIDCAuth entry: the dev tenant switcher stays hidden and asks for nothing.
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        var auth = new AnonymousAuthStateProvider();
        Services.AddSingleton<AuthenticationStateProvider>(auth);
        Services.AddSingleton(new VisibleTenantService(auth, NullLogger<VisibleTenantService>.Instance));
        Services.AddSingleton<IDevTenantSelection>(new StubDevTenantSelection());
        Services.AddSingleton<AdminDefaultTenantResolver>();
    }

    [TestMethod]
    public void Routes_At_The_Root_Route_Renders_The_Layout_Shell()
    {
        var cut = Render<Routes>();

        // article.content is MainLayout's content slot — Layout/MainLayout.razor authors it, and
        // nothing outside a RouteView-rendered layout can produce it. Its absence is the blank-app
        // regression; its presence proves the Router still owns the root of the render tree.
        cut.FindAll("article.content").Should().ContainSingle(
            "the routed layout must render: <Router> has to stay the root element of Routes.razor " +
            "(wrapping it in FluentUI v5's <FluentProviders>, which has no ChildContent parameter, " +
            "discards every routed page and blanks the whole app with no error)");
    }

    private sealed class NotFoundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private sealed class AnonymousAuthStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(new ClaimsPrincipal()));
    }

    private sealed class StubDevTenantSelection : IDevTenantSelection
    {
        public Task<Guid?> GetSelectedTenantIdAsync(CancellationToken ct = default) => Task.FromResult<Guid?>(null);
        public Task SetSelectedTenantIdAsync(Guid? tenantId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Guid?> GetAppliedDefaultTenantIdAsync(CancellationToken ct = default) => Task.FromResult<Guid?>(null);
        public Task SetAppliedDefaultTenantIdAsync(Guid? tenantId, CancellationToken ct = default) => Task.CompletedTask;
    }
}
