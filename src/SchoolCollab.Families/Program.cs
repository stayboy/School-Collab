using Microsoft.FluentUI.AspNetCore.Components;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.Features;
using SchoolCollab.Families.Components;
using SchoolCollab.Families.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// F1 (slice 2b) — the Families host is a ward/guardian surface app (owner
// decision: Option B). Auth + tenancy mirrors the Admin host exactly: OIDC via
// Keycloak in production, TestAuth fallback when FEATURE:DisableOIDCAuth is set
// (dev default). Note: DisableOIDCAuth is a *startup* auth-mode switch read from
// IConfiguration directly (below), NOT a runtime feature flag — as with Admin,
// auth schemes are registered once at process start.
builder.Services.AddAuthAndTenancy(builder.Configuration);

// Redis distributed cache backs IDevTenantSelection (TestAuth mode) and mirrors
// the Admin host. Falls back to an in-memory cache when no "cache" connection
// string is resolved (e.g. non-Aspire local run or tests).
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

// F1 — the Families Http client against the Assignments API (service discovery).
builder.Services.AddFamiliesModule();

var app = builder.Build();

// Startup auth-mode decision (mirrors Admin): read DisableOIDCAuth directly from
// IConfiguration, NOT via IFeatureFlagService (which is the runtime Config client
// and does not carry this startup-only flag).
var disableOIDC = bool.TryParse(
    builder.Configuration[$"FeatureFlags:{FeatureFlagKeys.DisableOIDCAuth}"], out var d) && d;

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

// Authentication is always on (TestAuth in dev, OIDC in prod); authorization is
// applied only when OIDC is enabled (the TestAuth path leaves routes open, matching
// the dev posture of the Admin host).
app.UseAuthentication();

if (!disableOIDC)
{
    app.UseAuthorization();
}

app.UseAntiforgery();

app.MapStaticAssets();
app.MapDefaultEndpoints();

var razorComponents = app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

if (!disableOIDC)
{
    razorComponents.RequireAuthorization();
}

app.Run();
