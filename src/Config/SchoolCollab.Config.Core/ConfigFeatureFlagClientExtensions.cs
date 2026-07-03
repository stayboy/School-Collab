using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SchoolCollab.Config.Core.Caching;
using SchoolCollab.Core.Features;

namespace SchoolCollab.Config.Core;

public static class ConfigFeatureFlagClientExtensions
{
    /// <summary>
    /// Wires the cached, DB-backed feature-flag client in a *consumer* host (an API,
    /// worker, or the unified Admin host) that wants <see cref="IFeatureFlagService"/>
    /// resolved from the Config service rather than <see cref="IConfiguration"/> alone.
    /// <para>
    /// Registers: <see cref="HybridCache"/> (L1 in-proc + L2 Redis), a named
    /// <c>config-api</c> <see cref="System.Net.Http.HttpClient"/> pointed at the
    /// Aspire-discovered Config service, <see cref="IFeatureFlagResolver"/> and
    /// replaces the default <see cref="IFeatureFlagService"/> registration with
    /// <see cref="ConfigFeatureFlagService"/>. Call this after
    /// <c>AddAuthAndTenancy</c>; the <c>IConfiguration</c>-only fallback registered
    /// there is replaced here.
    /// </para>
    /// <para>
    /// Cold-start fallback: when the Config API is unreachable the client reads
    /// <c>FeatureFlags:FEATURE:*</c> from <c>IConfiguration</c>, so consumers keep
    /// working without the Config service running (preserves dev out-of-the-box).
    /// </para>
    /// </summary>
    public static IServiceCollection AddConfigFeatureFlagClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHybridCache(options =>
        {
            options.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromSeconds(30),
                LocalCacheExpiration = TimeSpan.FromSeconds(5)
            };
        });

        services.AddHttpClient(ConfigFeatureFlagService.HttpClientName, c =>
            c.BaseAddress = new Uri("https+http://config-api"));

        services.TryAddSingleton<IFeatureFlagResolver, ConfigFeatureFlagService>();
        services.Replace(ServiceDescriptor.Singleton<IFeatureFlagService, ConfigFeatureFlagService>());

        return services;
    }
}