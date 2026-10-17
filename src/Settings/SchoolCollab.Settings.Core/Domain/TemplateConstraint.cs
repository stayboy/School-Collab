using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Settings.Core.Domain;

/// <summary>
/// One constraint dimension on a <see cref="TenantNotificationTemplate"/>. A template
/// applies to an assignment only when every constraint dimension intersects the
/// assignment's attributes (every-dimension intersect, spec §3.2 / Q15).
/// Constraint rows are owned children: replaced wholesale when their template is
/// updated, so they carry no audit columns or row version of their own — the parent
/// template and the <see cref="TemplateAuditEntry"/> log hold that history.
/// </summary>
public sealed class TemplateConstraint : BaseTenantEntity
{
    private TemplateConstraint() { }

    /// <summary>The owning template's id (FK).</summary>
    public Guid TemplateId { get; private set; }

    /// <summary>The audience/content dimension this row constrains.</summary>
    public TemplateConstraintDimension Dimension { get; private set; }

    /// <summary>The dimension value (grade level name, stream, subject, …).</summary>
    public string Value { get; private set; } = default!;

    /// <summary>Creates a constraint row.</summary>
    public static TemplateConstraint Create(
        Guid tenantId,
        Guid templateId,
        TemplateConstraintDimension dimension,
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Constraint value must not be empty.", nameof(value));

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