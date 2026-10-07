using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Core.Domain;

/// <summary>
/// One authored targeting constraint on an assignment
/// (documents/specs/assignment-authoring-compartments.md §7.1 TGT-1) — the multi-constraint
/// replacement for the single-choice <see cref="TargetAudienceType"/> authored audience. The
/// assignment's grade(s) are the <see cref="Kind"/> = <c>GradeLevel</c> rows of this set (round
/// <c>drop-primary-grade</c> — there is no separately-authored primary grade). Rows are tenant child
/// records of the assignment (direct tenancy — operational data, the
/// <see cref="AssignmentActivityGroup"/> precedent); <see cref="RefId"/> is an operational
/// reference into the Students context (no cross-context DB FK), integrity enforced in code.
/// </summary>
public sealed class AssignmentTarget : ITenantEntity, IEntity, IAuditableEntity
{
    private AssignmentTarget() { }

    public Guid Id { get; private set; }

    // Multi-tenancy: strict tenant entity (global-tenant-filter.md §3.2), inherits the
    // assignment's tenant.
    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }
    public Guid TenantId { get; private set; }

    public Guid AssignmentId { get; private set; }
    public TargetKind Kind { get; private set; }

    /// <summary>The referenced id — a grade level, stream coded value, student or activity
    /// group id. <see langword="null"/> for <see cref="TargetKind.AllStudents"/> (TGT-2).</summary>
    public Guid? RefId { get; private set; }

    /// <summary>0..n-1 in the author's order (TGT-1); the chip list renders in it and the
    /// replacement path re-indexes it.</summary>
    public int DisplayOrder { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Creates one target row (TGT-1/TGT-2). <paramref name="refId"/> must be null exactly
    /// when the kind is <see cref="TargetKind.AllStudents"/> — and non-empty otherwise.
    /// </summary>
    public static AssignmentTarget Create(
        Guid tenantId,
        Guid assignmentId,
        TargetKind kind,
        Guid? refId,
        int displayOrder)
    {
        if (assignmentId == Guid.Empty)
            throw new ArgumentException("Assignment id is required.", nameof(assignmentId));

        if (kind == TargetKind.AllStudents)
        {
            if (refId is not null)
                throw new ArgumentException("An AllStudents target must not carry a reference id.", nameof(refId));
        }
        else if (refId is null || refId == Guid.Empty)
        {
            throw new ArgumentException($"A {kind} target requires a reference id.", nameof(refId));
        }

        if (displayOrder < 0)
            throw new ArgumentException("Display order must be zero or greater.", nameof(displayOrder));

        var now = DateTimeOffset.UtcNow;
        return new AssignmentTarget
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AssignmentId = assignmentId,
            Kind = kind,
            RefId = refId,
            DisplayOrder = displayOrder,
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
