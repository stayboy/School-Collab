using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Core.Domain;

/// <summary>
/// One student-facing content module on an assignment (WS-A1 / spec
/// §4.10): a video or guide the student consumes to satisfy the
/// assignment. Standalone tenant entity (FK declared once from the
/// <see cref="Assignment"/> aggregate side — NOT an owned type) so it
/// can be referenced by future per-ward progress rows (WS-D, out of
/// this round). The completion threshold + IsRequired are stored here
/// rather than on the ward progress so the assignment carries its own
/// gating defaults; the ward progress rows project against them.
/// </summary>
public sealed class ContentModule : ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion
{
    private ContentModule() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }

    public Guid AssignmentId { get; private set; }
    public ModuleType ModuleType { get; private set; }
    public string? Title { get; private set; }
    public string Url { get; private set; } = default!;
    public string? StoragePath { get; private set; }
    public int DisplayOrder { get; private set; }
    public int MinCompletionThresholdPercent { get; private set; }
    public bool IsRequired { get; private set; }
    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Factory — constructor hygiene only (decision (d));
    /// business rules live in <c>AssignmentContentValidator</c>.</summary>
    public static ContentModule Create(
        Guid tenantId,
        Guid assignmentId,
        ModuleType moduleType,
        string url,
        string? title,
        string? storagePath,
        int displayOrder,
        int minCompletionThresholdPercent = 100,
        bool isRequired = false)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (assignmentId == Guid.Empty)
            throw new ArgumentException("Assignment id is required.", nameof(assignmentId));
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("Url is required.", nameof(url));

        var now = DateTimeOffset.UtcNow;
        return new ContentModule
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AssignmentId = assignmentId,
            ModuleType = moduleType,
            Title = title?.Trim(),
            Url = url.Trim(),
            StoragePath = storagePath?.Trim(),
            DisplayOrder = displayOrder,
            MinCompletionThresholdPercent = minCompletionThresholdPercent,
            IsRequired = isRequired,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    /// <summary>Used only by <see cref="Assignment.ReorderModules"/>
    /// after the aggregate has re-indexed the inbound list (EC-7).</summary>
    internal void SetDisplayOrder(int displayOrder)
    {
        DisplayOrder = displayOrder;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
