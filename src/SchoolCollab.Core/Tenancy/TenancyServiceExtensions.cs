using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SchoolCollab.Core.Tenancy;

/// <summary>
/// Shared DI registration for tenancy services.
/// </summary>
public static class TenancyServiceExtensions
{
    /// <summary>
    /// Registers the default <see cref="TenantProvider"/> and its
    /// <see cref="ITenantProvider"/> interface if not already registered.
    /// </summary>
    /// <remarks>
    /// Uses <see cref="ServiceCollectionDescriptorExtensions.TryAddSingleton{TService, TImplementation}(IServiceCollection)"/>
    /// so callers such as <see cref="Auth.AuthTenancyExtensions.AddAuthAndTenancy"/> can still
    /// override or extend the registration when auth is present.
    /// </remarks>
    public static IServiceCollection AddTenancy(this IServiceCollection services)
    {
        services.TryAddSingleton<TenantProvider>();
        services.TryAddSingleton<ITenantProvider>(sp => sp.GetRequiredService<TenantProvider>());

        return services;
    }
}
