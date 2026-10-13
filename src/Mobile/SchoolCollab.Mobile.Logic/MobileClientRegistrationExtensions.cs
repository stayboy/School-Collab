using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SchoolCollab.Admin.Shared.Services;

namespace SchoolCollab.Mobile.Logic;

/// <summary>
/// The mobile head's client-side service registration — the device's replacement for the module
/// <c>ModuleServices.cs</c> files, which assume a <i>server</i> process (discovery-style base
/// addresses, the Redis-backed tenant handler, server-to-server claim reads). The registered
/// interface shapes are unchanged, so the components consuming the clients
/// (<c>@inject CodedValuesApiClient</c>, …) need no edit: only the base address and the bearer
/// handler differ.
/// </summary>
/// <remarks>
/// It lives in <c>Mobile.Logic</c> (not in the head's <c>MauiProgram</c>) because the Logic split is
/// load-bearing — "everything CI has to see lives in <c>Mobile.Logic</c>" — and it makes the
/// base-address and handler wiring unit-testable on ubuntu. The host supplies the two platform
/// implementations the auth path needs (<see cref="ITokenStore"/>,
/// <see cref="ISystemBrowserAuthenticator"/>); they cannot be named from a plain <c>net10.0</c>
/// library because they are MAUI Essentials types.
/// </remarks>
public static class MobileClientRegistrationExtensions
{
    /// <summary>
    /// The single <see cref="MobileApiOptions"/> name every Admin.Shared client resolves its base
    /// address with. All eight clients are server-side <c>settings-api</c> consumers
    /// (<c>Settings.Application/ModuleServices.cs</c>), and <see cref="MobileApiOptions.Resolve"/>
    /// THROWS on any name outside <c>StudentsApi</c>/<c>SettingsApi</c>/<c>AssignmentsApi</c> — so a
    /// per-client invented name is a guaranteed runtime throw, not a configuration miss.
    /// </summary>
    private const string SettingsApiName = nameof(MobileApiOptions.SettingsApi);

    /// <summary>
    /// Registers the Admin.Shared API clients the mobile head can reach, each bound to the one
    /// configured Settings API base address and carrying the bearer handler, plus the
    /// <see cref="VisibleTenantService"/> the gates require.
    /// </summary>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="MobileApiOptions.Resolve"/> rejects
    /// <see cref="SettingsApiName"/> — unreachable while the name above matches
    /// <see cref="MobileApiOptions.ApiNames"/>.</exception>
    public static IServiceCollection AddMobileAdminClients(this IServiceCollection services, MobileApiOptions apiOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(apiOptions);

        // One resolve for the whole group: every client points at the Settings API, and a missing
        // value degrades that one API (null base address) instead of failing the configuration.
        var baseAddress = apiOptions.Resolve(SettingsApiName);

        // Transient, like every delegating handler in this repo: IHttpClientFactory builds one per
        // outgoing request through the client's pipeline.
        services.TryAddTransient<BearerTokenDelegatingHandler>();

        AddClient<CodedValuesApiClient>(services, baseAddress);
        AddClient<ConfigFlagsApiClient>(services, baseAddress);
        AddClient<TenantsApiClient>(services, baseAddress);
        AddClient<EntityCodeRulesApiClient>(services, baseAddress);
        AddClient<NotificationPolicyApiClient>(services, baseAddress);
        AddClient<AssignmentPolicyApiClient>(services, baseAddress);
        AddClient<SignatureConsentTextApiClient>(services, baseAddress);
        AddClient<AssignmentAiPromptApiClient>(services, baseAddress);

        // The gates' tenant condition reads the `tenant_id` claim through this service.
        services.AddScoped<VisibleTenantService>();

        return services;
    }

    /// <summary>
    /// Registers the mobile auth path: the session, the interactive sign-in, the client-side
    /// auth-state provider <b>as the one instance</b> every consumer sees, the testable clock, and
    /// the named <c>oidc-token</c> client the token requests go through.
    /// </summary>
    /// <remarks>
    /// <see cref="ITokenStore"/> and <see cref="ISystemBrowserAuthenticator"/> are deliberately NOT
    /// registered here: their implementations are platform code and belong to the host. The head
    /// binds them right after this call.
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static IServiceCollection AddMobileAuth(this IServiceCollection services, MobileAuthOptions authOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(authOptions);

        services.AddSingleton(authOptions);
        services.AddSingleton(TimeProvider.System);

        services.AddSingleton<TokenSession>();
        services.AddSingleton<OidcLoginService>();

        // The provider is registered under BOTH its concrete type (the host's teardown/DI seam) and
        // the abstraction the gates inject, resolving to the SAME instance: the gates subscribe to
        // AuthenticationStateChanged once per rendered gate, and a second instance would be a
        // second, silent source of auth state.
        services.AddSingleton<MobileAuthenticationStateProvider>();
        services.AddSingleton<AuthenticationStateProvider>(
            provider => provider.GetRequiredService<MobileAuthenticationStateProvider>());

        // The token endpoints are absolute (built from the configured authority), so the named
        // client's base address is convenience only — bound from configuration, never a literal.
        services.AddHttpClient(
            TokenSession.HttpClientName,
            client => client.BaseAddress = authOptions.AuthorityUri);

        return services;
    }

    private static void AddClient<TClient>(IServiceCollection services, Uri? baseAddress)
        where TClient : class
        => services
            .AddHttpClient<TClient>(client => client.BaseAddress = baseAddress)
            .AddHttpMessageHandler<BearerTokenDelegatingHandler>();
}
