namespace SchoolCollab.Core.AssignmentPolicies;

/// <summary>
/// Resolves the effective assignment policy for a grade by merging the tenant's global
/// default with the grade's optional override: a non-null grade field wins, otherwise the
/// tenant value is used (which may itself be null = built-in default). Pure and side-effect
/// free — Assignments.Api wraps it in an HTTP-backed
/// <c>IAssignmentPolicyResolver</c>.
/// </summary>
public interface IEffectiveAssignmentPolicyResolver
{
    /// <summary>
    /// Merges <paramref name="gradeOverride"/> over <paramref name="tenantDefault"/> per field
    /// and reports the per-field source flags. Either argument may be null (treated as
    /// "nothing configured").
    /// </summary>
    EffectiveAssignmentPolicy Resolve(
        AssignmentPolicyFields? tenantDefault,
        AssignmentPolicyFields? gradeOverride);
}
