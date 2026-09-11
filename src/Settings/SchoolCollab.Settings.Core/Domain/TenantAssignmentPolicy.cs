using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Settings.Core.Domain;

/// <summary>
/// Per-tenant <b>global default</b> for the guardian-signature requirement
/// (assignment-request-implementation-details.md §2 WS-C — the C1 prerequisite
/// bullet; assignment-request-go-forward-breakdown.md §2, spec §7 Q1). One row per
/// tenant. A per-grade <see cref="GradeAssignmentPolicy"/> (Students context) may
/// override the effective default for a grade; this row is the fallback when no
/// override exists.
/// </summary>
public sealed class TenantAssignmentPolicy : BaseTenantEntityWithAudit, IHasRowVersion
{
    private TenantAssignmentPolicy() { }

    /// <summary>
    /// Whether newly created assignments require a guardian signature by default
    /// for this tenant. Defaults to <see langword="false"/>; the author may still
    /// override on a per-assignment basis.
    /// </summary>
    public bool RequiresSignatureDefault { get; private set; }

    public uint RowVersion { get; private set; }

    /// <summary>
    /// Creates the single policy row for <paramref name="tenantId"/>. Callers
    /// typically use <see cref="SetPolicy"/> on an existing row instead; this
    /// factory is for the initial insert.
    /// </summary>
    public static TenantAssignmentPolicy Create(Guid tenantId, bool requiresSignatureDefault = false)
    {
        var now = DateTimeOffset.UtcNow;
        return new TenantAssignmentPolicy
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RequiresSignatureDefault = requiresSignatureDefault,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Replaces the tenant default. Stamps <see cref="UpdatedAt"/>.
    /// </summary>
    public void SetPolicy(bool requiresSignatureDefault)
    {
        RequiresSignatureDefault = requiresSignatureDefault;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
