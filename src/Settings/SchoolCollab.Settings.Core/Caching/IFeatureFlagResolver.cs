using SchoolCollab.Settings.Core.DTOs;

namespace SchoolCollab.Settings.Core.Caching;

/// <summary>
/// Resolves the effective value of a feature flag for a given tenant against the
/// global default, applying any in-effect tenant override. Returns the resolved
/// value plus its source for observability/audit.
/// </summary>
public interface IFeatureFlagResolver
{
    Task<ResolvedFlag> ResolveAsync(string key, Guid? tenantId, CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, ResolvedFlag>> ResolveAllAsync(Guid? tenantId, CancellationToken ct = default);
}

public readonly record struct ResolvedFlag(string Key, bool IsEnabled, ResolvedFlagSource Source, DateTimeOffset ResolvedAt);

public enum ResolvedFlagSource
{
    TenantOverride = 0,
    GlobalDefault = 1,
    ConfigurationFallback = 2,
    ServiceUnavailable = 3,
}