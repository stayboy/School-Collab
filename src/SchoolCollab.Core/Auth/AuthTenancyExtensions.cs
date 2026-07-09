using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
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

        // Dev tenant switcher store (auth-disabled / TestAuth mode only). Backed by
        // the shared IDistributedCache (Redis in dev) so the selection made in the
        // admin shell propagates to every API host's TestAuthHandler. Only consulted
        // by TestAuthHandler, which is registered solely when DisableOIDCAuth is on.
        services.TryAddSingleton<IDevTenantSelection, DevTenantSelection>();

        // Register configuration and feature flag service only if not already registered
        services.AddSingleton<IConfiguration>(configuration);
        services.TryAddSingleton<IFeatureFlagService, ConfigurationFeatureFlagService>();

        var disableOIDC = IsFlagEnabled(configuration, "FEATURE:DisableOIDCAuth");

        if (disableOIDC)
        {
            services
                .AddAuthentication(TestAuthExtensions.TestAuthScheme)
                .AddTestAuth();
        }
        else
        {
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
                    options.Authority = configuration["Auth:Keycloak:Authority"]
                        ?? "https://keycloak.local/realms/school-collab";
                    options.ClientId = configuration["Auth:Keycloak:ClientId"]
                        ?? "school-collab-client";
                    options.ClientSecret = configuration["Auth:Keycloak:ClientSecret"]
                        ?? "secret";
                    options.ResponseType = "code";
                    options.SaveTokens = true;

                    // TODO: configure ClaimActions here once the Keycloak claim mapping is finalised
                    // e.g. options.ClaimActions.MapJsonKey("tenant_id", "tenant_id");
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
