using Microsoft.FluentUI.AspNetCore.Components;
using SchoolCollab.Admin.Components;
using SchoolCollab.Assignments.Application;
using SchoolCollab.Settings.Application;
using SchoolCollab.Settings.Core;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.Features;
using SchoolCollab.Students.Application;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Auth + tenancy (OIDC via Keycloak for the unified admin host). Disable OIDC when
// FEATURE:DisableOIDCAuth is enabled; falls back to TestAuth for local development.
// NOTE: DisableOIDCAuth is a *startup auth-mode switch* read from IConfiguration
// directly (below), NOT a runtime feature flag — ASP.NET Core auth schemes are
// registered once at startup and cannot be flipped at runtime. Runtime, mutable,
// tenant-overridable flags (e.g. FEATURE:EnableCodedValuesAiChat) are resolved by
// the cached Settings client registered via AddSettingsFeatureFlagClient below.
// See documents/solution/settings-context-merge-spec.md §10.
builder.Services.AddAuthAndTenancy(builder.Configuration);

// Redis distributed cache backs the HybridCache L2 used by
// ConfigFeatureFlagService (the cached Settings client used by the Admin host).
var cacheConnectionString = builder.Configuration.GetConnectionString("cache")
    ?? builder.Configuration["Aspire:StackExchange:Redis:ConnectionString"];

if (string.IsNullOrWhiteSpace(cacheConnectionString))
{
    builder.Services.AddDistributedMemoryCache();
}
else
{
    builder.AddRedisDistributedCache("cache");
}

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddFluentUIComponents();

// Register module services (HttpClient factories for each bounded context).
// AddSettingsModule replaces the legacy AddCodedValuesModule + AddConfigModule
// pair (see documents/solution/settings-context-merge-spec.md §9).
builder.Services.AddSettingsModule();
builder.Services.AddAssignmentsModule();
builder.Services.AddStudentsModule();

// B4 — the flag-ON portal handshake (spec §5.2 / D6): the typed redemption client against the
// auth service, plus the host's /signin-handshake, /login and /logout routes. The base address is
// this host's own literal so CrossModuleWiringTests can match it to the AppHost's
// .WithReference(auth) on this host's resource (B3) — a literal inside Core would fail the guard
// for the four other hosts that consume Core without referencing the auth service.
builder.Services.AddPortalHandshake("https+http://auth");

// Cached, DB-backed feature-flag client (resolves runtime flags from the
// Settings FeatureFlag aggregate with an IConfiguration fallback). Replaces the
// config-only IFeatureFlagService registered by AddAuthAndTenancy.
builder.Services.AddConfigFeatureFlagClient(builder.Configuration);

var app = builder.Build();

// Startup auth-mode decision: read DisableOIDCAuth directly from IConfiguration
// (NOT via IFeatureFlagService, which is now the cached Config client that does not
// carry this startup-only flag).
var disableOIDC = bool.TryParse(
    builder.Configuration[$"FeatureFlags:{FeatureFlagKeys.DisableOIDCAuth}"], out var d) && d;

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}


app.UseHttpsRedirection();

// Always use authentication - required for both OIDC (production) and TestAuth (development).
// TestAuthHandler reads the tenant from Redis via IDevTenantSelection and sets the tenant_id claim,
// which TenantClaimsTransformation then propagates to TenantProvider.
app.UseAuthentication();

if (!disableOIDC)
{
    app.UseAuthorization();
}

app.UseAntiforgery();

// FEATURE:DisableOIDCAuth and the other feature flags are injected by the
// AppHost via WithEnvironment("FeatureFlags__FEATURE__DisableOIDCAuth", param);
// see src/AppHost/SchoolCollab.AppHost/Program.cs and documents/configuration.md §2.
app.MapStaticAssets();
app.MapDefaultEndpoints();

var razorComponents = app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(
        typeof(SchoolCollab.Settings.Application.Components._Imports).Assembly,
        typeof(SchoolCollab.Assignments.Application.Components._Imports).Assembly,
        typeof(SchoolCollab.Students.Application.Components._Imports).Assembly);

if (!disableOIDC)
{
    razorComponents.RequireAuthorization();

    // B4 — same conditional shape as the authorization gate above: the handshake group needs the
    // OIDC cookie + challenge schemes, which only exist in the real-auth branch (with TestAuth
    // there is no portal login, no OIDC sign-out and nothing to redeem). The routes are additive —
    // with the login-UI flag OFF the challenge path stays today's OIDC behaviour (AC7).
    app.MapPortalHandshakeEndpoints();
}

app.Run();
