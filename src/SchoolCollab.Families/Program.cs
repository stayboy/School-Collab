using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.AspNetCore.DataProtection;
using StackExchange.Redis;
using SchoolCollab.Core.Auth;
using SchoolCollab.Core.DeepLinks;
using SchoolCollab.Core.Features;
using SchoolCollab.Families.Components;
using SchoolCollab.Families.DeepLinks;
using SchoolCollab.Families.Services;
using SchoolCollab.Settings.Core;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// F1 (slice 2b) — the Families host is a ward/guardian surface app (owner
// decision: Option B). Auth + tenancy mirrors the Admin host exactly: OIDC via
// Keycloak in production, TestAuth fallback when FEATURE:DisableOIDCAuth is set
// (dev default). Note: DisableOIDCAuth is a *startup* auth-mode switch read from
// IConfiguration directly (below), NOT a runtime feature flag — as with Admin,
// auth schemes are registered once at process start.
builder.Services.AddAuthAndTenancy(builder.Configuration);

// WS-E1 (ar-14-deep-links): AddAuthAndTenancy registers .AddCookie() only in the OIDC
// branch. In dev (TestAuth) the landing signs the guardian into a cookie principal, so
// Families registers the cookie handler itself — guarded by the same startup switch to
// avoid the duplicate-scheme throw when OIDC is on.
if (IsFlagEnabled(builder.Configuration, FeatureFlagKeys.DisableOIDCAuth))
{
    builder.Services.AddAuthentication().AddCookie();
}

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

// WS-E1 (ar-14-deep-links): DataProtection keyring shared with the Assignments API on
// the same Redis resource so mint-side ciphertext can be unprotected here. Both hosts
// MUST SetApplicationName the same value. Falls back to the default in-memory keyring
// (still sharing the application name) when no Redis connection string is present.
var deeplinkKeyring = builder.Services.AddDataProtection();
if (string.IsNullOrWhiteSpace(cacheConnectionString))
{
    deeplinkKeyring.SetApplicationName(DeepLinkConstants.ApplicationName);
}
else
{
    // Dedicated process-lifetime keyring multiplexer (same lifetime as the host).
    // ConnectAsync with AbortOnConnectFail=false so an unreachable Redis does not
    // hard-fail host startup — keyring access retries lazily once Redis returns.
    var keyringOptions = ConfigurationOptions.Parse(cacheConnectionString);
    keyringOptions.AbortOnConnectFail = false;
    var keyringMultiplexer = await ConnectionMultiplexer.ConnectAsync(keyringOptions);
    deeplinkKeyring
        .PersistKeysToStackExchangeRedis(keyringMultiplexer)
        .SetApplicationName(DeepLinkConstants.ApplicationName);
}

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddFluentUIComponents();

// F1 — the Families Http client against the Assignments API (service discovery).
builder.Services.AddFamiliesModule();

// WS-E1 (ar-14-deep-links): cached, DB-backed runtime feature-flag client (resolves
// FEATURE:EnableDeepLinks tenant-isolated from the Settings FeatureFlag aggregate with
// an IConfiguration fallback). Replaces the config-only IFeatureFlagService installed by
// AddAuthAndTenancy — mirroring the Admin host's AddConfigFeatureFlagClient pattern.
builder.Services.AddConfigFeatureFlagClient(builder.Configuration);

// WS-E1 (ar-14-deep-links): the purpose-scoped protector the landing uses to
// validate deep-link tokens (unprotect side of the mint/validate contract).
builder.Services.AddScoped<SchoolCollab.Core.DeepLinks.DeepLinkProtector>();

// WS-E1 (ar-14-deep-links): the pure validate→flag→redirect logic backing the public
// /deeplink landing (unprotect → tenant flag → OpenedAt stamp → redirect target).
builder.Services.AddScoped<SchoolCollab.Families.DeepLinks.DeepLinkLandingService>();

// WS-F3 (ar-15-signoff-relocation, decision (e)/step 5): the Families-side certificate
// download orchestration for the guardian sign page (client bytes + the host's own
// fileDownload.js module — the Admin C3 service is RCL-scoped and not reachable here).
// Registered TRANSIENT to match the Admin CertificateDownloadService pattern: the service
// owns its lazily-loaded fileDownload.js module reference and each consuming component
// disposes it, so a long-lived (scoped) instance would leave a single shared module ref
// across the circuit and leak on component disposal. Transient gives each page its own
// instance whose DisposeAsync returns the module ref exactly once.
builder.Services.AddTransient<SchoolCollab.Families.Services.GuardianCertificateDownloadService>();

var app = builder.Build();

// Startup auth-mode decision (mirrors Admin): read DisableOIDCAuth directly from
// IConfiguration, NOT via IFeatureFlagService (which is the runtime Config client
// and does not carry this startup-only flag).
var disableOIDC = IsFlagEnabled(builder.Configuration, FeatureFlagKeys.DisableOIDCAuth);

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

// WS-E1 (ar-14-deep-links): the repo's first public token-auth route group — mapped
// OUTSIDE the razor-components mapping (which is RequireAuthorization-gated when OIDC
// is on) so the /deeplink landing is reachable without a pre-existing identity.
app.MapDeepLinkEndpoints();

app.Run();

/// <summary>Startup auth-mode switch read directly from IConfiguration (mirrors the
/// Admin host): whether FEATURE:DisableOIDCAuth is set. Used to register the dev-only
/// cookie handler before the app builds. Matches <c>AuthTenancyExtensions</c>' two-key
/// resolution — <c>FeatureFlags:{key}</c> first, then the bare <c>{key}</c> — so a
/// deployment setting only the bare key still enables TestAuth.</summary>
static bool IsFlagEnabled(IConfiguration configuration, string featureKey)
{
    var value = configuration[$"FeatureFlags:{featureKey}"]
             ?? configuration[featureKey];

    return bool.TryParse(value, out var enabled) && enabled;
}
