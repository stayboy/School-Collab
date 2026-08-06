using SchoolCollab.Core.Data;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Settings.Core.Domain;

public sealed class TenantCodedValueOverride : IEntity, IAuditableEntity, ITenantEntity
{
    private TenantCodedValueOverride() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid GlobalCodedValueId { get; private set; }
    public string? OverriddenName { get; private set; }
    public string? OverriddenDescription { get; private set; }
    public string? OverriddenCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Explicit interface mapping so the ModuleDbContext save-guard (FR-8) can read
    // and auto-stamp TenantId via ITenantEntity while the domain setter stays private
    // (matches the BaseTenantEntity pattern). global-tenant-filter.md §3.2 classifies
    // this entity Strict (== current); the named "Tenant" filter is installed by
    // TenantCodedValueOverrideConfiguration.
    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set => TenantId = value;
    }

    public static TenantCodedValueOverride Create(
        Guid tenantId, 
        Guid globalCodedValueId, 
        string? name, 
        string? description,
        string? code = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new TenantCodedValueOverride
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            GlobalCodedValueId = globalCodedValueId,
            OverriddenName = name,
            OverriddenDescription = description,
            OverriddenCode = code,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Update(string? name, string? description, string? code = null)
    {
        OverriddenName = name;
        OverriddenDescription = description;
        OverriddenCode = code;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}