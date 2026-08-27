using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Settings.Core.Domain;

/// <summary>
/// Tenant-scoped override of a <see cref="FeatureFlag"/>. A null <see cref="IsEnabled"/>
/// means "explicitly inherit the global default"; a non-null value pins the flag
/// for this tenant. Follows <c>TenantCodedValueOverride</c>: tenant-scoped, FK to
/// the global entity, unique per (tenant, flag).
/// </summary>
public sealed class TenantFeatureFlagOverride : BaseTenantEntityWithAudit, IHasRowVersion
{
    private TenantFeatureFlagOverride() { }

    public Guid FeatureFlagId { get; private set; }
    public bool? IsEnabled { get; private set; }

    /// <summary>
    /// The tenant's value for a value-valued flag (<see cref="FlagKind.String"/>).
    /// Null = inherit the global <see cref="FeatureFlag.Value"/>.
    /// </summary>
    public string? Value { get; private set; }

    public string Reason { get; private set; } = default!;
    public DateTimeOffset? EffectiveFrom { get; private set; }
    public DateTimeOffset? EffectiveTo { get; private set; }
    public uint RowVersion { get; private set; }

    public static TenantFeatureFlagOverride Create(
        Guid tenantId,
        Guid featureFlagId,
        bool? isEnabled,
        string? value,
        string reason,
        DateTimeOffset? effectiveFrom,
        DateTimeOffset? effectiveTo)
    {
        ValidateReason(reason);
        ValidateWindow(effectiveFrom, effectiveTo);

        var now = DateTimeOffset.UtcNow;
        return new TenantFeatureFlagOverride
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FeatureFlagId = featureFlagId,
            IsEnabled = isEnabled,
            Value = value,
            Reason = reason.Trim(),
            EffectiveFrom = effectiveFrom,
            EffectiveTo = effectiveTo,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Boolean-only overload (no value) for backward compatibility.</summary>
    public static TenantFeatureFlagOverride Create(
        Guid tenantId,
        Guid featureFlagId,
        bool? isEnabled,
        string reason,
        DateTimeOffset? effectiveFrom,
        DateTimeOffset? effectiveTo)
        => Create(tenantId, featureFlagId, isEnabled, value: null, reason, effectiveFrom, effectiveTo);

    public void Update(bool? isEnabled, string? value, string reason, DateTimeOffset? effectiveFrom, DateTimeOffset? effectiveTo)
    {
        ValidateReason(reason);
        ValidateWindow(effectiveFrom, effectiveTo);

        IsEnabled = isEnabled;
        Value = value;
        Reason = reason.Trim();
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Boolean-only overload (no value) for backward compatibility.</summary>
    public void Update(bool? isEnabled, string reason, DateTimeOffset? effectiveFrom, DateTimeOffset? effectiveTo)
        => Update(isEnabled, value: null, reason, effectiveFrom, effectiveTo);

    /// <summary>
    /// True when the override is in effect right now, considering its optional
    /// effective window. Used by the resolver to skip not-yet-active or expired
    /// overrides.
    /// </summary>
    public bool IsInEffectAt(DateTimeOffset when)
    {
        if (EffectiveFrom is { } from && when < from)
        {
            return false;
        }

        if (EffectiveTo is { } to && when > to)
        {
            return false;
        }

        return true;
    }

    private static void ValidateReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A reason is required for every tenant override.", nameof(reason));
        }
    }

    private static void ValidateWindow(DateTimeOffset? effectiveFrom, DateTimeOffset? effectiveTo)
    {
        if (effectiveFrom is { } from && effectiveTo is { } to && to <= from)
        {
            throw new ArgumentException("EffectiveTo must be later than EffectiveFrom.", nameof(effectiveTo));
        }
    }
}