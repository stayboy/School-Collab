using Microsoft.Extensions.Configuration;

namespace SchoolCollab.Core.Features;

public class ConfigurationFeatureFlagService : IFeatureFlagService
{
    private readonly IConfiguration _configuration;
    private const string SectionKey = "FeatureFlags";

    public ConfigurationFeatureFlagService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public bool IsEnabled(string featureKey)
    {
        // Supports both "FeatureFlags:FEATURE_X" and "FEATURE_X" for flexibility
        var value = _configuration[$"{SectionKey}:{featureKey}"] 
                 ?? _configuration[featureKey];
                 
        return bool.TryParse(value, out var enabled) && enabled;
    }

    public IDictionary<string, bool> GetAllFlags()
    {
        var flags = new Dictionary<string, bool>();
        var section = _configuration.GetSection(SectionKey);
        CollectFlags(section, flags, prefix: null);
        return flags;
    }

    private static void CollectFlags(IConfigurationSection section, Dictionary<string, bool> flags, string? prefix)
    {
        foreach (var child in section.GetChildren())
        {
            var key = prefix is null ? child.Key : $"{prefix}:{child.Key}";

            if (!string.IsNullOrEmpty(child.Value) && bool.TryParse(child.Value, out var enabled))
            {
                flags[key] = enabled;
            }
            else
            {
                // Recurse into nested sections (e.g. "FEATURE:DisableOIDCAuth")
                CollectFlags(child, flags, key);
            }
        }
    }
}
