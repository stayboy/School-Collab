namespace SchoolCollab.Core.Features;

/// <summary>
/// In-process signal that one or more feature flags changed, so Blazor gates
/// (<see cref="GateBase"/>) can re-evaluate without a page reload.
/// </summary>
/// <remarks>
/// Raised wherever a <c>FeatureFlagChanged</c> event is observed. The Settings client
/// (<c>AddConfigFeatureFlagClient</c>) raises it from its push-invalidation subscriber
/// on the <c>config</c> RabbitMQ exchange; until that subscriber lands, the gate still
/// re-resolves on page load and on authentication changes.
/// </remarks>
public interface IFeatureFlagChangeNotifier
{
    /// <summary>Raised when one or more feature flags may have changed (null payload = any).</summary>
    event Action? FeatureFlagsChanged;

    /// <summary>Raise the change signal for all subscribers.</summary>
    void Raise();
}
