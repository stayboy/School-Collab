using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Core.Features;

namespace SchoolCollab.Core.Auth;

/// <summary>
/// Shared configuration for OpenID Connect authentication + tenant context wiring.
/// </summary>
public static class AuthTenancyExtensions
{
    /// <summary>
    /// The JWT bearer authentication scheme name used by API endpoint groups that need
    /// real bearer-token auth (ar-20). Registered only when OIDC is enabled
    /// (<c>FEATURE:DisableOIDCAuth=false</c>); in TestAuth mode API groups use the
    /// default <see cref="AuthenticationOptions.DefaultScheme"/> instead.
    /// </summary>
    public const string BearerScheme = "Bearer";

    /// <summary>
    /// The challenge-routing policy scheme (round B, design P1-2). Registered as
    /// <see cref="AuthenticationOptions.DefaultChallengeScheme"/> in the OIDC branch so that
    /// <c>AddOpenIdConnect</c> no longer challenges Keycloak unconditionally: the scheme
    /// forwards the challenge to <see cref="OpenIdConnectDefaults.AuthenticationScheme"/> when
    /// <c>FEATURE:DisableKeycloakLoginUi</c> is OFF (today's behaviour, unchanged) and to
    /// <see cref="PortalRedirectChallengeHandler.SchemeName"/> when it is ON. The target is
    /// chosen once, at startup (D5) — auth pipelines are fixed at startup.
    /// </summary>
    public const string LoginUiChallengeScheme = "LoginUiChallenge";

