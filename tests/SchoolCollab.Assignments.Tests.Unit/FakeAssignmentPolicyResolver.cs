using SchoolCollab.Assignments.Core.Services;
using SchoolCollab.Core.AssignmentPolicies;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// Test double for <see cref="IAssignmentPolicyResolver"/> — the
/// <c>FakeNotificationPolicyResolver</c> precedent. Defaults to
/// <see cref="BuiltInDefault"/>, which is what a caller sees when no policy is configured
/// <i>and</i> also what the HTTP resolver yields when a policy fetch fails; tests that need a
/// configured policy set <see cref="Policy"/> before invoking the handler.
/// </summary>
internal sealed class FakeAssignmentPolicyResolver : IAssignmentPolicyResolver
{
    /// <summary>
    /// Every field's built-in default (signature <see cref="SignatureRequirementMode.Disabled"/>,
    /// approval not required, both contact caps uncapped) — the fail-open result of the HTTP
    /// resolver when the settings/students fetches fail.
    /// </summary>
    public static readonly EffectiveAssignmentPolicy BuiltInDefault =
        new EffectiveAssignmentPolicyResolver().Resolve(tenantDefault: null, gradeOverride: null);

    /// <summary>The policy every <see cref="ResolveAsync"/> call returns.</summary>
    public EffectiveAssignmentPolicy Policy { get; set; } = BuiltInDefault;

    /// <summary>Every grade the caller asked for, in call order — the observable for "which
    /// policy-scope grade did this write resolve" and for "this path never resolves at all".</summary>
    public List<Guid?> RequestedGradeLevelIds { get; } = [];

    public Task<EffectiveAssignmentPolicy> ResolveAsync(
        Guid? gradeLevelId, CancellationToken cancellationToken = default)
    {
        RequestedGradeLevelIds.Add(gradeLevelId);
        return Task.FromResult(Policy);
    }
}
