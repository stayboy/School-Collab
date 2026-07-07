namespace SchoolCollab.Core.Tenancy;

public enum TenantType
{
    School,
    Organization,
    Team
}

public record TenantContext(Guid TenantId, string TenantName, TenantType Type)
{
    /// <summary>
    /// The sentinel tenant id used when no real tenant is in scope (e.g. the
    /// dev tenant switcher's "(default tenant)" entry, or background workers
    /// that don't carry a tenant claim). Code that branches on "is there a real
    /// tenant?" should compare against <see cref="IsDefault"/> rather than
    /// <see cref="Guid.Empty"/> directly so the intent is explicit.
    /// </summary>
    public bool IsDefault => TenantId == Guid.Empty;
}

public interface ITenantProvider
{
    TenantContext GetTenantContext();
}
