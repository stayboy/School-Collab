using Microsoft.Extensions.Logging;
using SchoolCollab.Admin.Shared.Services;
using SchoolCollab.Core.Auth;

namespace SchoolCollab.Admin.Services;

/// <summary>
/// Resolves the tenant the admin portal opens on, and publishes it to the shared dev
/// tenant store so that the Blazor circuit's own pages and every API host it calls agree
/// on the same tenant.
/// </summary>
/// <remarks>
/// <para>
/// The chain is <b>explicit switcher pick → the operator's recorded preference → the
/// platform's designated default</b>, with the system / no-tenant context as the final
/// fallthrough. The first link is read from <see cref="IDevTenantSelection"/>; the second
/// comes from the Settings API; the third is supplied by the caller from the tenant list
/// it already loaded, so a portal load costs one extra round trip, not two.
/// </para>
/// <para>
/// <b>The resolved default is written to the store's <i>applied default</i> slot, never to
/// the selection slot.</b> The selection means "the operator chose this"; overwriting it
/// would make a later change to the platform default permanently invisible, because
/// "no explicit pick" and "the operator picked this tenant" would become the same state.
/// </para>
/// <para>
/// This is a dev/TestAuth-mode mechanism. Under OIDC the tenant is claim-authoritative and
/// this resolver is never consulted.
/// </para>
/// </remarks>
public sealed class AdminDefaultTenantResolver(
    TenantsApiClient tenants,
    IDevTenantSelection devTenant,
    ILogger<AdminDefaultTenantResolver> logger)
{
    /// <summary>
    /// Reads the signed-in operator's recorded preference, or <see langword="null"/> when none
    /// is recorded. A returned <see cref="Guid.Empty"/> is a real answer: an explicit preference
    /// for the system / no-tenant context.
    /// </summary>
    public async Task<Guid?> GetPreferenceAsync(CancellationToken ct = default)
        => (await tenants.GetMyDefaultTenantAsync(ct)).DefaultTenantId;

    /// <summary>
    /// The platform's designated default from an already-loaded tenant list, or
    /// <see langword="null"/> when no tenant is designated.
    /// </summary>
    public static Guid? PlatformDefaultOf(IEnumerable<TenantDto> tenants)
        => tenants.FirstOrDefault(t => t.IsDefault)?.Id;

    /// <summary>
    /// Whether the Blazor circuit must be reloaded for a freshly-applied default to take effect.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A Blazor Server circuit's tenant claim is fixed when <c>HttpContext.User</c> is turned into
    /// the circuit's <c>AuthenticationState</c> at circuit start. A default applied during
    /// <c>OnInitializedAsync</c> therefore changes Redis but NOT the claim this circuit is already
    /// using — so <c>TenantGate</c> and the tenant-scoped API calls would keep behaving as if
    /// nothing had been applied, which is precisely the bug this feature would otherwise ship
    /// with. Reloading re-enters through a new request, where the claim is recomputed from the
    /// now-populated store.
    /// </para>
    /// <para>
    /// The reload cannot loop, because it is a <b>fixed point</b>: the condition compares the claim
    /// against the very value the resolver would apply on the next pass. After one reload the claim
    /// equals the applied default, so this returns false and the portal settles. It is a pure
    /// function because the alternative — asserting "a full page reload happened" in a component
    /// test — cannot distinguish reloading once from reloading forever.
    /// </para>
    /// <para>
    /// With an explicit pick there is nothing to reconcile: the claim was already computed from
    /// that pick when the circuit started.
    /// </para>
    /// </remarks>
    /// <param name="explicitSelection">The operator's explicit switcher pick; non-null means no reload is ever needed.</param>
    /// <param name="effectiveDefault">The default just applied (null = nothing designated).</param>
    /// <param name="claimTenant">The tenant this circuit's claim currently reports (<see cref="Guid.Empty"/> for none).</param>
    public static bool NeedsReload(Guid? explicitSelection, Guid? effectiveDefault, Guid claimTenant)
        => !explicitSelection.HasValue && claimTenant != (effectiveDefault ?? Guid.Empty);

    /// <summary>
    /// Computes the effective tenant for this portal load and publishes it as the applied
    /// default. Returns the effective tenant (null = nothing to fall back to, so the
    /// configured <c>TestAuthHandlerOptions.TenantId</c> stays in force).
    /// </summary>
    /// <param name="platformDefaultTenantId">
    /// The designated platform default, from <see cref="PlatformDefaultOf"/>.
    /// </param>
    public async Task<Guid?> ResolveAndApplyAsync(
        Guid? platformDefaultTenantId,
        CancellationToken ct = default)
    {
        var selected = await devTenant.GetSelectedTenantIdAsync(ct);
        if (selected.HasValue)
        {
            // An explicit pick already wins in every host (TestAuthHandler and
            // TenantPropagationDelegatingHandler both consult the selection slot first),
            // so there is nothing to publish — and publishing would blur the two slots.
            logger.LogDebug(
                "AdminDefaultTenantResolver: explicit selection {TenantId} is in force; leaving the applied default untouched",
                selected.Value);
            return selected.Value;
        }

        var preference = await GetPreferenceAsync(ct);
        var effective = preference ?? platformDefaultTenantId;

        await devTenant.SetAppliedDefaultTenantIdAsync(effective, ct);

        logger.LogDebug(
            "AdminDefaultTenantResolver: applied default {TenantId} (preference={Preference}, platformDefault={PlatformDefault})",
            effective?.ToString() ?? "null",
            preference?.ToString() ?? "null",
            platformDefaultTenantId?.ToString() ?? "null");

        return effective;
    }
}
