using System.Threading;

namespace SchoolCollab.Core.Tenancy;

/// <summary>
/// Default implementation of <see cref="ITenantContextAccessor"/>.
/// </summary>
/// <remarks>
/// <para>Backed by the singleton <see cref="TenantProvider"/> (save/restore its
/// <see cref="AsyncLocal{T}"/> tenant context) plus a separate
/// <see cref="AsyncLocal{T}"/> guard flag read by
/// <see cref="SchoolCollab.Core.Data.ModuleDbContext"/>.</para>
/// <para><b>Nesting</b>: each <c>RunWith*</c> / <c>Suppress</c> scope captures the
/// prior value and restores it in <c>try/finally</c> / <c>Dispose</c>, so scopes
/// unwind correctly even when nested. See NFR-4.</para>
/// </remarks>
public sealed class TenantContextAccessor : ITenantContextAccessor
{
    private readonly TenantProvider _tenantProvider;

    /// <summary>
    /// The <see cref="AsyncLocal{T}"/> guard flag shared with
    /// <see cref="SchoolCollab.Core.Data.ModuleDbContext"/>. <see langword="true"/>
    /// when <c>SuppressTenantGuard</c> is active for the current async flow.
    /// </summary>
    /// <remarks>
    /// <see langword="internal"/> so <c>ModuleDbContext</c> (same assembly) can read
    /// it without an extra DI dependency. Defaults to <see langword="false"/> (guard
    /// active), which is the safe default.
    /// </remarks>
    internal static AsyncLocal<bool> GuardSuppressed { get; } = new();

    public TenantContextAccessor(TenantProvider tenantProvider)
    {
        _tenantProvider = tenantProvider;
    }

    /// <inheritdoc />
    public async Task<T> RunWithExplicitTenantAsync<T>(
        Guid? tenantId,
        Func<CancellationToken, Task<T>> callback,
        CancellationToken ct = default)
    {
        var previous = _tenantProvider.GetTenantContext();
        var wasDefault = previous.IsDefault;

        try
        {
            if (tenantId is { } tid && tid != Guid.Empty)
            {
                _tenantProvider.SetTenant(new TenantContext(tid, "Explicit", TenantType.Organization));
            }
            else
            {
                // null or Guid.Empty means "no tenant" (global / blueprint). Clear so
                // GetTenantContext returns the default (Guid.Empty) context.
                _tenantProvider.Clear();
            }

            return await callback(ct).ConfigureAwait(false);
        }
        finally
        {
            // Restore the prior context exactly. If the prior was the default (no
            // tenant), Clear() rather than re-stamping the synthesized default.
            if (wasDefault)
            {
                _tenantProvider.Clear();
            }
            else
            {
                _tenantProvider.SetTenant(previous);
            }
        }
    }

    /// <inheritdoc />
    public IDisposable SuppressTenantGuard()
    {
        var previous = GuardSuppressed.Value;
        GuardSuppressed.Value = true;
        return new GuardSuppressionScope(previous);
    }

    /// <summary>Whether the save-guard is currently suppressed for this async flow.</summary>
    internal static bool IsGuardSuppressed => GuardSuppressed.Value;

    private sealed class GuardSuppressionScope : IDisposable
    {
        private readonly bool _previous;
        private bool _disposed;

        internal GuardSuppressionScope(bool previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            GuardSuppressed.Value = _previous;
        }
    }
}
