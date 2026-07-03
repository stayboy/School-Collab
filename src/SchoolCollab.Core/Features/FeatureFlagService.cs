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

    /// <summary>
    /// Async, tenant-aware check. The default implementation delegates to the
    /// synchronous <see cref="IsEnabled"/> so existing config-only implementations
    /// (<see cref="ConfigurationFeatureFlagService"/>) keep compiling without
    /// changes. A cached, DB-backed implementation
    /// (<c>SchoolCollab.Config.Core.Caching.ConfigFeatureFlagService</c>) overrides
    /// this to resolve a tenant override against the global default.
    /// </summary>
    Task<bool> IsEnabledAsync(string featureKey, CancellationToken ct = default)
        => Task.FromResult(IsEnabled(featureKey));

    /// <summary>
    /// Async, tenant-aware bulk read. The default implementation delegates to
    /// <see cref="GetAllFlags"/> for back-compat.
    /// </summary>
    Task<IReadOnlyDictionary<string, bool>> GetAllFlagsAsync(Guid? tenantId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyDictionary<string, bool>>(new Dictionary<string, bool>(GetAllFlags()));
}

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
