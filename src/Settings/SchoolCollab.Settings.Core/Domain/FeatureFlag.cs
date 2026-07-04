using SchoolCollab.Core.Data;

namespace SchoolCollab.Settings.Core.Domain;

/// <summary>
/// Global blueprint for a feature flag. NOT tenant-scoped: this is the
/// default every tenant inherits unless a <see cref="TenantFeatureFlagOverride"/>
/// exists. Follows the established Global-blueprint → Tenant-override → Resolver
/// pattern (see <c>.skills/tenancy-override-pattern</c> and
/// <c>CodedValue</c>/<c>TenantCodedValueOverride</c>).
/// </summary>
public sealed class FeatureFlag : IEntity, IAuditableEntity, ISoftDeletableEntity, IHasRowVersion
{
    private FeatureFlag() { }

    public Guid Id { get; private set; }
    public string Key { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public FlagKind Kind { get; private set; }
    public bool IsEnabled { get; private set; }
    public bool IsArchived { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static FeatureFlag Create(string key, string name, string? description, bool isEnabled)
    {
        var now = DateTimeOffset.UtcNow;
        return new FeatureFlag
        {
            Id = Guid.NewGuid(),
            Key = NormalizeKey(key),
            Name = name.Trim(),
            Description = description,
            Kind = FlagKind.Boolean,
            IsEnabled = isEnabled,
            IsArchived = false,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Rename(string name, string? description)
    {
        Name = name.Trim();
        Description = description;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Enable()
    {
        if (IsEnabled)
        {
            return;
        }

        IsEnabled = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Disable()
    {
        if (!IsEnabled)
        {
            return;
        }

        IsEnabled = false;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetEnabled(bool enabled) => (enabled ? (Action)Enable : Disable)();

    public void Archive()
    {
        if (IsArchived)
        {
            return;
        }

        IsArchived = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Unarchive()
    {
        if (!IsArchived)
        {
            return;
        }

        IsArchived = false;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Delete()
    {
        if (IsDeleted)
        {
            return;
        }

        IsDeleted = true;
        DeletedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Recover()
    {
        if (!IsDeleted)
        {
            return;
        }

        IsDeleted = false;
        DeletedAt = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Canonical key shape: <c>FEATURE:&lt;AreaName&gt;</c>. Trims and
    /// upper-cases the area so <c>feature:enablex</c> and
    /// <c>FEATURE:EnableX</c> collide at the same row. The display text in the
    /// admin grid is humanised separately, so the canonical storage form can
    /// remain machine-friendly without hurting readability.
    /// </summary>
    public static string NormalizeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Feature flag key must not be empty.", nameof(key));
        }

        var trimmed = key.Trim();
        if (!trimmed.StartsWith("FEATURE:", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Feature flag key must start with 'FEATURE:' (got '{trimmed}').", nameof(key));
        }

        var area = trimmed["FEATURE:".Length..].Trim();
        if (area.Length == 0)
        {
            throw new ArgumentException("Feature flag key must include an area after 'FEATURE:'.", nameof(key));
        }

        return "FEATURE:" + area.ToUpperInvariant();
    }
}