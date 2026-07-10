using Microsoft.AspNetCore.Components;
using SchoolCollab.Admin.Shared.Components.Gate;
using SchoolCollab.Admin.Shared.Services;

namespace SchoolCollab.Admin.Shared.Components;

/// <summary>
/// Declarative, reusable gate for strict-tenant UI surfaces — the Blazor-idiomatic
/// analog of [Authorize], but for tenant *visibility* instead of authentication.
/// Derives from <see cref="GateBase"/> and supplies a single tenant-selected condition.
/// </summary>
public class TenantGate : GateBase
{
    /// <summary>Hide (default) or disable the gated content when no real tenant is selected.</summary>
    [Parameter] public TenantGateMode Mode { get; set; } = TenantGateMode.Hide;

    [Inject] private VisibleTenantService VisibleTenant { get; set; } = default!;

    protected override void OnParametersSet()
    {
        _mode = Mode == TenantGateMode.Disable ? GateMode.Disable : GateMode.Hide;
    }

    protected override Task<IReadOnlyList<IGateCondition>> GetConditionsAsync()
        => Task.FromResult<IReadOnlyList<IGateCondition>>(new IGateCondition[] { new TenantSelectedCondition(VisibleTenant) });

    /// <summary>How <see cref="TenantGate"/> behaves when no real tenant is selected.</summary>
    public enum TenantGateMode
    {
        /// <summary>Render ChildContent only with a real tenant; show Fallback (or a default banner) otherwise.</summary>
        Hide,

        /// <summary>Always render ChildContent, but disable it (via a disabled &lt;fieldset&gt;) without a real tenant.</summary>
        Disable
    }
}
