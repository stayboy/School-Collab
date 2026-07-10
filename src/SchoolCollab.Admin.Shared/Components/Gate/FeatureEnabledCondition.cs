using SchoolCollab.Core.Features;

namespace SchoolCollab.Admin.Shared.Components.Gate;

/// <summary>True when the given runtime feature flag is enabled for the current tenant.</summary>
public sealed class FeatureEnabledCondition(string key, IFeatureFlagService featureFlags) : IGateCondition
{
    public async Task<bool> EvaluateAsync(CancellationToken ct = default)
        => await featureFlags.IsEnabledAsync(key, ct);
}
