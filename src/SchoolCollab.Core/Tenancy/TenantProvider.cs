using System.Threading;
using SchoolCollab.Core.Tenancy;

namespace SchoolCollab.Core.Tenancy;

public class TenantProvider : ITenantProvider
{
    private readonly AsyncLocal<TenantContext> _currentTenant = new();

    public void SetTenant(TenantContext context)
    {
        _currentTenant.Value = context;
    }

    public TenantContext GetTenantContext()
    {
        // Return a default 'System' context if no tenant is set to avoid nulls in the pipeline
        return _currentTenant.Value ?? new TenantContext(Guid.Empty, "System", TenantType.Organization);
    }

    public void Clear()
    {
        _currentTenant.Value = null;
    }
}