    /// <summary>
    /// Adds cookie + OpenID Connect authentication using Keycloak and wires the current tenant
    /// from token claims into <see cref="ITenantProvider"/> via <see cref="TenantClaimsTransformation"/>.
    /// When <c>FEATURE:DisableOIDCAuth</c> is enabled (typically in Development), replaces OIDC with
    /// <see cref="TestAuthHandler"/> — unless <paramref name="requireOidcRelyingParty"/> opts the host
    /// into the relying-party pipeline.
    /// </summary>
    /// <param name="requireOidcRelyingParty">
    /// Dev-mode carve-out (D16, round <c>keycloak-logout-oidc</c>): with
    /// <c>FEATURE:DisableOIDCAuth</c> ON, additionally register the OIDC relying-party pipeline
    /// (default cookie scheme + OIDC, sharing the one options configurator with the real-auth branch)
    /// while <see cref="TestAuthExtensions.TestAuthScheme"/> stays the default scheme. Set only by the
    /// <c>auth</c> service: it IS the relying party, and its passkey <c>/complete</c> endpoint reads the
    /// cookie-scheme ticket the OIDC handler signs on <c>/signin-oidc</c>, so the dev bypass must spare
    /// the RP itself. Left <c>false</c>, every other consumer's dev pipeline is unchanged. Registration
    /// performs no network I/O (Keycloak metadata discovery is lazy, on the first challenge).
    /// </param>
    public static IServiceCollection AddAuthAndTenancy(
        this IServiceCollection services,
        IConfiguration configuration,
        bool requireOidcRelyingParty = false)
    {
        // Tenant context storage (AsyncLocal-backed provider shared per process).
        // Register via the shared tenancy helper so core modules can also resolve
        // ITenantProvider when authentication is not configured (e.g. workers/tests).
        services.AddTenancy();
        // Required so TenantProvider can fall back to the authenticated principal's
        // tenant_id claim (HttpContext.User) when the AsyncLocal seeded by
        // IClaimsTransformation is not present in the current async scope.
        services.AddHttpContextAccessor();

        // Bridge from ClaimsPrincipal -> TenantContext
        services.AddScoped<IClaimsTransformation, TenantClaimsTransformation>();

        // Server-authoritative current-user resolution (ar-20): teacher id + tenant from
        // the ClaimsPrincipal across OIDC cookie, bearer JWT, and TestAuth.
        services.AddScoped<ICurrentUser, CurrentUser>();

        // ar-24: bearer forwarding for real-auth cross-context API hops. Transient (one
        // instance per outgoing call via IHttpClientFactory); no-op in TestAuth/dev mode.
        services.TryAddTransient<BearerForwardingDelegatingHandler>();

        // Dev tenant switcher store (auth-disabled / TestAuth mode only). Backed by
        // the shared IDistributedCache (Redis in dev) so the selection made in the
        // admin shell propagates to every API host's TestAuthHandler. Only consulted
        // by TestAuthHandler, which is registered solely when DisableOIDCAuth is on.
        //
        // Registered through a FACTORY rather than as an implementation type. A type
        // registration is a hard CONSTRUCTOR dependency, so the container cannot build
        // it without an IDistributedCache — yet this shared helper is also called by a
        // host that legitimately has none: SchoolCollab.Auth registers no cache (it is
        // not a tenant-scoped data host and never reads the switcher). Under
        // ValidateOnBuild (the Development default) that turned an unused, OPTIONAL
        // dependency into a startup crash — "Unable to resolve service for type
        // IDistributedCache while attempting to activate DevTenantSelection" at
        // builder.Build() — so the auth service could not start in Development AT ALL.
        // The factory resolves late, which also makes it order-independent: the data
        // hosts register their cache AFTER this call. No cache present degrades to the
        // documented "no tenant selected" posture, which TestAuthHandler already falls
        // back from (it reads the value as `Guid?` and only overrides on HasValue).
        services.TryAddSingleton<IDevTenantSelection>(sp =>
            sp.GetService<IDistributedCache>() is { } cache
                ? new DevTenantSelection(cache, sp.GetRequiredService<ILogger<DevTenantSelection>>())
                : NullDevTenantSelection.Instance);

        // Register configuration and feature flag service only if not already registered
        services.AddSingleton<IConfiguration>(configuration);
        services.TryAddSingleton<IFeatureFlagService, ConfigurationFeatureFlagService>();

        var disableOIDC = IsFlagEnabled(configuration, FeatureFlagKeys.DisableOIDCAuth);

        if (disableOIDC)
        {
            var devAuthentication = services
                .AddAuthentication(TestAuthExtensions.TestAuthScheme)
                .AddTestAuth();

            if (requireOidcRelyingParty)
            {
                // D16: the auth service is the relying party — the dev bypass spares the
                // consumers, not the RP. Additive only: TestAuth stays the DEFAULT scheme, and
                // the challenge-routing policy scheme, JwtBearer and the portal-redirect handler
                // are deliberately NOT registered here (the RP's own flow goes through the OIDC
                // scheme's default SignInScheme, not a challenge policy). Same options
                // configurator as the real-auth branch, so the two cannot drift.
                var (rpAuthority, rpClientId) = ReadKeycloakClientSettings(configuration);

                devAuthentication
                    .AddCookie()
                    .AddOpenIdConnect(options => ConfigureKeycloakOpenIdConnect(
                        options,
                        configuration,
                        rpAuthority,
                        rpClientId));
            }
        }
        else
        {
            var (keycloakAuthority, keycloakClientId) = ReadKeycloakClientSettings(configuration);

            // D4/D5: which login UI the Blazor hosts present. Read ONCE here at startup —
            // the challenge target is baked into the policy scheme below, the same
            // "pipeline fixed at startup" posture as DisableOIDCAuth above. It only re-routes
            // the challenge; validation/authorization stay identical in both states.
            var disableKeycloakLoginUi = IsFlagEnabled(
                configuration,
                FeatureFlagKeys.DisableKeycloakLoginUi);

            // Authentication + authorization
            var authentication = services
                .AddAuthentication(options =>
                {
                    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                    // Not OpenIdConnect directly: the policy scheme below forwards to the
                    // scheme the login-UI flag selected. OFF is today's OIDC challenge
                    // (the forward target is the OIDC scheme).
                    options.DefaultChallengeScheme = LoginUiChallengeScheme;
                })
                .AddPolicyScheme(LoginUiChallengeScheme, "Login UI challenge routing", policy =>
                {
                    policy.ForwardChallenge = disableKeycloakLoginUi
                        ? PortalRedirectChallengeHandler.SchemeName
                        : OpenIdConnectDefaults.AuthenticationScheme;
                })
                .AddCookie()
                .AddOpenIdConnect(options => ConfigureKeycloakOpenIdConnect(
                    options,
                    configuration,
                    keycloakAuthority,
                    keycloakClientId))
                .AddJwtBearer(options =>
                {
                    options.Authority = keycloakAuthority;
                    // P1-1: Audience sets TokenValidationParameters audience validation; the
                    // realm's oidc-audience-mapper emits aud=clientId on the access token.
                    options.Audience = keycloakClientId;

                    // D9 parity with the shared OIDC options configurator: the same claim travels on the access token
                    // and the same default inbound mapping renames it to ClaimTypes.Role, so the
                    // pinned type stays identical to the OIDC value above and bearer roles
                    // resolve exactly like cookie roles. (Same corrective-pass evidence as the
                    // OIDC comment.)
                    options.TokenValidationParameters.RoleClaimType = ClaimTypes.Role;

                    // DEV-ONLY for the ar-20 Keycloak dev container (http:// authority) —
                    // production MUST keep this false (default). Same dev-gating note as the
                    // shared OIDC options configurator (ConfigureKeycloakOpenIdConnect).
                    options.RequireHttpsMetadata = false;

                    // NOTE (P1-2): the bearer path does NOT use ClaimActions — the JwtBearer
                    // handler copies every claim from the access-token JWT payload by default
                    // (incl. tenant_id/tenant_name/tenant_type/teacher_id, emitted by the realm
                    // mappers with access.token.claim=true), so ICurrentUser reads the same
                    // claim shape from a bearer token as from the OIDC cookie. No mapping here.
                });

            if (disableKeycloakLoginUi)
            {
                // Registered only with the flag ON: with it OFF the policy scheme forwards to
                // OIDC and this handler is unreachable, keeping the OFF pipeline identical to
                // the pre-flag one. The login URL is a startup read of Auth:Portal:LoginUrl
                // (env-var Auth__Portal__LoginUrl) fanned to the browser-facing hosts only;
                // absent here it makes the handler fail closed with 401.
                authentication.AddScheme<PortalRedirectChallengeOptions, PortalRedirectChallengeHandler>(
                    PortalRedirectChallengeHandler.SchemeName,
                    options => options.LoginUrl = configuration["Auth:Portal:LoginUrl"]);
            }
        }

        services.AddAuthorization();

        return services;
    }

