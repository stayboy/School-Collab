using SchoolCollab.Core.Data;

namespace SchoolCollab.Settings.Core.Domain;

public sealed class TenantCodedValueOverride : IEntity, IAuditableEntity
{
    private TenantCodedValueOverride() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid GlobalCodedValueId { get; private set; }
    public string? OverriddenName { get; private set; }
    public string? OverriddenDescription { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static TenantCodedValueOverride Create(
        Guid tenantId, 
        Guid globalCodedValueId, 
        string? name, 
        string? description)
    {
        var now = DateTimeOffset.UtcNow;
        return new TenantCodedValueOverride
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            GlobalCodedValueId = globalCodedValueId,
            OverriddenName = name,
            OverriddenDescription = description,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Update(string? name, string? description)
    {
        OverriddenName = name;
        OverriddenDescription = description;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}