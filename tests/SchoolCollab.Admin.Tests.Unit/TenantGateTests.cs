using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components;
using SchoolCollab.Admin.Shared.Services;
using System.Security.Claims;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for the reusable <see cref="TenantGate"/> component — the
/// tenant-visibility analog of [Authorize]. Covers the two modes (Hide /
/// Disable) and the default vs. custom fallback (spec TG-FR-2 / TG-FR-3 /
/// AC 1–4).
/// </summary>
[TestClass]
public class TenantGateTests : BunitContext
{
    private const string DefaultBannerText = "No tenant selected";
    private const string CustomFallbackText = "Custom no-tenant message.";
    private const string ChildContentText = "gated-content";

    public TenantGateTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    /// <summary>
    /// Mutable auth provider so a test can opt into a real or default tenant.
    /// </summary>
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

    private void Register(bool realTenant)
    {
        var auth = new MutableAuthenticationStateProvider { User = CreateUser(realTenant) };
        Services.AddSingleton<AuthenticationStateProvider>(auth);
        Services.AddSingleton(new VisibleTenantService(auth, NullLogger<VisibleTenantService>.Instance));
    }

    [TestMethod]
    public void RealTenant_RendersChildContent_NoBanner()
    {
        Register(realTenant: true);

        var cut = Render<TenantGate>(ps => ps.AddChildContent($"<p>{ChildContentText}</p>"));

        cut.Markup.Should().Contain(ChildContentText);
        cut.Markup.Should().NotContain(DefaultBannerText, "no banner when a real tenant is present");
    }

    [TestMethod]
    public void NoTenant_NoFallback_ShowsDefaultBanner_HidesChild()
    {
        Register(realTenant: false);

        var cut = Render<TenantGate>(ps => ps.AddChildContent($"<p>{ChildContentText}</p>"));

        cut.Markup.Should().Contain(DefaultBannerText, "the default banner is shown when no Fallback is supplied");
        cut.Markup.Should().NotContain(ChildContentText, "ChildContent is hidden when no real tenant");
    }

    [TestMethod]
    public void NoTenant_CustomFallback_ShowsFallback_HidesChildAndDefaultBanner()
    {
        Register(realTenant: false);

        var cut = Render<TenantGate>(ps => ps
            .AddChildContent($"<p>{ChildContentText}</p>")
            .Add(p => p.Fallback, (RenderFragment)(b => b.AddContent(0, CustomFallbackText))));

        cut.Markup.Should().Contain(CustomFallbackText, "the custom Fallback is shown");
        cut.Markup.Should().NotContain(ChildContentText, "ChildContent is hidden");
        cut.Markup.Should().NotContain(DefaultBannerText, "the default banner is suppressed by a custom Fallback");
    }

    [TestMethod]
    public void DisableMode_NoTenant_RendersChildDisabled()
    {
        Register(realTenant: false);

        var cut = Render<TenantGate>(ps => ps
            .AddChildContent($"<p>{ChildContentText}</p>")
            .Add(p => p.Mode, TenantGate.TenantGateMode.Disable));

        cut.Markup.Should().Contain(ChildContentText, "ChildContent is still rendered in Disable mode");
        cut.Markup.Should().Contain("disabled", "the child is wrapped in a disabled <fieldset> when no real tenant");
    }

    [TestMethod]
    public void DisableMode_RealTenant_RendersChildEnabled()
    {
        Register(realTenant: true);

        var cut = Render<TenantGate>(ps => ps
            .AddChildContent($"<p>{ChildContentText}</p>")
            .Add(p => p.Mode, TenantGate.TenantGateMode.Disable));

        cut.Markup.Should().Contain(ChildContentText);
        cut.Markup.Should().NotContain("disabled", "the child is enabled when a real tenant is present");
    }
}

