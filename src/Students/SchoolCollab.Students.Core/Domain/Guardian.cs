using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Students.Core.Domain;

/// <summary>
/// A student's guardian (spec §4.1). No email/phone columns — contacts live in
/// the <see cref="Contact"/> table. Soft-delete blocks the guardian but preserves
/// history, links, and contacts (no cascade).
/// </summary>
public sealed class Guardian : PersonDemographic, ITenantEntity, IEntity, IAuditableEntity, ISoftDeletableEntity, IHasRowVersion
{
    private readonly List<IDomainEvent> _domainEvents = [];
    private readonly List<GuardianNameHistory> _nameHistory = [];

    private Guardian() { }

    public Guid Id { get; private set; }
    public string? DisplayName { get; private set; }
    public string? Address { get; private set; }
    public Guid? CommunityId { get; private set; }

    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }
    public Guid TenantId { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public uint RowVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<GuardianNameHistory> NameHistory => _nameHistory.AsReadOnly();
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public static Guardian Create(
        Guid? titleCodedValueId,
        string firstName,
        string lastName,
        string? displayName,
        string? address,
        Guid? communityId,
        DateOnly? dateOfBirth = null,
        Guid? genderCodedValueId = null)
    {
        var now = DateTimeOffset.UtcNow;
        var guardian = new Guardian
        {
            Id = Guid.NewGuid(),
            Address = address?.Trim(),
            CommunityId = communityId,
            IsDeleted = false,
            CreatedAt = now,
            UpdatedAt = now
        };
        guardian.SetDemographics(titleCodedValueId, firstName, lastName, dateOfBirth, genderCodedValueId);
        guardian.DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        return guardian;
    }

    /// <summary>Appends the initial name-history snapshot (spec §16 Phase 4). Call after <see cref="ITenantEntity"/> tenant assignment.</summary>
    public void AddInitialNameHistory() =>
        _nameHistory.Add(GuardianNameHistory.Create(Id, TenantId, FirstName, LastName, DisplayName));

    /// <summary>Updates the guardian's name and appends a history row (spec §7).</summary>
    public void UpdateName(string firstName, string lastName, string? displayName)
    {
        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
        _nameHistory.Add(GuardianNameHistory.Create(Id, TenantId, FirstName, LastName, DisplayName));
    }

    public void UpdateProfile(string? address, Guid? communityId)
    {
        Address = address?.Trim();
        CommunityId = communityId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Full update used by <c>UpdateGuardian</c>. Appends a name-history row only when the name actually changes (spec §7).</summary>
    public void Update(Guid? titleCodedValueId, string firstName, string lastName, string? displayName, string? address, Guid? communityId, DateOnly? dateOfBirth = null, Guid? genderCodedValueId = null)
    {
        TitleCodedValueId = titleCodedValueId;
        Address = address?.Trim();
        CommunityId = communityId;
        DateOfBirth = dateOfBirth;
        GenderCodedValueId = genderCodedValueId;

        var trimmedFirst = firstName.Trim();
        var trimmedLast = lastName.Trim();
        var newDisplay = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        var currentDisplay = string.IsNullOrWhiteSpace(DisplayName) ? null : DisplayName;

        if (trimmedFirst != FirstName || trimmedLast != LastName || newDisplay != currentDisplay)
        {
            FirstName = trimmedFirst;
            LastName = trimmedLast;
            DisplayName = newDisplay;
            _nameHistory.Add(GuardianNameHistory.Create(Id, TenantId, FirstName, LastName, DisplayName));
        }

        UpdatedAt = DateTimeOffset.UtcNow;
    }

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

    public void ClearDomainEvents() => _domainEvents.Clear();
}
