namespace SchoolCollab.Settings.Contracts.Events;

/// <summary>
/// Published whenever a feature flag or a tenant override changes. Routed on
/// the <c>config</c> topic exchange with routing key <c>flags.changed</c>.
/// <para>
/// <see cref="TenantId"/> is null for a global-flag change and non-null for a
/// tenant-override change. Consumers (v1.1) evict the affected
/// <c>cfg:flags:{tenant|GLOBAL}</c> cache entry on receipt.
/// </para>
/// </summary>
public record FeatureFlagChanged(
    Guid FeatureFlagId,
    string FeatureFlagKey,
    Guid? TenantId,
    string ChangeKind,
    bool? NewIsEnabled,
    DateTimeOffset OccurredAt);