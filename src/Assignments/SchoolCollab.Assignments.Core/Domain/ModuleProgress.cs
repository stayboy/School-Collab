using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Assignments.Core.Domain;

/// <summary>
/// WS-D1 (spec §3.3) — one ward's progress on one content module of an
/// assignment. Standalone tenant entity (the ar-4 standalone-entity
/// pattern): the <c>ContentModuleId</c> FK is declared once in
/// <c>ContentModuleConfiguration</c> (cascade on module delete) with no
/// navigation on either side. A single numeric <see cref="PercentComplete"/>
/// serves both module types (video = watch %, guide = scroll-complete
/// reported as percent). Recording is monotonic + idempotent
/// (decision (b)): replays and out-of-order heartbeat posts never
/// regress progress, and <see cref="CompletedAt"/> is stamped once the
/// percent first meets the module's completion threshold.
/// </summary>
public sealed class ModuleProgress : ITenantEntity, IEntity, IAuditableEntity, IHasRowVersion
{
    private ModuleProgress() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }

    public Guid AssignmentId { get; private set; }
    public Guid StudentId { get; private set; }
    public Guid ContentModuleId { get; private set; }
    /// <summary>Clamped 0–100 (video watch % / guide scroll-complete %).</summary>
    public int PercentComplete { get; private set; }
    /// <summary>Stamped the first time a report meets the module threshold; never un-stamped.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }
    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Factory — creates the first progress row. Applies the same
    /// clamping + threshold logic as <see cref="Record"/> so the initial
    /// report can already be complete.</summary>
    public static ModuleProgress Create(
        Guid tenantId,
        Guid assignmentId,
        Guid studentId,
        Guid contentModuleId,
        int percent,
        int minCompletionThresholdPercent)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        if (assignmentId == Guid.Empty)
            throw new ArgumentException("Assignment id is required.", nameof(assignmentId));
        if (studentId == Guid.Empty)
            throw new ArgumentException("Student id is required.", nameof(studentId));
        if (contentModuleId == Guid.Empty)
            throw new ArgumentException("Content module id is required.", nameof(contentModuleId));

        var now = DateTimeOffset.UtcNow;
        var clamped = Math.Clamp(percent, 0, 100);
        return new ModuleProgress
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AssignmentId = assignmentId,
            StudentId = studentId,
            ContentModuleId = contentModuleId,
            PercentComplete = clamped,
            CompletedAt = clamped >= minCompletionThresholdPercent ? now : null,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    /// <summary>WS-D1 (decision (b)) — monotonic, idempotent recording of
    /// the ward's progress. A report at or below the current percent is a
    /// no-op (replays / out-of-order heartbeats are safe); a higher report
    /// raises the percent and stamps <see cref="CompletedAt"/> the first
    /// time the threshold is met. Completion is never revoked.</summary>
    public void Record(int percent, int minCompletionThresholdPercent)
    {
        var clamped = Math.Clamp(percent, 0, 100);
        if (clamped <= PercentComplete)
            return;

        PercentComplete = clamped;
        if (CompletedAt is null && PercentComplete >= minCompletionThresholdPercent)
        {
            CompletedAt = DateTimeOffset.UtcNow;
        }
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
