namespace SchoolCollab.Core.Tenancy;

/// <summary>
/// Thrown when an authenticated user attempts to access or modify an entity
/// that belongs to a different tenant.
/// </summary>
public sealed class TenantAccessException : Exception
{
    public Guid ExpectedTenantId { get; }
    public Guid ActualTenantId { get; }
    public string EntityType { get; }
    public Guid EntityId { get; }

    public TenantAccessException(Guid expectedTenantId, Guid actualTenantId, string entityType, Guid entityId)
        : base($"Access denied: {entityType} '{entityId}' belongs to tenant {actualTenantId}, but the current user is in tenant {expectedTenantId}.")
    {
        ExpectedTenantId = expectedTenantId;
        ActualTenantId = actualTenantId;
        EntityType = entityType;
        EntityId = entityId;
    }
}
