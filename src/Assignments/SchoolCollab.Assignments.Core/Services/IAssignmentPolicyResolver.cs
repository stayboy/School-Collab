using SchoolCollab.Core.AssignmentPolicies;

namespace SchoolCollab.Assignments.Core.Services;

/// <summary>
/// Cross-bounded-context resolver for the <b>effective</b> assignment policy of an assignment
/// being created, published, or scheduled (<c>documents/solution/assignment-policy-fields.md</c>
/// §5). The interface lives in Assignments.Core; the implementation (an HTTP client to the
/// Settings + Students APIs) lives in Assignments.Api so this module stays free of HTTP.
/// Mirrors <see cref="INotificationPolicyResolver"/>, and replaces the former single-boolean
/// signature-default resolver (WS-C1).
///
/// <para>Resolution: the tenant-global default (Settings API) with an optional per-grade override
/// (Students API) merged per field by the pure
/// <see cref="IEffectiveAssignmentPolicyResolver"/>. When <paramref name="gradeLevelId"/> is null
/// only the tenant-global default applies.</para>
///
/// <para><b>Failure posture.</b> Best-effort and fail-open: any fetch failure degrades to
/// "nothing configured", so the built-in defaults apply
/// (<see cref="SignatureRequirementMode.Disabled"/>, approval not required, uncapped). The
/// approval gate therefore never turns ON because a policy fetch failed — it is OR'd with
/// <c>FEATURE:RequireAssignmentApproval</c> at the call site, and the flag alone still gates. The
/// create/update snapshot seam reads the same degraded shape: <c>MandatoryReview</c> stays
/// <see langword="null"/> (the caller keeps its own default) and <c>ArchiveGraceDays</c> stays
/// <see langword="null"/> (the caller keeps the built-in 30-day retention floor).</para>
/// </summary>
public interface IAssignmentPolicyResolver
{
    /// <summary>
    /// Resolves the effective assignment policy for <paramref name="gradeLevelId"/> (null = the
    /// tenant-global default only). Never throws for a transport failure.
    /// </summary>
    Task<EffectiveAssignmentPolicy> ResolveAsync(
        Guid? gradeLevelId, CancellationToken cancellationToken = default);
}
