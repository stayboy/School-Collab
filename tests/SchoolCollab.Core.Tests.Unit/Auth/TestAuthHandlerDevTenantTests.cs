using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Encodings.Web;
using SchoolCollab.Core.Auth;

namespace SchoolCollab.Core.Tests.Unit.Auth;

[TestClass]
public class TestAuthHandlerDevTenantTests
{
    private static readonly Guid DefaultTenantId =
        Guid.Parse("00000000-0000-0000-0000-000000000001");

    /// <summary>
    /// Stands up <see cref="TestAuthHandler"/> against a synthetic HttpContext whose
    /// request services provide the given <see cref="IDevTenantSelection"/>, then
    /// authenticates and returns the resolved tenant_id claim.
    /// </summary>
    private static async Task<Guid> AuthenticateAsync(IDevTenantSelection? devSelection)
    {
        var services = new ServiceCollection();
        if (devSelection is not null)
            services.AddSingleton(devSelection);

        var httpContext = new DefaultHttpContext();
        httpContext.RequestServices = services.BuildServiceProvider();

        var scheme = new AuthenticationScheme(
            TestAuthExtensions.TestAuthScheme,
            TestAuthExtensions.TestAuthScheme,
            typeof(TestAuthHandler));

        var handler = new TestAuthHandler(
            new StubOptionsMonitor(new TestAuthHandlerOptions { TenantId = DefaultTenantId }),
            LoggerFactory.Create(_ => { }),
            UrlEncoder.Default);

        await handler.InitializeAsync(scheme, httpContext);
        var result = await handler.AuthenticateAsync();

        result.Succeeded.Should().BeTrue();
        var tenantIdClaim = result.Principal!.FindFirst("tenant_id")!.Value;
        return Guid.Parse(tenantIdClaim);
    }

    [TestMethod]
    public async Task NoDevSelection_UsesDefaultTenantId()
    {
        // No IDevTenantSelection registered at all → fall back to Options.TenantId.
        var tenantId = await AuthenticateAsync(devSelection: null);
        tenantId.Should().Be(DefaultTenantId);
    }

    [TestMethod]
    public async Task DevSelectionReturningNull_UsesDefaultTenantId()
    {
        var tenantId = await AuthenticateAsync(new StubDevTenantSelection(null));
        tenantId.Should().Be(DefaultTenantId);
    }

    [TestMethod]
    public async Task DevSelectionReturningId_UsesSelectedTenantId()
    {
        var selected = Guid.NewGuid();
        var tenantId = await AuthenticateAsync(new StubDevTenantSelection(selected));
        tenantId.Should().Be(selected);
    }

    private sealed class StubDevTenantSelection(Guid? value) : IDevTenantSelection
    {
        public Task<Guid?> GetSelectedTenantIdAsync(CancellationToken ct = default)
            => Task.FromResult(value);
        public Task SetSelectedTenantIdAsync(Guid? tenantId, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class StubOptionsMonitor(TestAuthHandlerOptions options) : IOptionsMonitor<TestAuthHandlerOptions>
    {
        public TestAuthHandlerOptions CurrentValue => options;
        public TestAuthHandlerOptions Get(string? name) => options;
        public IDisposable OnChange(Action<TestAuthHandlerOptions, string?> listener)
            => new NullDisposable();
        private sealed class NullDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}