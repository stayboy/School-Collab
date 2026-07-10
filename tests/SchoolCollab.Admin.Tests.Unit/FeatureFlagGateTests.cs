using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SchoolCollab.Admin.Shared.Components;
using SchoolCollab.Admin.Shared.Components.Gate;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Core.Features;
using System.Security.Claims;

namespace SchoolCollab.Admin.Tests.Unit;

/// <summary>
/// bUnit tests for <see cref="FeatureFlagGate"/> — the reactive feature-flag surface.
/// Covers Hide/Disable modes, default vs custom fallback, and live re-evaluation on
/// <see cref="IFeatureFlagChangeNotifier.FeatureFlagsChanged"/> (spec UG-FR-4 / UG-FR-5 / AC 2–3).
/// </summary>
[TestClass]
public class FeatureFlagGateTests : BunitContext
{
    private const string ChildText = "gated-content";
    private const string FlagKey = "FEATURE:EnableCodedValuesAiChat";

    public FeatureFlagGateTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
    }

    private sealed class StubFlagNotifier : IFeatureFlagChangeNotifier
    {
        public event Action? FeatureFlagsChanged;
        public void Raise() => FeatureFlagsChanged?.Invoke();
    }

    private sealed class StubFlagService : IFeatureFlagService
    {
        public bool Enabled { get; set; }
        public bool IsEnabled(string featureKey) => Enabled;
        public Task<bool> IsEnabledAsync(string featureKey, CancellationToken ct = default)
            => Task.FromResult(Enabled);
        public IDictionary<string, bool> GetAllFlags() => new Dictionary<string, bool>();
        public Task<IReadOnlyDictionary<string, bool>> GetAllFlagsAsync(Guid? tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, bool>>(new Dictionary<string, bool>());
    }

    private sealed class TestAuthProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim("tenant_id", Guid.NewGuid().ToString()), new Claim("tenant_name", "Hydeson") }, "TestScheme"))));
    }

    private void Register(bool flagOn, out StubFlagService flags, out StubFlagNotifier notifier)
    {
        // Register every service first; bUnit locks the provider as soon as any
        // service is *retrieved*, so we must not call GetRequiredService here.
        var auth = new TestAuthProvider();
        Services.AddSingleton<AuthenticationStateProvider>(auth);
        Services.AddSingleton(new VisibleTenantService(auth, NullLogger<VisibleTenantService>.Instance));
        flags = new StubFlagService { Enabled = flagOn };
        notifier = new StubFlagNotifier();
        Services.AddSingleton<IFeatureFlagService>(flags);
        Services.AddSingleton<IFeatureFlagChangeNotifier>(notifier);
    }

    [TestMethod]
    public void FlagOn_RendersChildContent()
    {
        Register(flagOn: true, out _, out _);
        var cut = Render<FeatureFlagGate>(ps => ps
            .Add(p => p.Key, FlagKey)
            .AddChildContent($"<p>{ChildText}</p>"));

        cut.Markup.Should().Contain(ChildText);
        cut.Markup.Should().NotContain("This feature is not enabled", "no fallback banner when the flag is on");
    }

    [TestMethod]
    public void FlagOff_NoFallback_ShowsDefaultBanner()
    {
        Register(flagOn: false, out _, out _);
        var cut = Render<FeatureFlagGate>(ps => ps
            .Add(p => p.Key, FlagKey)
            .AddChildContent($"<p>{ChildText}</p>"));

        cut.Markup.Should().Contain("This feature is not enabled for your tenant.");
        cut.Markup.Should().NotContain(ChildText, "ChildContent is hidden when the flag is off");
    }

    [TestMethod]
    public void FlagOff_CustomFallback_ShowsFallback()
    {
        Register(flagOn: false, out _, out _);
        var cut = Render<FeatureFlagGate>(ps => ps
            .Add(p => p.Key, FlagKey)
            .AddChildContent($"<p>{ChildText}</p>")
            .Add(p => p.Fallback, (RenderFragment)(b => b.AddContent(0, "nope"))));

        cut.Markup.Should().Contain("nope", "the custom Fallback is shown");
        cut.Markup.Should().NotContain(ChildText, "ChildContent is hidden");
        cut.Markup.Should().NotContain("This feature is not enabled", "the default banner is suppressed by a custom Fallback");
    }

    [TestMethod]
    public void DisableMode_FlagOff_RendersChildDisabled()
    {
        Register(flagOn: false, out _, out _);
        var cut = Render<FeatureFlagGate>(ps => ps
            .Add(p => p.Key, FlagKey)
            .Add(p => p.Mode, GateMode.Disable)
            .AddChildContent($"<p>{ChildText}</p>"));

        cut.Markup.Should().Contain(ChildText, "ChildContent is still rendered in Disable mode");
        cut.Markup.Should().Contain("disabled", "the child is wrapped in a disabled <fieldset> when the flag is off");
    }

    [TestMethod]
    public void LiveFlagChange_FlipsWithoutExplicitRerender()
    {
        Register(flagOn: false, out var flags, out var notifier);
        var cut = Render<FeatureFlagGate>(ps => ps
            .Add(p => p.Key, FlagKey)
            .AddChildContent($"<p>{ChildText}</p>"));

        cut.Markup.Should().NotContain(ChildText, "hidden initially (flag off)");

        flags.Enabled = true;
        notifier.Raise();

        cut.Markup.Should().Contain(ChildText, "the gate flips live when the flag is toggled, with no explicit re-render call");
    }
}
