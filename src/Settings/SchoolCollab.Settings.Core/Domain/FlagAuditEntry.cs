using SchoolCollab.Core.Data;

namespace SchoolCollab.Settings.Core.Domain;

/// <summary>
/// Append-only audit log row for a single feature-flag mutation. Carries who
/// changed what, when, the before/after enabled state, and the operator's
/// reason. Never updated or deleted — the admin audit endpoint is read-only.
/// </summary>
public sealed class FlagAuditEntry : IEntity, IAuditableEntity
{
    private FlagAuditEntry() { }

    public Guid Id { get; private set; }
    public Guid? TenantId { get; private set; }
    public Guid FeatureFlagId { get; private set; }
    public string FeatureFlagKey { get; private set; } = default!;
    public FlagChangeKind ChangeKind { get; private set; }
    public bool? PreviousIsEnabled { get; private set; }
    public bool? NewIsEnabled { get; private set; }
    public string? Reason { get; private set; }
    public string ActorId { get; private set; } = default!;
    public string ActorDisplayName { get; private set; } = default!;
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static FlagAuditEntry Create(
        Guid? tenantId,
        Guid featureFlagId,
        string featureFlagKey,
        FlagChangeKind changeKind,
        bool? previousIsEnabled,
        bool? newIsEnabled,
        string? reason,
        string actorId,
        string actorDisplayName)
    {
        var now = DateTimeOffset.UtcNow;
        return new FlagAuditEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FeatureFlagId = featureFlagId,
            FeatureFlagKey = featureFlagKey,
            ChangeKind = changeKind,
            PreviousIsEnabled = previousIsEnabled,
            NewIsEnabled = newIsEnabled,
            Reason = reason,
            ActorId = actorId,
            ActorDisplayName = actorDisplayName,
            OccurredAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }
}