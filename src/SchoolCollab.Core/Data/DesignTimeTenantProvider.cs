using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Core.Data;

/// <summary>
/// Stand-in tenant provider used by design-time <see cref="IDesignTimeDbContextFactory{TContext}"/>
/// implementations. Returns an empty/system tenant so migrations can be generated without
/// an active HTTP request or claims principal.
/// </summary>
public sealed class DesignTimeTenantProvider : ITenantProvider
{
    public TenantContext GetTenantContext()
        => new(Guid.Empty, "DesignTime", TenantType.Organization);
}
