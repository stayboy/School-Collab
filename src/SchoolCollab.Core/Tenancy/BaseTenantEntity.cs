using SchoolCollab.Core.Data;

namespace SchoolCollab.Core.Tenancy;

public interface ITenantEntity
{
    Guid TenantId { get; set; }
}

public abstract class BaseTenantEntity : IEntity, ITenantEntity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    // Explicit interface mapping so the reflection helper in TenantEntityExtensions
    // can set TenantId on any ITenantEntity, including those that have a private
    // setter on their own TenantId property.
    Guid ITenantEntity.TenantId { get => TenantId; set => TenantId = value; }

    protected BaseTenantEntity() { }

    protected BaseTenantEntity(Guid tenantId)
    {
        TenantId = tenantId;
    }
}

public abstract class BaseTenantEntityWithAudit : BaseTenantEntity, IAuditableEntity, ISoftDeletableEntity
{
    public DateTimeOffset CreatedAt { get; protected set; }
    public DateTimeOffset UpdatedAt { get; protected set; }
    public bool IsDeleted { get; protected set; }
    public DateTimeOffset? DeletedAt { get; protected set; }

    protected BaseTenantEntityWithAudit() { }

    protected BaseTenantEntityWithAudit(Guid tenantId)
    {
        TenantId = tenantId;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkAsDeleted()
    {
        IsDeleted = true;
        DeletedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Recover()
    {
        IsDeleted = false;
        DeletedAt = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
