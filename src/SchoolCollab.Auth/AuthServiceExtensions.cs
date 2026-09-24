using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SchoolCollab.Auth.Auth;
using SchoolCollab.Auth.Endpoints;
using SchoolCollab.Auth.Options;
using SchoolCollab.Auth.Providers;
using SchoolCollab.Auth.Services;
using SchoolCollab.Core.Http;

namespace SchoolCollab.Auth;

/// <summary>
/// DI registrations for the SchoolCollab.Auth service, kept out of the feature
/// host's <c>Program.cs</c> (dotnet-best-practices "Never": no inline
/// <c>services.Add*()</c> in a feature <c>Program.cs</c>; the repo precedent is
/// <c>SchoolCollab.Core.Messaging.OutboxExtensions</c>). Later round-A passes
/// that add registrations extend this file instead of inlining them.
/// </summary>
public static class AuthServiceExtensions
{
    /// <summary>
    /// Registers <see cref="AuthServiceOptions"/> from the <c>Auth</c> configuration
    /// section with startup validation (<c>ValidateOnStart</c>), so a missing Keycloak
    /// setting fails fast at launch instead of surfacing mid-request. The validator is
    /// <see cref="AuthServiceOptions.FirstValidationError"/> — shared with
    /// <c>AuthServiceSmokeTests</c> so the startup check and the tests cannot drift.
    /// </summary>
    public static IServiceCollection AddAuthServiceOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<AuthServiceOptions>()
            .Bind(configuration.GetSection(AuthServiceOptions.SectionName))
            .Validate(o => AuthServiceOptions.FirstValidationError(o) is null,
                "Invalid SchoolCollab.Auth configuration; see AuthServiceOptions.FirstValidationError for the failing key.")
            .ValidateOnStart();

