using Microsoft.AspNetCore.Components;
using SchoolCollab.Admin.Shared.Components.Gate;
using SchoolCollab.Core.Features;

namespace SchoolCollab.Admin.Shared.Components;

/// <summary>
/// Declarative, reactive gate for a runtime feature flag — the Blazor surface for
/// <see cref="IFeatureFlagService"/>. Derives from <see cref="GateBase"/>; resolves
/// <see cref="Key"/> and re-evaluates live when the flag changes (via
/// <see cref="IFeatureFlagChangeNotifier"/>), with no page reload.
/// </summary>
public class FeatureFlagGate : GateBase
{
    /// <summary>The feature-flag key, e.g. <c>"FEATURE:EnableCodedValuesAiChat"</c>.</summary>
    [Parameter] public string? Key { get; set; }

    /// <summary>Hide (default) or disable the gated content when the flag is off.</summary>
    [Parameter] public GateMode Mode { get; set; } = GateMode.Hide;

    [Inject] private IFeatureFlagService FeatureFlags { get; set; } = default!;

    protected override void OnParametersSet()
    {
        _mode = Mode;
    }

    protected override Task<IReadOnlyList<IGateCondition>> GetConditionsAsync()
        => Task.FromResult<IReadOnlyList<IGateCondition>>(new IGateCondition[] { new FeatureEnabledCondition(Key!, FeatureFlags) });

    // Feature-flag gates hide silently by default — no "feature disabled" banner.
    protected override bool ShowDefaultBanner => false;
}
