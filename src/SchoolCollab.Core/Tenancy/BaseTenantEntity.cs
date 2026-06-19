namespace SchoolCollab.Core.Tenancy;

public interface ITenantEntity
{
    Guid TenantId { get; }
}

public abstract class BaseTenantEntity : ITenantEntity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
    public Guid TenantId { get; protected set; }

    protected BaseTenantEntity() { }

    protected BaseTenantEntity(Guid tenantId)
    {
        TenantId = tenantId;
    }
}

public abstract class BaseTenantEntityWithAudit : BaseTenantEntity
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
