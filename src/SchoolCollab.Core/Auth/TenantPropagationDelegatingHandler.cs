using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SchoolCollab.Core.Auth;

/// <summary>
/// Propagates the developer-selected tenant from the admin shell to the API
/// hosts on every outgoing HTTP request via the <c>x-tenant-id</c> header.
/// </summary>
/// <remarks>
/// <para>The dev tenant switcher stores the selection in the shared
/// <see cref="IDevTenantSelection"/> (Redis in the Aspire topology). The
/// Blazor Server admin makes its API calls server-side, so the selection must
/// travel with each request. Reading the selection from the admin shell's own
/// <see cref="IDevTenantSelection"/> (which the shell just wrote) and sending it
/// as a header is topology-independent: it works even when the API host cannot
/// read the shared cache.</para>
/// <para>The API's <see cref="TestAuthHandler"/> honours this header (dev/TestAuth
/// mode only), so it cannot be spoofed in production OIDC where
/// <see cref="TestAuthHandler"/> is not registered.</para>
/// </remarks>
public sealed class TenantPropagationDelegatingHandler : DelegatingHandler
{
    private readonly IDevTenantSelection _devTenant;

    public TenantPropagationDelegatingHandler(IDevTenantSelection devTenant)
    {
        _devTenant = devTenant;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var selected = await _devTenant.GetSelectedTenantIdAsync(cancellationToken);
        if (selected is { } tenantId && tenantId != Guid.Empty)
        {
            request.Headers.Remove("x-tenant-id");
            request.Headers.Add("x-tenant-id", tenantId.ToString());
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