    /// <summary>
    /// The Keycloak client settings the OIDC registration needs, with the dev defaults that let
    /// registration succeed when no Keycloak configuration is present. One spelling: both the
    /// real-auth branch and the <c>requireOidcRelyingParty</c> carve-out read them here.
    /// </summary>
    private static (string Authority, string ClientId) ReadKeycloakClientSettings(
        IConfiguration configuration)
        => (configuration["Auth:Keycloak:Authority"]
                ?? "https://keycloak.local/realms/school-collab",
            configuration["Auth:Keycloak:ClientId"]
                ?? "school-collab-client");

    /// <summary>
    /// The single spelling of the Keycloak OpenID Connect options, consumed by BOTH the real-auth
    /// branch and the dev <c>requireOidcRelyingParty</c> carve-out so the two pipelines cannot
    /// drift. Reads configuration only — no network I/O at registration; Keycloak metadata
    /// discovery happens lazily on the first challenge.
    /// </summary>
    private static void ConfigureKeycloakOpenIdConnect(
        OpenIdConnectOptions options,
        IConfiguration configuration,
        string authority,
        string clientId)
    {
        options.Authority = authority;
        options.ClientId = clientId;
        options.ClientSecret = configuration["Auth:Keycloak:ClientSecret"]
            ?? "secret";
        options.ResponseType = "code";
        options.SaveTokens = true;

        // D9: Keycloak is the sole source of roles. The realm's `User Realm Role`
        // protocol mapper emits a flat `roles` claim on the ID token, and the
        // handler's DEFAULT inbound mapping (MapInboundClaims=true — nothing in the
        // repo overrides it) renames that claim to ClaimTypes.Role. Pinning the
        // MAPPED type here is what makes [Authorize(Roles=...)]/RequireRole resolve
        // from the claim that actually survives. Pinning the literal "roles" was
        // PROVEN ineffective (pass-3d corrective pass: the round's /auth/admin gate
        // rejected every authenticated caller under the default mapping until the
        // pin was corrected; 12 gate tests failed with the literal pin). Must stay
        // identical to the JwtBearer value (cookie/bearer parity).
        options.TokenValidationParameters.RoleClaimType = ClaimTypes.Role;

        // DEV-ONLY for the ar-20 Keycloak dev container (http:// authority):
        // an http:// authority is rejected unless metadata HTTPS is relaxed.
        // Production MUST keep this true (default) — gate on the dev flag.
        options.RequireHttpsMetadata = false;

        // The tenant/teacher values reach the cookie identity via the KEYCLOAK ID
        // TOKEN, not via ClaimActions. The realm's protocol mappers enable
        // id.token.claim=true, and the OIDC handler copies every id_token claim onto
        // the ClaimsPrincipal by default. ClaimActions would only shape the userinfo
        // payload, and GetClaimsFromUserInfoEndpoint defaults to false — so the four
        // MapJsonKey calls would be inert. We deliberately do NOT enable the userinfo
        // fetch: it would add a network round-trip for data the id_token already carries.
        options.GetClaimsFromUserInfoEndpoint = false;
    }

    private static bool IsFlagEnabled(IConfiguration configuration, string featureKey)
    {
        var value = configuration[$"FeatureFlags:{featureKey}"]
                 ?? configuration[featureKey];

        return bool.TryParse(value, out var enabled) && enabled;
    }
}
