using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Students.Core.Domain;

/// <summary>
/// Append-only audit log row for a single contact mutation (update or soft-delete).
/// Carries who changed what, when, the before/after values, and the operator's
/// reason. Never updated or deleted — the admin history endpoint is read-only.
/// </summary>
public sealed class ContactAuditEntry : ITenantEntity, IEntity, IAuditableEntity
{
    private ContactAuditEntry() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ContactId { get; private set; }
    public ContactOwnerType OwnerType { get; private set; }
    public Guid OwnerId { get; private set; }
    public ContactChangeKind ChangeKind { get; private set; }

    /// <summary>Channel before the change. For a delete this is the channel that was removed.</summary>
    public ContactChannel PreviousChannel { get; private set; }

    /// <summary>Value before the change.</summary>
    public string PreviousValue { get; private set; } = default!;

    /// <summary>Label before the change.</summary>
    public string? PreviousLabel { get; private set; }

    /// <summary>Country code before the change.</summary>
    public string? PreviousCountryCode { get; private set; }

    /// <summary>Channel after the change. Null for deletes.</summary>
    public ContactChannel? NewChannel { get; private set; }

    /// <summary>Value after the change. Null for deletes.</summary>
    public string? NewValue { get; private set; }

    /// <summary>Label after the change. Null for deletes.</summary>
    public string? NewLabel { get; private set; }

    /// <summary>Country code after the change. Null for deletes.</summary>
    public string? NewCountryCode { get; private set; }

    /// <summary>Operator-supplied reason for the change (required).</summary>
    public string Reason { get; private set; } = default!;

    /// <summary>Stable actor id (e.g. OIDC <c>sub</c>).</summary>
    public string ActorId { get; private set; } = default!;

    /// <summary>Human-readable actor name for the audit log.</summary>
    public string ActorDisplayName { get; private set; } = default!;

    /// <summary>When the change occurred (same as CreatedAt for rows created here).</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }

    public static ContactAuditEntry Create(
        Guid tenantId,
        Guid contactId,
        ContactOwnerType ownerType,
        Guid ownerId,
        ContactChangeKind changeKind,
        ContactChannel previousChannel,
        string previousValue,
        string? previousLabel,
        string? previousCountryCode,
        ContactChannel? newChannel,
        string? newValue,
        string? newLabel,
        string? newCountryCode,
        string reason,
        string actorId,
        string actorDisplayName)
    {
        var now = DateTimeOffset.UtcNow;
        return new ContactAuditEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ContactId = contactId,
            OwnerType = ownerType,
            OwnerId = ownerId,
            ChangeKind = changeKind,
            PreviousChannel = previousChannel,
            PreviousValue = previousValue,
            PreviousLabel = previousLabel,
            PreviousCountryCode = previousCountryCode,
            NewChannel = newChannel,
            NewValue = newValue,
            NewLabel = newLabel,
            NewCountryCode = newCountryCode,
            Reason = reason,
            ActorId = actorId,
            ActorDisplayName = actorDisplayName,
            OccurredAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }
}
