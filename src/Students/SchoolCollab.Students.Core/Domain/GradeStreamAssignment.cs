using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;
using SchoolCollab.Students.Core.Domain.Events;

namespace SchoolCollab.Students.Core.Domain;

/// <summary>
/// Bridge linking a <see cref="GradeLevel"/> to a stream coded value (a child of
/// the Settings <c>GRSTREAMS</c> parent). M:N — one stream coded value may be
/// offered by several grade levels.
/// </summary>
/// <remarks>
/// <para>This row <b>is</b> the grade↔stream link. Before this bridge existed the
/// link was the coded value's <c>gradeLevel</c> attribute; the attribute is no
/// longer written (only read by the one-time backfill seeder).</para>
/// <para><see cref="StreamCodedValueId"/> is a bare <see cref="Guid"/> with
/// <b>no FK</b>: coded values live physically in the Settings database — a
/// separate Npgsql database — so the reference is a cross-context operational
/// ref enforced only by the unique index, mirroring
/// <see cref="StudentEnrollment.StreamCodedValueId"/> and
/// <see cref="GradeLevel.CodedValueId"/>.</para>
/// <para>Strict tenant entity (global-tenant-filter.md §3.2): the "Tenant" query
/// filter is declared by <c>GradeStreamAssignmentConfiguration</c>.</para>
/// </remarks>
public sealed class GradeStreamAssignment : ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion
{
    /// <summary>The Settings coded-value parent whose children are the streams this bridge may reference.</summary>
    public const string CatalogueParentCode = "GRSTREAMS";

    /// <summary>The coded-value attribute holding a stream's version label (e.g. <c>5A</c>).</summary>
    public const string StreamVersionAttributeKey = "streamVersion";

    private readonly List<IDomainEvent> _domainEvents = [];

    private GradeStreamAssignment() { }

    public Guid Id { get; private set; }

    // Multi-tenancy: each row belongs to a tenant (global-tenant-filter.md §3.2 Strict).
    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }
    public Guid TenantId { get; private set; }

    /// <summary>The grade level this stream is offered by (FK → <c>grade_levels</c>, cascade).</summary>
    public Guid GradeLevelId { get; private set; }

    /// <summary>
    /// The stream coded value (a <c>GRSTREAMS</c> child) offered by
    /// <see cref="GradeLevelId"/>. Cross-database reference — no FK.
    /// </summary>
    public Guid StreamCodedValueId { get; private set; }

    /// <summary>
    /// Position of this offer in the grade's stream list. The <b>bridge row</b>
    /// is the ordering authority (the coded value's own
    /// <c>DisplayOrder</c> orders the cross-grade GRSTREAMS catalogue, a
    /// different concern). Contiguous 0..n-1 per grade once normalised; the
    /// migration backfills it from the listing order the UI rendered before the
    /// column existed.
    /// </summary>
    public int DisplayOrder { get; private set; }

    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>
    /// Creates a bridge row offering <paramref name="streamCodedValueId"/> for
    /// <paramref name="gradeLevelId"/>. <paramref name="displayOrder"/> defaults to
    /// 0 so existing callers (the migration seeder, tests) keep compiling; the
    /// assign flow stamps the append-at-end position explicitly.
    /// </summary>
    public static GradeStreamAssignment Create(
        Guid gradeLevelId, Guid streamCodedValueId, int displayOrder = 0)
    {
        var now = DateTimeOffset.UtcNow;
        var assignment = new GradeStreamAssignment
        {
            Id = Guid.NewGuid(),
            GradeLevelId = gradeLevelId,
            StreamCodedValueId = streamCodedValueId,
            DisplayOrder = displayOrder,
            CreatedAt = now,
            UpdatedAt = now,
        };

        assignment._domainEvents.Add(
            new GradeStreamAssignedEvent(assignment.Id, gradeLevelId, streamCodedValueId));
        return assignment;
    }

    /// <summary>
    /// Moves this offer to <paramref name="displayOrder"/>. The reorder handler
    /// owns the swap semantics (normalise, then swap the mover with the row at the
    /// requested position); this only stamps the value and the audit timestamp.
    /// </summary>
    public void SetDisplayOrder(int displayOrder)
    {
        if (DisplayOrder == displayOrder) return;
        DisplayOrder = displayOrder;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}
