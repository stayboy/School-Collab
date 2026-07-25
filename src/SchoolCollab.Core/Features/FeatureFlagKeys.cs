namespace SchoolCollab.Core.Features;

/// <summary>
/// Canonical keys for application feature flags.
/// </summary>
/// <remarks>
/// Feature flags are stored and looked up as strings. This class centralises
/// those strings so call sites, migration seeds, and tests stay in sync. The
/// <see cref="IFeatureFlagService"/> implementations normalise keys to an
/// upper-case canonical form internally, but all human-readable references
/// should use these constants.
/// </remarks>
public static class FeatureFlagKeys
{
    /// <summary>
    /// When enabled, replaces OIDC authentication with the local test-auth
    /// handler. Used by API endpoint groups, the Admin host, and auth
    /// registration code. Intended for development / integration-test scenarios.
    /// </summary>
    public const string DisableOIDCAuth = "FEATURE:DisableOIDCAuth";

    /// <summary>
    /// Enables the AI chat assistant on the Coded Values landing page.
    /// </summary>
    public const string EnableCodedValuesAiChat = "FEATURE:EnableCodedValuesAiChat";

    /// <summary>
    /// Enables the inline "+" grade-create button on the Enroll Student dialog.
    /// Enabled by default as of the feature rollout. The action has a global
    /// side-effect (it creates a new GRADE coded value and a matching
    /// GradeLevel row), so tenants can opt out via the ConfigFlags page.
    /// </summary>
    public const string EnableGradeLevelSetupOnEnrollDialog = "FEATURE:EnableGradeLevelSetupOnEnrollDialog";
}
