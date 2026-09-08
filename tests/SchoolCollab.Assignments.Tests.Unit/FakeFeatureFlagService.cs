using SchoolCollab.Core.Features;

namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>Test double for <see cref="IFeatureFlagService"/> — the
/// shared fake used by the new lifecycle/approval handler tests
/// (WS-A2 / spec §7 Q2). Mirrors the standalone-file shape of the
/// <c>FakeNotificationPolicyResolver</c> precedent. <see cref="IsEnabled"/>
/// (and the async variant) read <see cref="IsEnabled"/>; <see cref="GetAllFlags"/>
/// returns a single-entry dictionary so bulk reads have a stable
/// shape. Default OFF — tests opt into flag-on by setting the field
/// before the handler call.</summary>
public sealed class FakeFeatureFlagService : IFeatureFlagService
{
    /// <summary>Toggleable flag state for every key. Default false
    /// (the approval flag ships dark — spec §7 Q2).</summary>
    public bool IsEnabledValue { get; set; }

    public bool IsEnabled(string featureKey) => IsEnabledValue;

    public Task<bool> IsEnabledAsync(string featureKey, CancellationToken ct = default)
        => Task.FromResult(IsEnabledValue);

    public IDictionary<string, bool> GetAllFlags()
        => new Dictionary<string, bool> { ["FEATURE:RequireAssignmentApproval"] = IsEnabledValue };

    public Task<IReadOnlyDictionary<string, bool>> GetAllFlagsAsync(Guid? tenantId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyDictionary<string, bool>>(
            new Dictionary<string, bool> { ["FEATURE:RequireAssignmentApproval"] = IsEnabledValue });
}
