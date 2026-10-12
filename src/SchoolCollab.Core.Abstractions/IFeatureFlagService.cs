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
