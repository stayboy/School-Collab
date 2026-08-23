using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

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
/// <para><b>Fault isolation:</b> the selection read must never fail the request.
/// If the shared cache is unavailable (Redis down / connection blip), the handler
/// logs a warning and proceeds WITHOUT the header — the receiving
/// <see cref="TestAuthHandler"/> then falls back to its own
/// <see cref="IDevTenantSelection"/> or default tenant, exactly the pre-handler
/// behaviour, instead of every admin API call throwing from inside
/// <c>SendAsync</c>. Genuine request cancellation (<see cref="OperationCanceledException"/>,
/// e.g. the user navigating away mid-load) still propagates.</para>
/// <para>The API's <see cref="TestAuthHandler"/> honours this header (dev/TestAuth
/// mode only), so it cannot be spoofed in production OIDC where
/// <see cref="TestAuthHandler"/> is not registered.</para>
/// </remarks>
public sealed class TenantPropagationDelegatingHandler : DelegatingHandler
{
    private readonly IDevTenantSelection _devTenant;
    private readonly ILogger<TenantPropagationDelegatingHandler>? _logger;

    public TenantPropagationDelegatingHandler(
        IDevTenantSelection devTenant,
        ILogger<TenantPropagationDelegatingHandler>? logger = null)
    {
        _devTenant = devTenant;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Guid? selected = null;

        try
        {
            selected = await _devTenant.GetSelectedTenantIdAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The request itself is being cancelled — rethrow so the caller sees
            // the cancellation instead of a misleading "success without header".
            throw;
        }
        catch (Exception ex)
        {
            // Cache read failure (Redis down, connection reset, …): proceed
            // WITHOUT the header rather than failing the whole call here.
            _logger?.LogWarning(ex,
                "TenantPropagationDelegatingHandler: could not read the dev tenant selection; "
                + "sending {Method} {Uri} without x-tenant-id (the receiver will fall back to "
                + "its own IDevTenantSelection/default tenant)",
                request.Method, request.RequestUri);
        }

        if (selected is { } tenantId && tenantId != Guid.Empty)
        {
            request.Headers.Remove("x-tenant-id");
            request.Headers.Add("x-tenant-id", tenantId.ToString());
        }

        try
        {
            return await base.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // INTERNAL-DIAGNOSTIC: the caller's token is NOT cancelled (the enroll
            // submit passes CancellationToken.None — see
            // StudentsApiClient.EnrollStudentAsync), so a TaskCanceledException here
            // means the underlying typed HttpClient was disposed/torn down mid-call
            // (Blazor Server request-scope lifetime), NOT a token cancellation.
            if (_logger is { } logger)
            {
                logger.LogError(ex,
                    "TenantPropagationDelegatingHandler: {Method} {Uri} was CANCELED without the "
                    + "caller's token being cancelled — the request-scoped HttpClient was closed "
                    + "mid-call (TaskCanceledException). This is a component/host lifetime issue, "
                    + "not a tenant-selection failure.",
                    request.Method, request.RequestUri, ex is not null ? ex.ToString() : "?");
            }
            throw;
        }
    }
}

