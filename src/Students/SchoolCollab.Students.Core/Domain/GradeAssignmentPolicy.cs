using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Students.Core.Domain;

/// <summary>
/// Optional per-grade <b>override</b> for the guardian-signature requirement
/// (assignment-request-implementation-details.md §2 WS-C — the C1 prerequisite
/// bullet; spec §7 Q1). At most one row per (tenant, grade).
/// <see cref="RequiresSignatureDefault"/> being <see langword="null"/> means
/// <b>inherit the tenant default</b> (the effective value is the tenant's
/// <see cref="TenantAssignmentPolicy"/> default); a non-null value overrides it.
/// </summary>
public sealed class GradeAssignmentPolicy : BaseTenantEntityWithAudit, IHasRowVersion
{
    private GradeAssignmentPolicy() { }

    public Guid GradeLevelId { get; private set; }

    /// <summary>
    /// Grade-level override. <see langword="null"/> = inherit the tenant global
    /// default; non-null = override it for this grade.
    /// </summary>
    public bool? RequiresSignatureDefault { get; private set; }

    public uint RowVersion { get; private set; }

    /// <summary>
    /// Creates the override row for a grade. A null
    /// <paramref name="requiresSignatureDefault"/> stores "inherit the tenant
    /// default".
    /// </summary>
    public static GradeAssignmentPolicy Create(
        Guid tenantId, Guid gradeLevelId, bool? requiresSignatureDefault = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new GradeAssignmentPolicy
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            GradeLevelId = gradeLevelId,
            RequiresSignatureDefault = requiresSignatureDefault,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Replaces the override. A null <paramref name="requiresSignatureDefault"/>
    /// restores "inherit the tenant default". Stamps <see cref="UpdatedAt"/>.
    /// </summary>
    public void SetOverride(bool? requiresSignatureDefault)
    {
        RequiresSignatureDefault = requiresSignatureDefault;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
