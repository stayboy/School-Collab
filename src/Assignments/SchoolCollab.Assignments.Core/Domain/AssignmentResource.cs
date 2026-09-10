using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Core.Domain;

/// <summary>
/// One AI-generation input on an assignment (WS-A1 / spec §3.2 /
/// FR-211): a Url, uploaded file, or video consumed by the question
/// generator (and surfaced in the create payload alongside attachments).
/// Standalone tenant entity (FK declared once from the <see cref="Assignment"/>
/// aggregate side — NOT an owned type) so the resource rows persist as
/// first-class records. Per WS-A1 the column list intentionally omits
/// a row version — these rows are write-once on the draft.
/// </summary>
public sealed class AssignmentResource : ITenantEntity, IEntity, IAuditableEntity
{
    private AssignmentResource() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }

    public Guid AssignmentId { get; private set; }
    public ResourceKind ResourceKind { get; private set; }
    public string? Url { get; private set; }
    public string? StoragePath { get; private set; }
    public string? DisplayName { get; private set; }
    public bool IncludedInGeneration { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Factory — constructor hygiene only (decision (d));
    /// the kind matrix is validator-owned.</summary>
    public static AssignmentResource Create(
        Guid tenantId,
        Guid assignmentId,
        ResourceKind resourceKind,
        string? url,
        string? storagePath,
        string? displayName,
        bool includedInGeneration = true)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (assignmentId == Guid.Empty)
            throw new ArgumentException("Assignment id is required.", nameof(assignmentId));

        var now = DateTimeOffset.UtcNow;
        return new AssignmentResource
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AssignmentId = assignmentId,
            ResourceKind = resourceKind,
            Url = url?.Trim(),
            StoragePath = storagePath?.Trim(),
            DisplayName = displayName?.Trim(),
            IncludedInGeneration = includedInGeneration,
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
