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

    /// <summary>
    /// Enables demographic (age, gender) and single-active-enrollment validation
    /// in EnrollStudentHandler. Disabled by default for gradual rollout; existing
    /// active enrollments are grandfathered (validation applies to new enrollments
    /// only).
    /// </summary>
    public const string EnableEnrollmentValidation = "FEATURE:EnableEnrollmentValidation";

    /// <summary>
    /// Enables the activity-group management surface (groups, memberships,
    /// assignment targeting via SelectedGroups). Disabled by default so the
    /// feature ships dark behind the flag (spec activity-group-enrollment.md
    /// NFR-11). Gated in API endpoints and Admin UI via
    /// <c>IFeatureFlagService.IsEnabledAsync</c> / <c>&lt;FeatureFlagGate&gt;</c>.
    /// </summary>
    public const string EnableActivityGroups = "FEATURE:EnableActivityGroups";

    /// <summary>
    /// Value-valued (string) tenant setting selecting the academic-calendar
    /// subdivision (period-hierarchy-terms-semesters.md FR-H6). Value is one of
    /// <c>None</c> | <c>Terms</c> | <c>Semesters</c>; tenants override the global
    /// <c>None</c> default via a <see cref="TenantFeatureFlagOverride"/> carrying the value.
    /// </summary>
    public const string AcademicYearDivision = "FEATURE:AcademicYearDivision";
}
