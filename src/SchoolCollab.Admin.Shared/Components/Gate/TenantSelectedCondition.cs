using SchoolCollab.Admin.Shared.Services;

namespace SchoolCollab.Admin.Shared.Components.Gate;

/// <summary>True when the signed-in user has a real (non-default) tenant selected.</summary>
public sealed class TenantSelectedCondition(VisibleTenantService visibleTenant) : IGateCondition
{
    public async Task<bool> EvaluateAsync(CancellationToken ct = default)
        => (await visibleTenant.GetScopeAsync()).IsRealTenant;
}
