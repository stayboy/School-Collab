using SchoolCollab.Core.Data;

namespace SchoolCollab.Settings.Core.Domain;

/// <summary>
/// Append-only audit log row for a single notification-template mutation. Mirrors
/// <see cref="FlagAuditEntry"/>: change kind + before/after subject/body/constraints
/// + timestamps + actor. Never updated or deleted.
/// </summary>
public sealed class TemplateAuditEntry : IEntity, IAuditableEntity
{
    private TemplateAuditEntry() { }

    public Guid Id { get; private set; }
    public Guid? TenantId { get; private set; }
    public Guid TemplateId { get; private set; }
    public string TemplateKey { get; private set; } = default!;   // "{kind}:{channel}"
    public TemplateChangeKind ChangeKind { get; private set; }

    /// <summary>Before/after subject (null for SMS templates).</summary>
    public string? PreviousSubject { get; private set; }
    public string? NewSubject { get; private set; }

    /// <summary>Before/after body.</summary>
    public string? PreviousBody { get; private set; }
    public string? NewBody { get; private set; }

    /// <summary>Before/after constraints summary (JSON-serialised).</summary>
    public string? PreviousConstraints { get; private set; }
    public string? NewConstraints { get; private set; }

    public string? Reason { get; private set; }
    public string ActorId { get; private set; } = default!;
    public string ActorDisplayName { get; private set; } = default!;
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static TemplateAuditEntry Create(
        Guid? tenantId,
        Guid templateId,
        string templateKey,
        TemplateChangeKind changeKind,
        string? previousSubject,
        string? newSubject,
        string? previousBody,
        string? newBody,
        string? previousConstraints,
        string? newConstraints,
        string? reason,
        string actorId,
        string actorDisplayName)
    {
        var now = DateTimeOffset.UtcNow;
        return new TemplateAuditEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TemplateId = templateId,
            TemplateKey = templateKey,
            ChangeKind = changeKind,
            PreviousSubject = previousSubject,
            NewSubject = newSubject,
            PreviousBody = previousBody,
            NewBody = newBody,
            PreviousConstraints = previousConstraints,
            NewConstraints = newConstraints,
            Reason = reason,
            ActorId = actorId,
            ActorDisplayName = actorDisplayName,
            OccurredAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }
}