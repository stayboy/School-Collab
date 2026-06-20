namespace SchoolCollab.Core.Tenancy;

public enum TenantType
{
    School,
    Organization,
    Team
}

public record TenantContext(Guid TenantId, string TenantName, TenantType Type);

public interface ITenantProvider
{
    TenantContext GetTenantContext();
}
