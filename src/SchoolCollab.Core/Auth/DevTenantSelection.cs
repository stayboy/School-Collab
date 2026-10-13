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
/// <para>
/// This is a <b>single dev-user</b> mechanism: one global selection. It is
/// only consulted by <see cref="TestAuthHandler"/>, which is registered solely
/// when <c>FEATURE:DisableOIDCAuth</c> is enabled (development). In production
/// (OIDC on) this service is never read for auth — the real tenant id arrives
/// via the <c>tenant_id</c> claim from Keycloak.
/// </para>
/// <para>
/// <b>Two slots, two meanings.</b> The <i>selection</i> is an operator's explicit
/// pick in the switcher, and <see langword="null"/> there means "nothing picked". The
/// <i>applied default</i> is what the admin portal resolved on load from its
/// preference → platform-default chain and pushed down so every host agrees; it is
/// written by the portal, never by a user action. Keeping them apart is what lets a
/// later change to the platform default take effect: had the portal written its
/// resolved default into the selection slot, "no explicit pick" would have become
/// indistinguishable from "the operator chose this tenant", and the fallback would
/// never be recomputed.
/// </para>
/// <para>
/// In both slots <see langword="null"/> means "not set" while <see cref="Guid.Empty"/>
/// is a meaningful value — the system / no-tenant context that shared-blueprint
/// coded-value editing requires.
/// </para>
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

    /// <summary>
    /// Returns the default tenant the admin portal resolved and applied on load, or
    /// <see langword="null"/> when the portal has not resolved one yet. Consulted only
    /// after <see cref="GetSelectedTenantIdAsync"/> has reported "no explicit pick".
    /// </summary>
    Task<Guid?> GetAppliedDefaultTenantIdAsync(CancellationToken ct = default);

    /// <summary>
    /// Publishes the default tenant the portal resolved, or clears it when
    /// <paramref name="tenantId"/> is <see langword="null"/>.
    /// </summary>
    Task SetAppliedDefaultTenantIdAsync(Guid? tenantId, CancellationToken ct = default);
}

/// <summary>
/// <see cref="IDistributedCache"/>-backed implementation of
/// <see cref="IDevTenantSelection"/>. Each slot is stored as a UTF-8 GUID string
/// under its own fixed cache key with no expiration (the values persist for the dev
/// session).
/// </summary>
internal sealed class DevTenantSelection(
    IDistributedCache cache,
    ILogger<DevTenantSelection> logger) : IDevTenantSelection
{
    /// <summary>Key holding the operator's explicit switcher pick.</summary>
    private const string SelectionKey = "dev:tenant-selection";

    /// <summary>Key holding the default the admin portal resolved on load.</summary>
    private const string AppliedDefaultKey = "dev:tenant-default";

    public Task<Guid?> GetSelectedTenantIdAsync(CancellationToken ct = default)
        => ReadAsync(SelectionKey, ct);

    public Task SetSelectedTenantIdAsync(Guid? tenantId, CancellationToken ct = default)
        => WriteAsync(SelectionKey, tenantId, ct);

    public Task<Guid?> GetAppliedDefaultTenantIdAsync(CancellationToken ct = default)
        => ReadAsync(AppliedDefaultKey, ct);

    public Task SetAppliedDefaultTenantIdAsync(Guid? tenantId, CancellationToken ct = default)
        => WriteAsync(AppliedDefaultKey, tenantId, ct);

    private async Task<Guid?> ReadAsync(string key, CancellationToken ct)
    {
        var bytes = await cache.GetAsync(key, ct);
        if (bytes is null || bytes.Length == 0)
        {
            logger.LogDebug("DevTenantSelection: No tenant stored in cache (key={Key})", key);
            return null;
        }

        var text = Encoding.UTF8.GetString(bytes);
        var result = Guid.TryParse(text, out var tenantId) ? tenantId : (Guid?)null;

        logger.LogDebug("DevTenantSelection: Retrieved tenant {TenantId} from cache (key={Key})",
            result?.ToString() ?? "null", key);

        return result;
    }

    private async Task WriteAsync(string key, Guid? tenantId, CancellationToken ct)
    {
        if (tenantId is null)
        {
            await cache.RemoveAsync(key, ct);
            logger.LogDebug("DevTenantSelection: Cleared tenant from cache (key={Key})", key);
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(tenantId.Value.ToString());
        await cache.SetAsync(key, bytes, ct);
        logger.LogDebug("DevTenantSelection: Stored tenant {TenantId} in cache (key={Key})",
            tenantId.Value, key);
    }
}

/// <summary>
/// The <see cref="IDevTenantSelection"/> used when the host registered no
/// <see cref="IDistributedCache"/> at all. The dev tenant switcher is a
/// convenience of the tenant-scoped DEV hosts, so a host without a cache loses
/// the switcher — never its ability to start.
/// </summary>
/// <remarks>
/// It behaves exactly like the real store with nothing selected: reads return
/// <see langword="null"/> ("no selection") and writes are discarded.
/// <see cref="TestAuthHandler"/> already reads the value as <c>Guid?</c> and only
/// overrides its configured default on <c>HasValue</c>, so this is the same
/// request outcome as the service being absent — the reason a host without a
/// cache needs no cache registration just to satisfy DI.
/// </remarks>
internal sealed class NullDevTenantSelection : IDevTenantSelection
{
    /// <summary>The shared instance — this implementation is stateless.</summary>
    public static NullDevTenantSelection Instance { get; } = new();

    private NullDevTenantSelection()
    {
    }

    public Task<Guid?> GetSelectedTenantIdAsync(CancellationToken ct = default)
        => Task.FromResult<Guid?>(null);

    public Task SetSelectedTenantIdAsync(Guid? tenantId, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<Guid?> GetAppliedDefaultTenantIdAsync(CancellationToken ct = default)
        => Task.FromResult<Guid?>(null);

    public Task SetAppliedDefaultTenantIdAsync(Guid? tenantId, CancellationToken ct = default)
        => Task.CompletedTask;
}
