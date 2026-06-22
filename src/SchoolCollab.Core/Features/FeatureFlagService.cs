using Microsoft.Extensions.Configuration;

namespace SchoolCollab.Core.Features;

/// <summary>
/// Contract for managing application feature flags.
/// </summary>
public interface IFeatureFlagService
{
    /// <summary>
    /// Checks if a specific feature flag is enabled.
    /// </summary>
    bool IsEnabled(string featureKey);

    /// <summary>
    /// Returns all current feature flags and their states.
    /// </summary>
    IDictionary<string, bool> GetAllFlags();
}

public class FeatureFlagService : IFeatureFlagService
{
    private readonly IConfiguration _configuration;
    private const string SectionKey = "FeatureFlags";

    public FeatureFlagService(IConfiguration configuration)
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
        
        foreach (var child in section.GetChildren())
        {
            if (bool.TryParse(child.Value, out var enabled))
            {
                flags[child.Key] = enabled;
            }
        }
        
        return flags;
    }
}
