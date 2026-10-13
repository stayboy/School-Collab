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

    [TestMethod]
    public async Task NullSelection_UsesThePortalAppliedDefault_OverTheConfiguredOne()
    {
        var applied = Guid.NewGuid();

        var tenantId = await AuthenticateAsync(
            new StubDevTenantSelection(null) { AppliedDefault = applied });

        tenantId.Should().Be(applied,
            "the default the admin portal resolved and applied on load outranks the configured options");
    }

    [TestMethod]
    public async Task ExplicitSelection_OutranksThePortalAppliedDefault()
    {
        var selected = Guid.NewGuid();

        var tenantId = await AuthenticateAsync(
            new StubDevTenantSelection(selected) { AppliedDefault = Guid.NewGuid() });

        tenantId.Should().Be(selected, "an explicit switcher pick is authoritative");
    }

    /// <summary>
    /// The precedence bug this pins: <see cref="Guid.Empty"/> is a MEANINGFUL explicit pick
    /// (the system / no-tenant context that shared-blueprint coded-value editing needs), so the
    /// resolution must test <c>HasValue</c> and not <c>!= Guid.Empty</c>. Treating it as "nothing
    /// selected" would silently bounce the operator off the system context onto the portal
    /// default, every request.
    /// </summary>
    [TestMethod]
    public async Task ExplicitSystemContext_OutranksThePortalAppliedDefault()
    {
        var tenantId = await AuthenticateAsync(
            new StubDevTenantSelection(Guid.Empty) { AppliedDefault = Guid.NewGuid() });

        tenantId.Should().Be(Guid.Empty,
            "an explicit pick of the system / no-tenant context must survive the applied default");
    }

    private sealed class StubDevTenantSelection(Guid? value) : IDevTenantSelection
    {
        public Task<Guid?> GetSelectedTenantIdAsync(CancellationToken ct = default)
            => Task.FromResult(value);
        public Task SetSelectedTenantIdAsync(Guid? tenantId, CancellationToken ct = default)
            => Task.CompletedTask;

        /// <summary>Optional portal-applied default; null keeps the fixture on the pre-existing path.</summary>
        public Guid? AppliedDefault { get; set; }

        public Task<Guid?> GetAppliedDefaultTenantIdAsync(CancellationToken ct = default)
            => Task.FromResult(AppliedDefault);
        public Task SetAppliedDefaultTenantIdAsync(Guid? tenantId, CancellationToken ct = default)
        {
            AppliedDefault = tenantId;
            return Task.CompletedTask;
        }
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