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
        // Tenant context storage (AsyncLocal-backed provider shared per process)
        services.AddSingleton<TenantProvider>();
        services.AddSingleton<ITenantProvider>(sp => sp.GetRequiredService<TenantProvider>());

        // Bridge from ClaimsPrincipal -> TenantContext
        services.AddScoped<IClaimsTransformation, TenantClaimsTransformation>();

        // Register configuration and feature flag service only if not already registered
        services.AddSingleton<IConfiguration>(configuration);
        services.TryAddSingleton<IFeatureFlagService, FeatureFlagService>();

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
