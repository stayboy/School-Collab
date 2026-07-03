using SchoolCollab.Core.Features;

namespace SchoolCollab.CodedValues.Tests.Unit.Components;

/// <summary>
/// Stub <see cref="IFeatureFlagService"/> for bUnit tests that render the
/// CodedValues <c>Index</c> page. The page resolves
/// <c>FEATURE:EnableCodedValuesAiChat</c> at init via
/// <see cref="IFeatureFlagService.IsEnabledAsync"/>; this stub returns
/// <c>true</c> for every key so the AI-chat surfaces render and the existing
/// chat tests run unchanged.
/// </summary>
internal sealed class AlwaysOnFeatureFlagService : IFeatureFlagService
{
    public bool IsEnabled(string featureKey) => true;

    public IDictionary<string, bool> GetAllFlags() => new Dictionary<string, bool>();
}