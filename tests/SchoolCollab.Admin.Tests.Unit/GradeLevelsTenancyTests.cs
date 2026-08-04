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
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit test asserting the Grade Levels landing page skips all tenant-scoped
/// API calls and shows a tenant prompt when the signed-in user has no real
/// tenant (AC7: zero tenant-scoped API calls, explanatory empty state). The
/// page is rendered with a <see cref="CountingHandler"/> that fails loudly on
/// any unexpected request, so a successful no-tenant render proves no call was
/// issued.
/// </summary>
[TestClass]
public class GradeLevelsTenancyTests : BunitContext
{
    private const string TenantEmptyMessage = "Select a tenant to manage grade levels.";

    public GradeLevelsTenancyTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private sealed class MutableAuthenticationStateProvider : AuthenticationStateProvider
    {
        private ClaimsPrincipal _user = new();
        public ClaimsPrincipal User
        {
            set
            {
                _user = value;
                NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
            }
        }
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(_user));
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int CallCount;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref CallCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent($"Unexpected request: {request.Method} {request.RequestUri}")
            });
        }
    }

    private static ClaimsPrincipal CreateUser(bool realTenant)
    {
        var tenantId = realTenant ? Guid.NewGuid().ToString() : Guid.Empty.ToString();
        var claims = new[]
        {
            new Claim("tenant_id", tenantId),
            new Claim("tenant_name", realTenant ? "Hydeson" : "System"),
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestScheme"));
    }

    private (MutableAuthenticationStateProvider Auth, CountingHandler Handler) Register(bool realTenant)
    {
        var auth = new MutableAuthenticationStateProvider { User = CreateUser(realTenant) };
        var handler = new CountingHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:1234") };

        Services.AddSingleton<AuthenticationStateProvider>(auth);
        var codedValuesClient = new CodedValuesApiClient(http);
        Services.AddSingleton(codedValuesClient);
        Services.AddSingleton(new StudentsApiClient(http, NullLogger<StudentsApiClient>.Instance, codedValuesClient));
        Services.AddSingleton(new VisibleTenantService(auth, NullLogger<VisibleTenantService>.Instance));

        return (auth, handler);
    }

    [TestMethod]
    public void NoTenant_SkipsApiCalls_AndShowsTenantPrompt()
    {
        var (_, handler) = Register(realTenant: false);

        var cut = Render<SchoolCollab.Students.Admin.Components.Pages.Students.GradeLevels.Index>();

        handler.CallCount.Should().Be(0, "no tenant-scoped API call may be issued when !IsRealTenant");
        cut.Markup.Should().Contain(TenantEmptyMessage);
        cut.FindAll("fluent-button").Should().BeEmpty("the + New Grade Level button is hidden when !IsRealTenant");
    }

    [TestMethod]
    public void RealTenant_AttemptsApiCalls()
    {
        var (_, handler) = Register(realTenant: true);

        var cut = Render<SchoolCollab.Students.Admin.Components.Pages.Students.GradeLevels.Index>();

        handler.CallCount.Should().BeGreaterThan(0, "tenant-scoped API calls are expected for a real tenant");
        cut.Markup.Should().NotContain(TenantEmptyMessage, "the tenant prompt must not show for a real tenant");
    }
}
