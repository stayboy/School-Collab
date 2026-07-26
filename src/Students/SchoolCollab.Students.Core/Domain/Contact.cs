using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Students.Core.Domain;

/// <summary>
/// A multi-channel contact owned by a student or guardian (spec §4.4). Replaces
/// the legacy <c>Student.ContactEmail</c>/<c>ContactPhone</c> columns. New
/// contacts default to unverified; subscription state lives on
/// <see cref="ContactSubscription"/>. Soft-delete blocks the contact.
/// </summary>
public sealed class Contact : ITenantEntity, IEntity, IAuditableEntity, ISoftDeletableEntity, IHasRowVersion
{
    private readonly List<ContactSubscription> _subscriptions = [];
    private Contact() { }

    public Guid Id { get; private set; }
    public ContactOwnerType OwnerType { get; private set; }
    public Guid OwnerId { get; private set; }
    public ContactChannel Channel { get; private set; }
    public string Value { get; private set; } = default!;
    public string? Label { get; private set; }
    public string? CountryCode { get; private set; }
    public bool IsPrimary { get; private set; }
    /// <summary>
    /// Display ordering hint for the owner's contact list (spec §4.9). Lower
    /// values render first; the contact with the smallest <c>DisplayOrder</c>
    /// is the owner's preferred contact. <c>IsPrimary</c> remains for
    /// backward compatibility during the additive phase; it is kept in sync
    /// with <c>DisplayOrder == 0</c> by <see cref="SetPrimary"/> and the
    /// repository's reorder APIs.
    /// </summary>
    public int DisplayOrder { get; private set; }
    public bool IsVerified { get; private set; }

    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }
    public Guid TenantId { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<ContactSubscription> Subscriptions => _subscriptions.AsReadOnly();

    public static Contact Create(
        ContactOwnerType ownerType, Guid ownerId, ContactChannel channel, string value, string? label, string? countryCode, bool isPrimary, int displayOrder = 0)
    {
        var now = DateTimeOffset.UtcNow;
        return new Contact
        {
            Id = Guid.NewGuid(),
            OwnerType = ownerType,
            OwnerId = ownerId,
            Channel = channel,
            Value = value.Trim(),
            Label = label?.Trim(),
            CountryCode = countryCode?.Trim(),
            IsPrimary = isPrimary,
            // DisplayOrder drives ordering; IsPrimary is kept in sync for
            // the additive phase. The first contact added becomes both
            // primary and order 0; subsequent contacts default to order 0
            // (i.e. also "primary" candidates) until the user reorders.
            DisplayOrder = displayOrder,
            IsVerified = false,
            IsDeleted = false,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Update(string value, string? label, string? countryCode)
    {
        Value = value.Trim();
        Label = label?.Trim();
        CountryCode = countryCode?.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Verify() { IsVerified = true; UpdatedAt = DateTimeOffset.UtcNow; }
    public void SetPrimary(bool isPrimary) { IsPrimary = isPrimary; UpdatedAt = DateTimeOffset.UtcNow; }
    /// <summary>Sets the contact's display order. Lower values render first.</summary>
    public void SetDisplayOrder(int order) { DisplayOrder = order; UpdatedAt = DateTimeOffset.UtcNow; }

    public void SoftDelete()
    {
        if (IsDeleted) return;
        IsDeleted = true;
        DeletedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Recover()
    {
        if (!IsDeleted) return;
        IsDeleted = false;
        DeletedAt = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
