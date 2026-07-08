using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace SchoolCollab.Core.Auth;

/// <summary>
/// Stores the developer-selected tenant id for the dev tenant switcher (auth
/// disabled / <c>TestAuthHandler</c> mode only). Backed by the shared
/// <see cref="IDistributedCache"/> (Redis in the Aspire dev topology, shared
/// across all hosts) so a single selection made in the admin shell propagates
/// to every API host's <c>TestAuthHandler</c> on its next request — without
/// requiring the Admin→API <c>HttpClient</c> calls to carry a tenant header
/// (Blazor Server makes those calls server-side, so browser cookies don't
/// travel) and without touching OIDC.
/// </summary>
/// <remarks>
/// <para>This is a <b>single dev-user</b> mechanism: one global selection. It is
/// only consulted by <see cref="TestAuthHandler"/>, which is registered solely
/// when <c>FEATURE:DisableOIDCAuth</c> is enabled (development). In production
/// (OIDC on) this service is never read for auth — the real tenant id arrives
/// via the <c>tenant_id</c> claim from Keycloak.</para>
/// <para>When no tenant is selected, <see cref="GetSelectedTenantIdAsync"/>
/// returns <see langword="null"/> and <c>TestAuthHandler</c> falls back to its
/// configured default (<see cref="TestAuthHandlerOptions.TenantId"/>).</para>
/// </remarks>
public interface IDevTenantSelection
{
    /// <summary>
    /// Returns the currently selected dev tenant id, or <see langword="null"/>
    /// if none has been selected (or the stored value is missing/unparseable).
    /// </summary>
    Task<Guid?> GetSelectedTenantIdAsync(CancellationToken ct = default);

    /// <summary>
    /// Sets the selected dev tenant id, or clears it when <paramref name="tenantId"/>
    /// is <see langword="null"/>.
    /// </summary>
    Task SetSelectedTenantIdAsync(Guid? tenantId, CancellationToken ct = default);
}

/// <summary>
/// <see cref="IDistributedCache"/>-backed implementation of
/// <see cref="IDevTenantSelection"/>. The tenant id is stored as a UTF-8 GUID
/// string under a single fixed cache key with no expiration (the selection
/// persists for the dev session).
/// </summary>
internal sealed class DevTenantSelection(
    IDistributedCache cache,
    ILogger<DevTenantSelection> logger) : IDevTenantSelection
{
    private const string Key = "dev:tenant-selection";

    public async Task<Guid?> GetSelectedTenantIdAsync(CancellationToken ct = default)
    {
        var bytes = await cache.GetAsync(Key, ct);
        if (bytes is null || bytes.Length == 0)
        {
            logger.LogDebug("DevTenantSelection.GetSelectedTenantIdAsync: No tenant stored in cache (key={Key})", Key);
            return null;
        }

        var text = Encoding.UTF8.GetString(bytes);
        var result = Guid.TryParse(text, out var tenantId) ? tenantId : (Guid?)null;
        
        logger.LogDebug("DevTenantSelection.GetSelectedTenantIdAsync: Retrieved tenant {TenantId} from cache (key={Key})", 
            result?.ToString() ?? "null", Key);
        
        return result;
    }

    public async Task SetSelectedTenantIdAsync(Guid? tenantId, CancellationToken ct = default)
    {
        if (tenantId is null)
        {
            await cache.RemoveAsync(Key, ct);
            logger.LogDebug("DevTenantSelection.SetSelectedTenantIdAsync: Cleared tenant from cache (key={Key})", Key);
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(tenantId.Value.ToString());
        await cache.SetAsync(Key, bytes, ct);
        logger.LogDebug("DevTenantSelection.SetSelectedTenantIdAsync: Stored tenant {TenantId} in cache (key={Key})", 
            tenantId.Value, Key);
    }
}