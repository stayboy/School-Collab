using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using SchoolCollab.Core.Auth;

namespace SchoolCollab.Core.Http;

/// <summary>
/// Reference-pattern helpers for registering HTTP clients that call other
/// modules in the Aspire topology. Each client gets:
/// <list type="bullet">
/// <item>a longer <see cref="HttpClientBuilderExtensions.SetHandlerLifetime"/></item>
/// <item>the <see cref="CrossModuleRetryDelegatingHandler"/> for transient
///       handler/connection-level faults</item>
/// <item>the <see cref="TenantPropagationDelegatingHandler"/> when
///       <paramref name="propagateTenant"/> is true (Blazor admin → API calls)</item>
/// </list>
/// </summary>
public static class CrossModuleHttpClientExtensions
{
    public static readonly TimeSpan DefaultHandlerLifetime = TimeSpan.FromMinutes(30);
    /// <summary>
    /// Registers a typed cross-module HTTP client with resilience defaults.
    /// </summary>
    public static IHttpClientBuilder AddCrossModuleHttpClient<TClient>(
        this IServiceCollection services,
        string baseAddress,
        bool propagateTenant = true)
        where TClient : class
    {
        EnsureOptions(services);
        services.TryAddTransient<CrossModuleRetryDelegatingHandler>();

        var builder = services
            .AddHttpClient<TClient>(client => client.BaseAddress = new Uri(baseAddress))
            .SetHandlerLifetime(DefaultHandlerLifetime)
            .AddHttpMessageHandler<CrossModuleRetryDelegatingHandler>();

        if (propagateTenant)
        {
            services.TryAddTransient<TenantPropagationDelegatingHandler>();
            builder.AddHttpMessageHandler<TenantPropagationDelegatingHandler>();
        }

        return builder;
    }

    /// <summary>
    /// Registers a named cross-module HTTP client with resilience defaults.
    /// </summary>
    public static IHttpClientBuilder AddCrossModuleHttpClient(
        this IServiceCollection services,
        string name,
        string baseAddress,
        bool propagateTenant = true)
    {
        EnsureOptions(services);
        services.TryAddTransient<CrossModuleRetryDelegatingHandler>();

        var builder = services
            .AddHttpClient(name, client => client.BaseAddress = new Uri(baseAddress))
            .SetHandlerLifetime(DefaultHandlerLifetime)
            .AddHttpMessageHandler<CrossModuleRetryDelegatingHandler>();

        if (propagateTenant)
        {
            services.TryAddTransient<TenantPropagationDelegatingHandler>();
            builder.AddHttpMessageHandler<TenantPropagationDelegatingHandler>();
        }

        return builder;
    }

    private static void EnsureOptions(IServiceCollection services)
    {
        // Registers IOptions<CrossModuleHttpClientOptions> with the defaults
        // defined on the class. Callers can override via Configure<> later.
        services.Configure<CrossModuleHttpClientOptions>(_ => { });
    }
}
