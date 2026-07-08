using Microsoft.FluentUI.AspNetCore.Components;
using SchoolCollab.Admin.Components;
using SchoolCollab.Assignments.Admin;
using SchoolCollab.Settings.Admin;
using SchoolCollab.Settings.Core;
using SchoolCollab.Core.Auth;
using SchoolCollab.Students.Admin;

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

// Cached, DB-backed feature-flag client (resolves runtime flags from the
// Settings FeatureFlag aggregate with an IConfiguration fallback). Replaces the
// config-only IFeatureFlagService registered by AddAuthAndTenancy.
builder.Services.AddConfigFeatureFlagClient(builder.Configuration);

var app = builder.Build();

// Startup auth-mode decision: read DisableOIDCAuth directly from IConfiguration
// (NOT via IFeatureFlagService, which is now the cached Config client that does not
// carry this startup-only flag).
var disableOIDC = bool.TryParse(
    builder.Configuration["FeatureFlags:FEATURE:DisableOIDCAuth"], out var d) && d;

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
        typeof(SchoolCollab.Settings.Admin.Components._Imports).Assembly,
        typeof(SchoolCollab.Assignments.Admin.Components._Imports).Assembly,
        typeof(SchoolCollab.Students.Admin.Components._Imports).Assembly);

if (!disableOIDC)
{
    razorComponents.RequireAuthorization();
}

app.Run();
