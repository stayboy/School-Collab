namespace SchoolCollab.Core.Tenancy;

/// <summary>
/// The single sanctioned way to bypass the tenant query filter and the
/// <see cref="SchoolCollab.Core.Data.ModuleDbContext"/> save-guard.
/// </summary>
/// <remarks>
/// <para><b>Use cases</b> (all MUST cite the justifying spec section in a comment):
/// <list type="bullet">
/// <item>Admin / cross-tenant aggregate views — <c>RunWithExplicitTenantAsync(null, …)</c></item>
/// <item>Outbox dispatcher per-message tenant context — <c>RunWithExplicitTenantAsync(msg.TenantId, …)</c></item>
/// <item>Design-time factories, migration/seed services, CodedValue blueprint edit — <c>SuppressTenantGuard()</c></item>
/// <item>Workers enumerating tenants — <c>RunWithExplicitTenantAsync(tenantId, …)</c> per tenant</item>
/// </list></para>
/// <para>The implementation saves and restores the prior <see cref="TenantContext"/>
/// (and the guard flag) in <c>try/finally</c>, so nesting is correct and a missing
/// restore can never leak tenant A's context into tenant B's flow. See
/// <c>global-tenant-filter.md</c> FR-8 / FR-10 / NFR-4.</para>
/// </remarks>
public interface ITenantContextAccessor
{
    /// <summary>
    /// Runs <paramref name="callback"/> with <paramref name="tenantId"/> as the
    /// current tenant, restoring the prior context on exit.
    /// </summary>
    /// <param name="tenantId">
    /// The tenant to run under, or <see langword="null"/> to suppress the tenant
    /// context (global / blueprint operations).
    /// </param>
    /// <param name="callback">The work to perform under the explicit tenant.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <typeparam name="T">The callback's return type.</typeparam>
    /// <returns>The callback's result.</returns>
    Task<T> RunWithExplicitTenantAsync<T>(
        Guid? tenantId,
        Func<CancellationToken, Task<T>> callback,
        CancellationToken ct = default);

    /// <summary>
    /// Suppresses the <see cref="SchoolCollab.Core.Data.ModuleDbContext"/> save-guard
    /// for the current async flow until the returned <see cref="IDisposable"/> is
    /// disposed. Use in a <c>using</c> block.
    /// </summary>
    /// <returns>A scope that restores the prior guard state on dispose.</returns>
    IDisposable SuppressTenantGuard();
}
