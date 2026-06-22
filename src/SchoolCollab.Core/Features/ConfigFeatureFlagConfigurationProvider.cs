using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace SchoolCollab.Core.Features;

/// <summary>
/// Loads the <c>FeatureFlags</c> configuration section from the SchoolCollab.Config
/// <c>/api/features</c> endpoint during application startup. Flags are surfaced
/// under the <c>FeatureFlags:</c> prefix so the existing <see cref="FeatureFlagService"/>
/// continues to work unchanged.
/// </summary>
public class ConfigFeatureFlagConfigurationProvider : ConfigurationProvider
{
    private readonly HttpClient _httpClient;

    public ConfigFeatureFlagConfigurationProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public override void Load()
    {
        try
        {
            using var response = _httpClient.GetAsync("/api/features").ConfigureAwait(false).GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
            {
                Data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                return;
            }

            var json = response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            var flags = JsonSerializer.Deserialize<Dictionary<string, bool>>(json);

            if (flags is null)
            {
                Data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                return;
            }

            var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var flag in flags)
            {
                data[$"FeatureFlags:{flag.Key}"] = flag.Value.ToString();
            }

            Data = data;
        }
        catch
        {
            // Config API is unavailable (e.g. service not running, network issue).
            // Leave configuration empty so local appsettings/environment values remain effective.
            Data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
