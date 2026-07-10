using SchoolCollab.Core.Features;

namespace SchoolCollab.Settings.Core.Caching;

/// <summary>Default <see cref="IFeatureFlagChangeNotifier"/> — a process-local signal.</summary>
public sealed class FeatureFlagChangeNotifier : IFeatureFlagChangeNotifier
{
    public event Action? FeatureFlagsChanged;

    public void Raise() => FeatureFlagsChanged?.Invoke();
}
