using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace SchoolCollab.Core.Features;

/// <summary>
/// Extension methods for adding centralized feature-flag configuration from
/// <c>SchoolCollab.Config</c>.
/// </summary>
public static class FeatureFlagConfigurationExtensions
{
    /// <summary>
    /// Adds a configuration source that fetches feature flags from the Config API at
    /// <paramref name="configBaseAddress"/>. The source is registered with a short
    /// total request deadline and standard resilience so a transient Config API failure
    /// does not prevent the consuming service from starting.
    /// </summary>
    /// <param name="builder">The configuration builder to extend.</param>
    /// <param name="configBaseAddress">Base address of the Config API. In Aspire this is
    /// typically <c>https+http://config</c>.</param>
    /// <returns>The same <see cref="IConfigurationBuilder"/> for chaining.</returns>
    public static IConfigurationBuilder AddRemoteFeatureFlags(
        this IConfigurationBuilder builder,
        string configBaseAddress)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configBaseAddress);

        var resolvedBaseAddress = ResolveAspireServiceAddress(configBaseAddress);

        var services = new ServiceCollection();
        services.AddHttpClient("ConfigFeatureFlags")
            .AddStandardResilienceHandler(options =>
            {
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);
            });

        var serviceProvider = services.BuildServiceProvider();
        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var httpClient = httpClientFactory.CreateClient("ConfigFeatureFlags");
        httpClient.BaseAddress = new Uri(resolvedBaseAddress.TrimEnd('/'));

        return builder.Add(new ConfigFeatureFlagConfigurationSource(httpClient));
    }

    /// <summary>
    /// Resolves an Aspire service-discovery URI (e.g. <c>https+http://config</c>) to an
    /// actual endpoint URL using the environment variables Aspire injects.
    /// Non-Aspire addresses are returned unchanged.
    /// </summary>
    internal static string ResolveAspireServiceAddress(string address)
    {
        if (!address.Contains('+') || !Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            return address;
        }

        var serviceName = uri.Host;
        var schemes = uri.Scheme.Split('+', StringSplitOptions.RemoveEmptyEntries);

        foreach (var scheme in schemes)
        {
            var envVarName = $"services__{serviceName}__{scheme}__0";
            var resolved = Environment.GetEnvironmentVariable(envVarName);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return resolved;
            }
        }

        // No Aspire env vars found; return original and let the HttpClient fail gracefully.
        return address;
    }
}

public class ConfigFeatureFlagConfigurationSource : IConfigurationSource
{
    private readonly HttpClient _httpClient;

    public ConfigFeatureFlagConfigurationSource(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new ConfigFeatureFlagConfigurationProvider(_httpClient);
    }
}
