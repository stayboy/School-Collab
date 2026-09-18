using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
    /// Adds cookie + OpenID Connect authentication using Keycloak and wires the current tenant
    /// from token claims into <see cref="ITenantProvider"/> via <see cref="TenantClaimsTransformation"/>.
    /// When <c>FEATURE:DisableOIDCAuth</c> is enabled (typically in Development), replaces OIDC with
    /// <see cref="TestAuthHandler"/>.
    /// </summary>
    public static IServiceCollection AddAuthAndTenancy(
        this IServiceCollection services,
        IConfiguration configuration)
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

        // Dev tenant switcher store (auth-disabled / TestAuth mode only). Backed by
        // the shared IDistributedCache (Redis in dev) so the selection made in the
        // admin shell propagates to every API host's TestAuthHandler. Only consulted
        // by TestAuthHandler, which is registered solely when DisableOIDCAuth is on.
        services.TryAddSingleton<IDevTenantSelection, DevTenantSelection>();

        // Register configuration and feature flag service only if not already registered
        services.AddSingleton<IConfiguration>(configuration);
        services.TryAddSingleton<IFeatureFlagService, ConfigurationFeatureFlagService>();

        var disableOIDC = IsFlagEnabled(configuration, FeatureFlagKeys.DisableOIDCAuth);

        if (disableOIDC)
        {
            services
                .AddAuthentication(TestAuthExtensions.TestAuthScheme)
                .AddTestAuth();
        }
        else
        {
            var keycloakAuthority = configuration["Auth:Keycloak:Authority"]
                ?? "https://keycloak.local/realms/school-collab";
            var keycloakClientId = configuration["Auth:Keycloak:ClientId"]
                ?? "school-collab-client";

            // Authentication + authorization
            services
                .AddAuthentication(options =>
                {
                    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
                })
                .AddCookie()
                .AddOpenIdConnect(options =>
                {
                    options.Authority = keycloakAuthority;
                    options.ClientId = keycloakClientId;
                    options.ClientSecret = configuration["Auth:Keycloak:ClientSecret"]
                        ?? "secret";
                    options.ResponseType = "code";
                    options.SaveTokens = true;

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
                })
                .AddJwtBearer(options =>
                {
                    options.Authority = keycloakAuthority;
                    // P1-1: Audience sets TokenValidationParameters audience validation; the
                    // realm's oidc-audience-mapper emits aud=clientId on the access token.
                    options.Audience = keycloakClientId;

                    // DEV-ONLY for the ar-20 Keycloak dev container (http:// authority) —
                    // production MUST keep this false (default). Same dev-gating note as the
                    // OIDC branch above.
                    options.RequireHttpsMetadata = false;

                    // NOTE (P1-2): the bearer path does NOT use ClaimActions — the JwtBearer
                    // handler copies every claim from the access-token JWT payload by default
                    // (incl. tenant_id/tenant_name/tenant_type/teacher_id, emitted by the realm
                    // mappers with access.token.claim=true), so ICurrentUser reads the same
                    // claim shape from a bearer token as from the OIDC cookie. No mapping here.
                });
        }

        services.AddAuthorization();

        return services;
    }

    private static bool IsFlagEnabled(IConfiguration configuration, string featureKey)
    {
        var value = configuration[$"FeatureFlags:{featureKey}"]
                 ?? configuration[featureKey];

        return bool.TryParse(value, out var enabled) && enabled;
    }
}