        return services;
    }

    /// <summary>
    /// Registers the round-A services plus round B's D15 provider seam and portal-session scheme.
    /// The Direct-Grant exchanger, the Admin REST client and the <see cref="KeycloakAuthProvider"/>
    /// are typed <see cref="HttpClient"/> consumers (their endpoints are derived from
    /// <c>Auth:Keycloak:Authority</c>); the stores use <see cref="TimeProvider"/> (the repo's
    /// testable-clock pattern); the audit log is a thin structured <see cref="ILogger"/> wrapper.
    /// </summary>
    public static IServiceCollection AddAuthServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<OneTimeCodeStore>();
        // Round B pass B6 (D16): the bootstrap-code custody. It REUSES round A's OneTimeCodeStore
        // primitive (single-use, TTL-bound, redirect-URI-bound) in a dedicated instance created
        // inside the store, so bootstrap codes and D6 handshake codes live in two namespaces that
        // cannot cross-redeem: the portal-bound code can never be turned into a claim set at
        // /auth/redeem, and an app's handshake code can never be redeemed at /auth/bootstrap/redeem.
        services.AddSingleton<BootstrapCodeStore>();
        services.AddSingleton<PortalSessionStore>();
        services.AddSingleton<ClaimSetFactory>();
        services.AddSingleton<AuthAuditLog>();
        // The per-app callback allowlist (round B pass B5b, spec §14): one shared, immutable
        // matcher over Auth:AppCallbackPrefixes. Singleton because the bound options are fixed at
        // startup (ValidateOnStart) and the matcher holds only parsed prefixes.
        services.AddSingleton<AppCallbackAllowlist>();
        services.AddHttpClient<DirectGrantExchanger>();
        services.AddHttpClient<KeycloakAdminClient>();
        services.AddHttpClient<KeycloakAuthProvider>();

        // Round B pass B7 (D17): the mediated picker readers. Each is a typed cross-module
        // HttpClient — retry handler + long handler lifetime, and the base address is the LITERAL
        // Aspire service name at the call site (the FamiliesModuleServices precedent), which the
        // auth resource's .WithReference(settingsApi)/.WithReference(studentsApi) satisfies. Tenant
        // propagation is OFF: the identity of a mediated read travels in the forwarded bearer token
        // (the data APIs' tenant middleware reads its tenant_id claim), so a dev-tenant header would
        // be a second, weaker source of scope.
        services.AddCrossModuleHttpClient<TenantDirectoryReader>(
            "https+http://settings-api",
            propagateTenant: false);
        services.AddCrossModuleHttpClient<TeacherDirectoryReader>(
            "https+http://students-api",
            propagateTenant: false);

        // The custody access-token read a mediated call needs. It resolves THROUGH the seam's own
        // singleton, so a mediated read and every portal-facing call share ONE custody view of a
        // session; it is NOT part of IAuthProvider, whose seven members are the portal-facing
        // contract (D15). The provider implements both interfaces by construction — round B has
        // exactly one provider implementation, so the cast is a wiring invariant, not a guess.
        services.AddSingleton<ISessionTokenAccessor>(
            sp => (ISessionTokenAccessor)sp.GetRequiredService<IAuthProvider>());

        // The seam resolves to the SAME typed-client instance the factory above builds — a second
        // registration of the implementation type would hand it an unwired HttpClient. It is a
        // SINGLETON because the provider remembers a session's refreshed token set in custody
        // (see KeycloakAuthProvider): a per-request instance would lose it between requests and
        // break refresh-token rotation.
        services.AddSingleton<IAuthProvider>(sp => sp.GetRequiredService<KeycloakAuthProvider>());

        // The portal-session scheme (D12): the portal presents its opaque session id instead of a
        // token. Registered here rather than in AddAuthAndTenancy because the auth service's portal
        // surface exists with OIDC either on or off — the portal's own login is the D16 form path, so
        // it holds a session in both flag states. The scheme list of the portal-facing policy lives
        // in AuthEndpointGroup.
        services.AddAuthentication()
            .AddScheme<PortalSessionAuthenticationOptions, PortalSessionAuthenticationHandler>(
                PortalSessionAuthenticationHandler.SchemeName,
                static _ => { })
            .AddPolicyScheme(
                PortalSessionAuthenticationHandler.GatewaySchemeName,
                "Portal-session or host-scheme routing",
                policy =>
                {
                    // Authentication follows the request (portal session vs the host's own scheme);
                    // the challenge is ALWAYS this scheme's bare 401, never a redirect.
                    policy.ForwardDefaultSelector = PortalSessionAuthenticationHandler.SelectAuthenticationScheme;
                    policy.ForwardChallenge = PortalSessionAuthenticationHandler.SchemeName;
                });

        return services;
    }

    /// <summary>
    /// Registers the fixed-window rate limiter for <c>POST /auth/exchange</c> (spec §14,
    /// plan-review P1-5, service side). Moved out of <c>Program.cs</c> so the endpoint tests
    /// can compose the real pipeline from public extensions only (the 429 assertion needs the
    /// genuine middleware; the <c>Microsoft.AspNetCore.Mvc.Testing</c> package route was
    /// rejected to honour round A's no-new-package constraint — pass 3c row 51). The ordering
    /// requirement that made the host file the lawful home is preserved: <c>Program.cs</c>
    /// still calls this BEFORE <c>builder.Build()</c>.
    /// </summary>
    public static IServiceCollection AddAuthRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // TRUST BOUNDARY: POST /auth/exchange is called server-to-server by the portal inside
        // the AppHost network, never from a browser; this limiter is the service-side mitigation,
        // keyed per portal caller. The portal form's own antiforgery requirement is round B's,
        // because the form does not exist in round A. Framework-provided — no new package.
        var rateLimitConfig = configuration.GetSection(AuthServiceOptions.SectionName)
            .Get<AuthServiceOptions>() ?? new AuthServiceOptions();
        services.AddRateLimiter(rateLimiter =>
        {
            // The framework default rejection status is 503; the 429 endpoint assertion depends
            // on this override.
            rateLimiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            rateLimiter.AddPolicy(AuthEndpointGroup.ExchangeRateLimitPolicyName, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        Window = rateLimitConfig.CredentialEndpointRateLimitWindow,
                        PermitLimit = rateLimitConfig.CredentialEndpointRateLimitPermits,
                        QueueLimit = 0,
                    }));
        });

        return services;
    }
}
