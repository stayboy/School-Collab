using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Settings.Core.Domain;

/// <summary>
/// A single tenant-specific override of one <see cref="EntityCodeSegment"/>
/// field on one <see cref="EntityCodeRule"/>. Delta model: only the overridden
/// fields are stored, so a tenant can change one segment's <c>FixedText</c>
/// or <c>ResetPeriod</c> without redefining the whole template (spec §4.12).
/// <para>
/// Strict tenant entity (every row belongs to a real tenant — the default
/// sentinel <c>Guid.Empty</c> may also own overrides when a developer
/// wants to scope-test against the no-tenant "default" path). The active
/// rule at generation time is the row the generator finds by Code; if that
/// rule is shared (TenantId = null), tenant overrides layer on top. If
/// the active rule is tenant-owned, no overrides apply (the tenant already
/// has full control over their own rule's segments).
/// </para>
/// <para>
/// Sequence state (<see cref="EntityCodeSegment.LastSequence"/>,
/// <see cref="EntityCodeSegment.LastPrefix"/>,
/// <see cref="EntityCodeSegment.LastPeriodBucket"/>) is <b>not</b>
/// overridable — all tenants on a rule share the same sequence counter in
/// v1 (per §1.2 non-goal). Overrides only change the FORMAT, not the
/// continuation.
/// </para>
/// </summary>
public sealed class TenantEntityCodeRuleOverride : IEntity, IAuditableEntity, ITenantEntity
{
    private TenantEntityCodeRuleOverride() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid GenerationRuleId { get; private set; }
    public Guid EntityCodeSegmentId { get; private set; }
    public OverrideField Field { get; private set; }

    /// <summary>
    /// Stringly-typed value (matches the wire format). Cast back to
    /// <see cref="int"/> at apply time for <see cref="OverrideField.MinWidth"/>
    /// and <see cref="OverrideField.ResetPeriod"/>; otherwise pass-through.
    /// </summary>
    public string Value { get; private set; } = default!;

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Explicit interface mapping so the ModuleDbContext save-guard (FR-8) can
    // read and auto-stamp TenantId via ITenantEntity while the domain setter
    // stays private (matches the TenantCodedValueOverride pattern).
    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set => TenantId = value;
    }

    public static TenantEntityCodeRuleOverride Create(
        Guid tenantId,
        Guid generationRuleId,
        Guid entityCodeSegmentId,
        OverrideField field,
        string value)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException(
                "TenantEntityCodeRuleOverride requires an explicit tenant id. " +
                "Use the system tenant via the ITenantProvider rather than Guid.Empty.",
                nameof(tenantId));
        if (generationRuleId == Guid.Empty)
            throw new ArgumentException("GenerationRuleId is required.", nameof(generationRuleId));
        if (entityCodeSegmentId == Guid.Empty)
            throw new ArgumentException("EntityCodeSegmentId is required.", nameof(entityCodeSegmentId));
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Override value is required.", nameof(value));

        var now = DateTimeOffset.UtcNow;
        return new TenantEntityCodeRuleOverride
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            GenerationRuleId = generationRuleId,
            EntityCodeSegmentId = entityCodeSegmentId,
            Field = field,
            Value = value.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void UpdateValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Override value is required.", nameof(value));
        Value = value.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Materialises a row that points at an existing persisted id (used by
    /// the PUT /overrides handler to carry "this field is an update, not an
    /// insert" through the entity boundary without reflection).
    /// </summary>
    internal static TenantEntityCodeRuleOverride Rehydrate(
        Guid id,
        Guid tenantId,
        Guid generationRuleId,
        Guid entityCodeSegmentId,
        OverrideField field,
        string value)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Rehydrate requires a non-empty id.", nameof(id));
        var now = DateTimeOffset.UtcNow;
        return new TenantEntityCodeRuleOverride
        {
            Id = id,
            TenantId = tenantId,
            GenerationRuleId = generationRuleId,
            EntityCodeSegmentId = entityCodeSegmentId,
            Field = field,
            Value = value.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
