using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Settings.Core.Domain;

/// <summary>
/// One constraint dimension on a <see cref="TenantNotificationTemplate"/>. A template
/// applies to an assignment only when every constraint dimension intersects the
/// assignment's attributes (every-dimension intersect, spec 3.2 / Q15).
/// Constraint rows are owned children: replaced wholesale when their template is
/// updated, so they carry no audit columns or row version of their own - the parent
/// template and the <see cref="TemplateAuditEntry"/> log hold that history.
/// <para>
/// The row is <b>hybrid</b> like its parent (redesign spec 3.7): its <see cref="TenantId"/> always
/// mirrors the parent's scope, so a system template's constraints are visible to every tenant and a
/// tenant template's constraints are isolated to that tenant.
/// </para>
/// </summary>
public sealed class TemplateConstraint : IEntity, IHybridTenantEntity
{
    private TemplateConstraint() { }

    /// <inheritdoc />
    public Guid Id { get; private set; }

    /// <summary>The owning template's id (FK).</summary>
    public Guid TemplateId { get; private set; }

    /// <summary>The audience/content dimension this row constrains.</summary>
    public TemplateConstraintDimension Dimension { get; private set; }

    /// <summary>The dimension value (grade level name, stream, subject, ...).</summary>
    public string Value { get; private set; } = default!;

    /// <summary>The owning scope: the parent template's tenant, or null for the system blueprint.</summary>
    public Guid? TenantId { get; private set; }

    /// <inheritdoc />
    Guid? IHybridTenantEntity.TenantId
    {
        get => TenantId;
        set => TenantId = value;
    }

    /// <summary>Creates a constraint row in the same scope as its parent template.</summary>
    public static TemplateConstraint Create(
        Guid? tenantId,
        Guid templateId,
        TemplateConstraintDimension dimension,
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Constraint value must not be empty.", nameof(value));

        if (tenantId == Guid.Empty)
            throw new ArgumentException(
                "Guid.Empty is not a valid constraint scope. Use null for the system blueprint or a "
                + "real tenant id for a tenant-owned row.",
                nameof(tenantId));

        return new TemplateConstraint
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TemplateId = templateId,
            Dimension = dimension,
            Value = value.Trim(),
        };
    }
}